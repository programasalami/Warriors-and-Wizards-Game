using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using Common.Network;
using Common.Utilities;
using GameServer.Game.Network.Messaging;


namespace GameServer.Game.Network;

// Handles all of the network socket communication
public class NetworkHandler {
    private static readonly Logger _log = new(typeof(NetworkHandler));

    public string IP { get; private set; }
    public User User { get; }
    public Socket Socket { get; private set; }

    // Static (2026-09-21 audit): it was an instance field, so each of the 1000 pre-allocated Users compiled its own copy.
    private static readonly Dictionary<PacketId, Func<IIncomingPacket>> _packetFactory =
        PacketLib.LoadIncoming()
            .ToDictionary(kvp => kvp.Key, kvp =>
                Expression.Lambda<Func<IIncomingPacket>>(
                    Expression.New(kvp.Value)).Compile());

    private readonly ConcurrentQueue<IIncomingPacket> _pendingReceive = [];

    private readonly SocketAsyncEventArgs _receiveSAEA;
    private readonly SocketReceiveState _receiveState;
    private readonly SocketAsyncEventArgs _sendSAEA;
    private readonly SocketSendState _sendState;

    public NetworkHandler(User user) {
        User = user;

        _sendState = new SocketSendState();
        _receiveState = new SocketReceiveState(0x20000);

        _sendSAEA = new SocketAsyncEventArgs();
        _sendSAEA.Completed += ProcessSend;

        _receiveSAEA = new SocketAsyncEventArgs();
        _receiveSAEA.Completed += ProcessReceive;
    }

    // Reset this instance's values for a possible future connection
    public void Reset() {
        IP = null;
        _pendingHandler = null;
        _pendingReceive.Clear();
        _sendState.Reset();
        _receiveState.Reset();
    }

    public void Setup(string ip, Socket socket) {
        IP = ip;
        Socket = socket;
        Socket.NoDelay = true;
    }

    public void WritePacket<T>(in T packet) where T : IOutgoingPacket, allows ref struct {
        // Console.WriteLine($"SENDING {packet.ID}");
        _sendState.WritePacket(packet, (byte)packet.ID);
    }

    public void SendSocketData() {
        if (User.State == ConnectionState.Disconnected || !Socket.Connected)
            return;

        if (!_sendState.TryBeginSend(_sendSAEA))
            return;

        if (!Socket.SendAsync(_sendSAEA))
            ProcessSend(null, _sendSAEA);
    }

    private void ProcessSend(object sender, SocketAsyncEventArgs args) {
        while (true) {
            if (User.State == ConnectionState.Disconnected) {
                User.Disconnect(reason: DisconnectReason.Unknown);
                break;
            }

            if (args.SocketError != SocketError.Success) {
                User.Disconnect($"Send Error: {args.SocketError}", DisconnectReason.NetworkError);
                break;
            }

            if (!_sendState.OnDataSent(args))
                return;

            if (Socket.SendAsync(_sendSAEA))
                break;
        }
    }

    public void StartReceive() {
        if (Socket == null || !Socket.Connected)
            return;

        _receiveState.PrepareSAEA(_receiveSAEA);

        if (!Socket.ReceiveAsync(_receiveSAEA)) // Completed synchronously
            ProcessReceive(null, _receiveSAEA);
    }

    private void ProcessReceive(object sender, SocketAsyncEventArgs args) {
        if (HandleReceive(args))
            StartReceive();
    }

    private bool HandleReceive(SocketAsyncEventArgs args) {
        if (User.State == ConnectionState.Disconnected || args.BytesTransferred == 0) {
            User.Disconnect(reason: DisconnectReason.Unknown);
            return false;
        }

        var error = args.SocketError;

        // Check for any errors during the operation
        if (error != SocketError.Success && error != SocketError.IOPending) {
            string msg = null;
            if (error != SocketError.ConnectionReset)
                msg = $"Receive SocketError.{error}";
            User.Disconnect(msg, DisconnectReason.NetworkError);
            return false;
        }

        _receiveState.OnDataReceived(args.BytesTransferred);

        while (true) {
            bool ready;
            try {
                ready = _receiveState.PacketReady();
            }
            catch (InvalidDataException ex) {
                User.Disconnect($"Invalid packet: {ex.Message}", DisconnectReason.NetworkError);
                return false;
            }

            if (!ready)
                break;

            var pktId = (PacketId)_receiveState.ReadPacket(out var rdr);
            try {
                // Console.WriteLine($"RECEIVING {pktId}");
                if (_packetFactory.TryGetValue(pktId, out var pktGen)) {
                    var pkt = pktGen();
                    pkt.Read(ref rdr);
                    _pendingReceive.Enqueue(pkt);
                }
            }
            catch (Exception ex) {
                _log.Error($"Error handling message {pktId}: {ex}");
            }
        }

        return true;
    }

    // A handler that is still awaiting something (Hello / Load / Create talk to the AccountServer). Until it finishes no further
    // packet of THIS user is handled, so the order Hello -> Load is kept; other users and the worlds are not held up. The code after
    // each await runs back on the game thread (GameThreadSynchronizationContext). 2026-09-21 audit (C5): this used to be
    // GetAwaiter().GetResult() on the game thread.
    private Task _pendingHandler;
    public const int WarnPacketsPerDrain = 200;
    public const int MaxPacketsPerDrain = 2000;

    public void HandleIncomingPackets() {
        if (_pendingHandler != null) {
            if (!_pendingHandler.IsCompleted)
                return;
            var finished = _pendingHandler;
            _pendingHandler = null;
            if (finished.IsFaulted) {
                _log.Error($"Error handling packet for user {User.Id}: {finished.Exception?.GetBaseException()}");
                User.Disconnect("Internal error handling packet", DisconnectReason.Failure);
                return;
            }
        }

        var handledThisDrain = 0;
        while (_pendingReceive.TryDequeue(out var pkt)) {
            if (User.State == ConnectionState.Disconnected || !Socket.Connected)
                break;

            // Packet budget (2026-09-21 audit, F26): an honest client sends a handful of packets per tick. Hundreds in one drain
            // are logged; thousands mean a flood and the connection is dropped.
            handledThisDrain++;
            if (handledThisDrain == WarnPacketsPerDrain)
                _log.Warn($"[PLAUSIBILITY] user {User.Id} sent {WarnPacketsPerDrain}+ packets in one tick (queued {_pendingReceive.Count} more)");
            if (handledThisDrain > MaxPacketsPerDrain) {
                _log.Warn($"[PLAUSIBILITY] user {User.Id} flooded {MaxPacketsPerDrain}+ packets in one tick: disconnecting");
                User.Disconnect("Packet flood", DisconnectReason.IllegalAction);
                break;
            }

            Task task;
            try {
                task = pkt.Handle(User);
            }
            catch (Exception ex) {
                _log.Error($"Error handling packet for user {User.Id}: {ex}");
                User.Disconnect("Internal error handling packet", DisconnectReason.Failure);
                break;
            }

            if (!task.IsCompleted) {
                _pendingHandler = task;
                break;
            }

            if (task.IsFaulted) {
                _log.Error($"Error handling packet for user {User.Id}: {task.Exception?.GetBaseException()}");
                User.Disconnect("Internal error handling packet", DisconnectReason.Failure);
                break;
            }
        }
    }
}