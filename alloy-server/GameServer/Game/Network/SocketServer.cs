#region

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Common.Resources.Config;
using Common.Utilities;

#endregion

namespace GameServer.Game.Network;

// TCP socket server
public static class SocketServer {
    
    private static readonly Logger _log = new(typeof(SocketServer));
    private static ConcurrentFactory<User> _userFactory;
    // Per-address connection counts + accept / refuse counters for the [STATS] line (ConnectionLedger, 2026-09-22). The old Dictionary was
    // touched from two threads, DELETED an address's whole count on any disconnect (so the cap never triggered) and a refused socket was never
    // closed. A stress test against this port left no trace in the log; now every refusal is counted and the first (then every 100th) is logged.
    public static ConnectionLedger Ledger { get; private set; }
    private static Socket _socket;

    // Start accepting connections
    public static void Start(int port, int maxConnections) {
        Ledger = new ConnectionLedger(GameServerConfig.Config.MaxClientsPerIP);
        _userFactory = new ConcurrentFactory<User>(maxConnections); // Used to prevent memory leaks

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _socket.Listen(3000); // Backlog is the max number of pending connections

        StartAccept();
    }

    private static void StartAccept() {
        var args = new SocketAsyncEventArgs();
        args.Completed += ProcessAccept;

        try {
            // Socket async methods return false when they complete synchronously, meaning that the Completed
            // event won't be invoked, so we have to manually call the callback ourselves
            if (!_socket.AcceptAsync(args))
                ProcessAccept(null, args);
        }
        catch { }
    }

    private static void ProcessAccept(object sender, SocketAsyncEventArgs args) {
        if (args.SocketError != SocketError.Success) // If error, recycle and continue
        {
            args.Dispose();
            StartAccept();
            return;
        }

        var skt = args.AcceptSocket;
        var ip = (skt?.RemoteEndPoint as IPEndPoint)?.Address.ToString();

        if (!Ledger.TryAdd(ip)) {
            // Over the per-address cap (or no address at all): close it, or the socket stays open for as long as the peer likes.
            if (ip != null && Ledger.ShouldWarn(ip))
                _log.Warn($"[FLOOD] refused a connection from {ip}: {Ledger.OpenFrom(ip)} already open (cap {Ledger.MaxPerAddress} per address), {Ledger.RefusalsFrom(ip)} refused from it so far");
            CloseQuietly(skt);
            args.Dispose();
            StartAccept();
            return;
        }

        var user = _userFactory.Pop(); // Give the connection a NetClient instance to communicate with
        if (user == null) {
            // Every player slot (MaxPlayers) is taken.
            Ledger.RefuseFull(ip);
            _log.Warn($"[FLOOD] refused a connection from {ip}: the server is full ({GameServerConfig.Config.MaxPlayers} slots)");
            CloseQuietly(skt);
            args.Dispose();
            StartAccept();
            return;
        }

        user.Setup(ip, skt);

        RealmManager.UserConnected(user);

        // Recycle the SAEA object
        args.Dispose();
        StartAccept();
    }

    private static void CloseQuietly(Socket skt) {
        try { skt?.Close(); } catch { }
    }

    public static void DisconnectUser(User user) {
        // Terminate connection with the Socket
        try {
            user.Network.Socket.Shutdown(SocketShutdown.Both);
            user.Network.Socket.Close();

        }
        catch { }
        Ledger?.Remove(user.Network.IP);

        // Recycle client instance
        user.Reset();

        _userFactory.Push(user);
    }
}