// Browser replacement for AlloyClient.Networking.Client: same public surface, but the game connection is a WebSocket (browsers cannot
// open raw TCP). On the VPS a websockify bridge (wss -> 127.0.0.1:2050) passes the exact same packet byte stream through, so the
// game server is unchanged: packets are still [int32 length][byte id][body], just carried in WebSocket binary frames of any size.
using System.Collections.Concurrent;
using System.Net.WebSockets;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Game;
using AlloyClient.Loading;
using AlloyClient.Logging;
using AlloyClient.Networking.Packets;
using AlloyClient.Networking.Packets.Outgoing;
using AlloyClient.Utils;
using Microsoft.Extensions.Logging;

namespace AlloyClient.Networking;

public enum ConnectionState {
    Disconnected,
    Connected
}

public static class Client {
    public const int RECV_BUFFER_SIZE = 0x40000;
    public const int SEND_BUFFER_SIZE = 0x10000;

    public static readonly ILogger Logger = ILogger.CreateLogger(nameof(Client));

    private static readonly ConcurrentQueue<IIncomingPacket> IncomingQueue = new();
    private static readonly ConcurrentQueue<byte[]> OutgoingQueue = new();

    public static ConnectionState State;
    public static bool IsReconnecting;

    private static ClientWebSocket _ws;
    private static CancellationTokenSource _cts;
    private static bool _sending;

    // Stream reassembly: WebSocket frames can split / merge packets exactly like TCP segments did.
    private static readonly byte[] _scratch = new byte[0x10000];   // single-threaded: one packet is serialised at a time
    private static byte[] _recv = new byte[RECV_BUFFER_SIZE];
    private static int _recvStart;
    private static int _recvLength;

    private static void Reset() {
        _recvStart = 0;
        _recvLength = 0;
        _sending = false;
        while (OutgoingQueue.TryDequeue(out _)) { }
    }

    public static async void Connect(string ip, ushort port) {
        Reset();
        var url = WarriorsWeb.WebHost.GameUrl;
        Logger.Log(LogLevel.Information, $"Connecting to {url}...");

        _cts = new CancellationTokenSource();
        var ws = new ClientWebSocket();
        _ws = ws;

        while (true) {
            try {
                await ws.ConnectAsync(new Uri(url), _cts.Token);
                break;
            } catch (OperationCanceledException) {
                return;
            } catch (Exception e) {
                Logger.Log(LogLevel.Warning, $"Failed to connect to server ({e.Message}). Retrying...");
                ws.Dispose();
                ws = new ClientWebSocket();
                _ws = ws;
                try { await Task.Delay(1500, _cts.Token); } catch (OperationCanceledException) { return; }
            }
        }

        State = ConnectionState.Connected;
        WorldLoad.Mark(WorldMilestone.Connected);
        Logger.Log(LogLevel.Information, "Connected to server.");

        SendHello();
        await ReceiveLoop(ws, _cts.Token);
    }

    private static async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct) {
        var chunk = new byte[0x10000];
        try {
            while (State == ConnectionState.Connected && ws.State == WebSocketState.Open) {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(chunk), ct);
                if (result.MessageType == WebSocketMessageType.Close) {
                    Disconnect("Remote host closed connection.");
                    return;
                }

                Append(chunk.AsSpan(0, result.Count));
                DrainPackets();
            }
        } catch (OperationCanceledException) {
            return;
        } catch (Exception e) {
            if (State == ConnectionState.Connected) Disconnect("Receive error: " + e.Message);
            return;
        }

        if (State == ConnectionState.Connected) Disconnect("Unknown");
    }

    private static void Append(ReadOnlySpan<byte> data) {
        if (_recvStart > 0) {   // compact
            if (_recvLength > 0) Buffer.BlockCopy(_recv, _recvStart, _recv, 0, _recvLength);
            _recvStart = 0;
        }
        if (_recvLength + data.Length > _recv.Length) Array.Resize(ref _recv, Math.Max(_recv.Length * 2, _recvLength + data.Length));
        data.CopyTo(_recv.AsSpan(_recvLength));
        _recvLength += data.Length;
    }

    private static void DrainPackets() {
        while (_recvLength >= 4) {
            var span = _recv.AsSpan(_recvStart, _recvLength);
            var length = BitConverter.ToInt32(span[..4]);   // little endian, same as SpanReader.ReadInt32
            if (length < 5 || length > _recv.Length) { Disconnect($"Invalid packet length: {length}"); return; }
            if (length > _recvLength) return;

            var rdr = new SpanReader(span);
            rdr.ReadInt32();
            var pktId = (PacketId)rdr.ReadByte();
            var body = new SpanReader(span.Slice(5, length - 5));
            try {
                var pkt = PacketUtils.CreateIncomingPacket(pktId);
                pkt.Read(ref body);
                IncomingQueue.Enqueue(pkt);
            } catch (Exception ex) {
                Logger.Log(LogLevel.Error, $"Error handling message {pktId}: {ex.Message}");
            }

            _recvStart += length;
            _recvLength -= length;
            if (_recvLength == 0) _recvStart = 0;
        }
    }

    public static void Tick() {
        SendPending();

        while (IncomingQueue.TryDequeue(out var packet)) {
            PacketLogger.LogPacket(packet);
            packet.Handle();
            packet.ReturnPacket();
        }
    }

    private static async void SendPending() {
        var ws = _ws;
        if (_sending || State == ConnectionState.Disconnected || ws == null || ws.State != WebSocketState.Open) return;
        if (OutgoingQueue.IsEmpty) return;

        _sending = true;
        try {
            while (OutgoingQueue.TryDequeue(out var bytes)) {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, _cts.Token);
            }
        } catch (Exception e) {
            if (State == ConnectionState.Connected) Disconnect("Send Error: " + e.Message);
        } finally {
            _sending = false;
        }
    }

    public static void QueuePacket(IOutgoingPacket pkt) {
        if (pkt.PacketId == PacketId.Unknown) return;
        Game.PerfCounters.PacketsQueuedThisFrame++;

        var buffer = _scratch;
        var writer = new SpanWriter(buffer.AsSpan()) { Position = 5 };
        pkt.Write(ref writer);
        var total = writer.Position;
        writer.Position = 0;
        writer.Write(total);
        writer.Write((byte)pkt.PacketId);
        OutgoingQueue.Enqueue(buffer.AsSpan(0, total).ToArray());
    }

    public static void Disconnect(string message = null) {
        if (State != ConnectionState.Disconnected) {
            State = ConnectionState.Disconnected;
            Reset();

            try { _cts?.Cancel(); _ws?.Abort(); _ws?.Dispose(); } catch { }

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
        hello.GameId = Data.FastTravel.GameIdFor(Settings.FastTravel.Value, GlobalData.Get<AccountData>());      // the FAST TRAVEL choice - mirror of Client.SendHello (this file replaces Client.cs wholesale in the web build; it sent -1 = Nexus until 2026-09-22)
        hello.Username = login.Username;
        hello.Password = login.Password;
        hello.MapJSON = "";
        QueuePacket(hello);
    }
}
