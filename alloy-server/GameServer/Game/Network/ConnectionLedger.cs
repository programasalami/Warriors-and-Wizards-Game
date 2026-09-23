using System.Net;

namespace GameServer.Game.Network;

// Who is connected to the game port, by address, and what the accept loop did (2026-09-22, after a stress test that left no trace in the log).
// Pure and thread-safe (the accept callback and the disconnect path run on different threads), tested in ConnectionLedgerTests.
//
// - TryAdd: one more socket from this address, unless it already has MaxPerAddress open. The local machine (127.0.0.1 / ::1) is never
//   capped: every BROWSER player arrives from the websocket bridge on the same box, so they all share that address (nginx caps those).
// - Remove: one socket from this address closed (SocketServer used to delete the whole entry, so the count could never reach a cap).
// - Counters for the [STATS] line: sockets open now, accepted / refused since the last report (TakeReport resets them).
// - ShouldWarn: the first refusal from an address and then every WarnEvery-th, so a flood writes a few lines, not thousands.
public sealed class ConnectionLedger {
    public const int WarnEvery = 100;

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _open = new();
    private readonly Dictionary<string, int> _refusals = new();
    private int _accepted;
    private int _refused;
    private int _refusedFull;

    public int MaxPerAddress { get; }

    public ConnectionLedger(int maxPerAddress) {
        MaxPerAddress = maxPerAddress < 1 ? 1 : maxPerAddress;
    }

    public static bool IsLocal(string ip) => ip == "127.0.0.1" || ip == "::1" || ip == "::ffff:127.0.0.1";

    public int OpenFrom(string ip) {
        lock (_lock) return _open.TryGetValue(ip, out var n) ? n : 0;
    }

    public int Open {
        get { lock (_lock) { var sum = 0; foreach (var n in _open.Values) sum += n; return sum; } }
    }

    // True = the socket may stay (and is now counted). False = over the cap for this address; the caller closes it.
    public bool TryAdd(string ip) {
        if (string.IsNullOrEmpty(ip))
            return false;
        lock (_lock) {
            _open.TryGetValue(ip, out var n);
            if (!IsLocal(ip) && n >= MaxPerAddress) {
                _refused++;
                return false;
            }
            _open[ip] = n + 1;
            _accepted++;
            return true;
        }
    }

    // The accept succeeded by address but there was no free player slot (MaxPlayers): count it and give the socket back.
    public void RefuseFull(string ip) {
        lock (_lock) {
            _refusedFull++;
            Remove(ip);
        }
    }

    public void Remove(string ip) {
        if (string.IsNullOrEmpty(ip))
            return;
        lock (_lock) {
            if (!_open.TryGetValue(ip, out var n))
                return;
            if (n <= 1) _open.Remove(ip);
            else _open[ip] = n - 1;
        }
    }

    public bool ShouldWarn(string ip) {
        lock (_lock) {
            _refusals.TryGetValue(ip, out var n);
            n++;
            _refusals[ip] = n;
            return n == 1 || n % WarnEvery == 0;
        }
    }

    public int RefusalsFrom(string ip) {
        lock (_lock) return _refusals.TryGetValue(ip, out var n) ? n : 0;
    }

    public readonly record struct Report(int Open, int Accepted, int Refused, int RefusedFull);

    // The counters since the last report, then reset (the open count is a level, not reset).
    public Report TakeReport() {
        lock (_lock) {
            var r = new Report(Open, _accepted, _refused, _refusedFull);
            _accepted = _refused = _refusedFull = 0;
            return r;
        }
    }
}
