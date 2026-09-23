using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using WaWClient.Data;
using WaWClient.Display;
using WaWClient.Game;
using WaWClient.Loading;
using WaWClient.Logging;
using WaWClient.Networking.Packets;
using WaWClient.Networking.Packets.Outgoing;
using WaWClient.Screens;
using WaWClient.Utils;
using Microsoft.Extensions.Logging;

namespace WaWClient.Networking;

public enum ConnectionState {
    Disconnected,
    Connected
}

public static class Client {
    public const int RECV_BUFFER_SIZE = 0x40000;
    public const int SEND_BUFFER_SIZE = 0x10000;

    public static readonly ILogger Logger = ILogger.CreateLogger(nameof(Client));

    private static readonly ConcurrentQueue<IIncomingPacket> IncomingQueue = new();

    public static ConnectionState State;

    public static bool IsReconnecting;

    private static readonly SocketAsyncEventArgs _receiveSAEA;
    private static readonly SocketReceiveState _receiveState;
    private static readonly SocketAsyncEventArgs _sendSAEA;
    private static readonly SocketSendState _sendState;

    private static Socket _socket;
    private static TcpClient _tcp;

    static Client() {
        _sendState = new SocketSendState();
        _receiveState = new SocketReceiveState();

        _sendSAEA = new SocketAsyncEventArgs();
        _sendSAEA.Completed += ProcessSend;

        _receiveSAEA = new SocketAsyncEventArgs();
        _receiveSAEA.Completed += ProcessReceive;
    }

    private static void Reset() {
        _sendState.Reset();
        _receiveState.Reset();
    }

    // How often a refused connection is retried before giving up (one try per second). It used to retry forever, silently.
    public const int MaxConnectAttempts = 10;

    // async void on purpose (fire-and-forget from GameScreen): everything inside is caught, so nothing can escape and kill the process.
    public static async void Connect(string ip, ushort port) {
        try {
            await ConnectAsync(ip, port);
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, $"Connect failed: {e.Message}");
            Disconnect("Could not connect to the game server");
        }
    }

    private static async Task ConnectAsync(string ip, ushort port) {
        Reset();

        _tcp = new TcpClient();
        _tcp.NoDelay = true;

        Logger.Log(LogLevel.Information, $"Connecting to {ip}:{port}...");

        for (var attempt = 1; ; attempt++) {
            try {
                await _tcp.ConnectAsync(ip, port);
                break;
            } catch (SocketException e) {
                if (e.SocketErrorCode == SocketError.ConnectionRefused && attempt < MaxConnectAttempts) {
                    Logger.Log(LogLevel.Warning, $"Failed to connect to server. Retrying ({attempt}/{MaxConnectAttempts})...");
                    await Task.Delay(1000);
                    continue;
                }

                Logger.Log(LogLevel.Error, $"Could not connect to {ip}:{port} after {attempt} attempt(s): {e.SocketErrorCode}");
                Disconnect("Could not connect to the game server");
                return;
            }
        }

        _socket = _tcp.Client;
        if (_socket == null) {
            Disconnect("Could not connect to the game server");
            return;
        }

        State = ConnectionState.Connected;
        WorldLoad.Mark(WorldMilestone.Connected);

        Logger.Log(LogLevel.Information, "Connected to server.");

        SendHello();

        _ = Task.Run(ReceiveLoop);
    }

    private static void ReceiveLoop() {
        while (true) {
            if (State == ConnectionState.Disconnected || !_socket.Connected) {
                Disconnect("Unknown");
                return;
            }

            _receiveState.PrepareSAEA(_receiveSAEA);

            if (_socket.ReceiveAsync(_receiveSAEA))
                break;

            if (!HandleReceive(_receiveSAEA))
                break;
        }
    }

    private static void ProcessReceive(object sender, SocketAsyncEventArgs args) {
        if (HandleReceive(args))
            ReceiveLoop();
    }

    private static bool HandleReceive(SocketAsyncEventArgs args) {
        if (State == ConnectionState.Disconnected || !_socket.Connected) {
            Disconnect("Unknown");
            return false;
        }

        // Check for any errors during the operation
        var error = args.SocketError;
        if (error != SocketError.Success && error != SocketError.IOPending) {
            string msg = null;
            if (error != SocketError.ConnectionReset) {
                msg = $"Receive SocketError.{error}";
            }

            Disconnect(msg);
            return false;
        }

        if (args.BytesTransferred == 0) {
            Disconnect("Remote host closed connection.");
            return false;
        }

        _receiveState.OnDataReceived(args.BytesTransferred);

        while (true) {
            bool ready;
            try {
                ready = _receiveState.PacketReady();
            } catch (InvalidDataException e) {
                // A corrupt length prefix used to throw out of the receive task and leave the client "connected" but deaf.
                Disconnect($"Invalid packet from server: {e.Message}");
                return false;
            }
            if (!ready)
                break;

            var pktId = (PacketId) _receiveState.ReadPacket(out var rdr);
            try {
                var pkt = PacketUtils.CreateIncomingPacket(pktId);
                pkt.Read(ref rdr);
                IncomingQueue.Enqueue(pkt);
            } catch (Exception ex) {
                Logger.Log(LogLevel.Error, $"Error handling message {pktId}: {ex.Message}");
            }
        }

        return true;
    }

    public static void Tick() {
        SendPendingPackets();

        while (IncomingQueue.TryDequeue(out var packet)) {
            PacketLogger.LogPacket(packet);

            packet.Handle();
            packet.ReturnPacket();
        }
    }

    private static void SendPendingPackets() {
        if (State == ConnectionState.Disconnected || !_socket.Connected) {
            return;
        }

        Game.PerfCounters.SendBufferBytes = _sendState.PendingBytes;
        if (!_sendState.TryBeginSend(_sendSAEA))
            return;

        if (!_socket.SendAsync(_sendSAEA))
            ProcessSend(null, _sendSAEA);
    }

    private static void ProcessSend(object sender, SocketAsyncEventArgs args) {
        while (true) {
            if (State == ConnectionState.Disconnected) {
                Disconnect("Unknown");
                break;
            }

            if (args.SocketError != SocketError.Success) {
                Disconnect($"Send Error: {args.SocketError}");
                break;
            }

            if (_sendState.OnDataSent(args))
                if (_socket.SendAsync(_sendSAEA))
                    break;

            break;
        }
    }

    public static void QueuePacket(IOutgoingPacket pkt) {
        if (pkt.PacketId == PacketId.Unknown)
            return;

        Game.PerfCounters.PacketsQueuedThisFrame++;
        bool written;
        lock (_sendState) {
            written = _sendState.WritePacket(pkt, (byte) pkt.PacketId);
        }

        if (!written) {
            Game.PerfCounters.PacketsDroppedTotal = _sendState.DroppedPackets;
            if (_sendState.DroppedPackets == 1 || _sendState.DroppedPackets % 1000 == 0) {
                Logger.Log(LogLevel.Warning, $"Outgoing packet {pkt.PacketId} dropped: send buffer at its limit ({_sendState.DroppedPackets} dropped so far)");
            }
        }
    }

    public static void Disconnect(string message = null) {
        if (State != ConnectionState.Disconnected) {
            State = ConnectionState.Disconnected;

            Reset();

            _tcp?.Close();
            _socket?.Close();

            Logger.Log(LogLevel.Information, $"Disconnecting client {(message != null ? $"({message})" : "")}");

            while (IncomingQueue.TryDequeue(out var pkt)) {
                pkt.ReturnPacket();
            }
        }

        Map.Reset();
        // Every drop - a server restart for an update included - asks the account server again which build it wants now, so the book says
        // "Update required" straight away instead of after a failed PLAY (2026-09-22; mirrored in the web shim).
        _ = AppEngine.VersionCheck.FetchAsync();
        LoaderFlows.ToCharacterList();
    }

    private static void SendHello() {
        var login = GlobalData.Get<LoginData>();
        var hello = Hello.CreatePacket();
        hello.BuildVersion = Settings.BuildVersion;
        hello.GameId = Data.FastTravel.GameIdFor(Settings.FastTravel.Value, GlobalData.Get<AccountData>());      // the FAST TRAVEL choice
        hello.Username = login.Username;
        hello.Password = login.Password;
        hello.MapJSON = "";
        QueuePacket(hello);
    }
}