using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Game.Network;
using Common.Utilities;

namespace GameServer.Game;

public class GameLogic {
    private static readonly Logger _log = new(typeof(GameLogic));
    
    public static RealmTime WorldTime;
    public static int TPS;

    private static readonly ConcurrentQueue<Action> _pendingActions = [];
    
    // Autosave: every playing user's character is snapshotted and queued for the AccountServer this often (plus on disconnect,
    // world switch and shutdown). 2026-09-21 audit (C4).
    public const int AutosaveIntervalMs = 60_000;

    private static volatile bool _stopRequested;
    public static readonly ManualResetEventSlim ShutdownCompleted = new(false);
    public static GameThreadSynchronizationContext SyncContext { get; private set; }

    public static void RequestStop() {
        if (!_stopRequested)
            _log.Info("Shutdown requested: saving characters and disconnecting players...");
        _stopRequested = true;
    }

    public static void Run(int mspt) {
        TPS = 1000 / mspt;
        SyncContext = new GameThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(SyncContext);
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch (Exception) { }   // a late tick hurts more than a late build job
        Systems.Persistence.CharacterSaver.Start();
        var nextAutosave = Stopwatch.GetTimestamp() + Stopwatch.Frequency * AutosaveIntervalMs / 1000;
        
        var lagMs = (int)(mspt * 1.5);
        if (OperatingSystem.IsWindows())
            TimeBeginPeriod(1);     // 1 ms sleeps; Windows' default 15.6 ms timer would make every tick late
        var sw = Stopwatch.StartNew();
        while (!_stopRequested) {
            Update();

            // Between ticks the thread used to spin at 100% of a core (2026-09-21 audit). Now it sleeps 1 ms at a time (network
            // I/O and queued actions still get drained every millisecond by Update above) and only spins for the last moment.
            var remaining = mspt - sw.Elapsed.TotalMilliseconds;
            if (remaining > 2) {
                Thread.Sleep(1);
                continue;
            }
            if (remaining > 0) {
                Thread.SpinWait(50);
                continue;
            }

            WorldTime.ElapsedMsDelta = (int)sw.ElapsedMilliseconds;
            WorldTime.TotalElapsedMs += sw.ElapsedMilliseconds;
            WorldTime.TickCountDecimal += WorldTime.ElapsedMsDelta / (float)mspt;
            if (WorldTime.TickCountDecimal > 1) {
                var ticks = (int)WorldTime.TickCountDecimal;
                WorldTime.TickCountDecimal -= ticks;
                WorldTime.TickCount += ticks;
            }

            if (WorldTime.ElapsedMsDelta >= lagMs) {
                Stats.LateTicks++;
                if (WorldTime.ElapsedMsDelta >= mspt * 5)
                    _log.Warn($"LAGGED | MsPT: {mspt} Elapsed: {WorldTime.ElapsedMsDelta}");
            }

            sw.Restart();

            var tickStart = Stopwatch.GetTimestamp();
            TickWorlds(WorldTime);
            Stats.Record(Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds, WorldTime.ElapsedMsDelta);

            if (Stopwatch.GetTimestamp() >= nextAutosave) {
                nextAutosave += Stopwatch.Frequency * AutosaveIntervalMs / 1000;
                AutosaveAll();
            }
        }

        // The loop is over: nobody pumps the game-thread queue any more, so awaits on this thread must not be routed into it
        // (the first shutdown test hung exactly here - the flush's own await sat in the dead queue forever).
        SynchronizationContext.SetSynchronizationContext(null);
    }

    private static void AutosaveAll() {
        var count = 0;
        foreach (var user in RealmManager.Users.Values) {
            try {
                if (user.GameInfo.State != GameState.Playing)
                    continue;
                Systems.Persistence.CharacterSaver.SaveUser(user);
                count++;
            }
            catch (Exception ex) {
                _log.Error($"Autosave failed for user {user.Id}: {ex}");
            }
        }
        if (count > 0)
            _log.Info($"Autosave: {count} character(s) queued.");
    }

    // Called on the main thread once Run has returned: every playing character is saved, every player is told, the sockets are
    // flushed, then the save queue is drained. Ctrl+C, SIGTERM and closing the console window all lead here (see Program).
    public static async Task ShutdownAsync() {
        try {
            var users = RealmManager.Users.Values.ToArray();
            foreach (var user in users) {
                try {
                    Systems.Persistence.CharacterSaver.SaveUser(user);
                    user.SendFailure(Session.Failure.DEFAULT, "The server is restarting. Please reconnect in a moment.");
                }
                catch (Exception ex) {
                    _log.Error($"Shutdown: could not save / disconnect user {user.Id}: {ex}");
                }
            }

            // Disconnect requests are queued actions; run them and push the goodbye packets out.
            DrainPendingActions();
            foreach (var user in users) {
                try { user.Network.SendSocketData(); } catch (Exception) { }
            }

            // Keep running queued actions while waiting (handler continuations, disconnects) and never capture a sync context here.
            var deadline = Environment.TickCount64 + 8000;
            while (Systems.Persistence.CharacterSaver.Pending > 0 && Environment.TickCount64 < deadline) {
                DrainPendingActions();
                await Task.Delay(20).ConfigureAwait(false);
            }
            DrainPendingActions();
            var flushed = Systems.Persistence.CharacterSaver.Pending == 0;
            _log.Info(flushed
                ? $"Shutdown: {users.Length} player(s) saved and disconnected. Bye."
                : $"Shutdown: save queue did not drain in time ({Systems.Persistence.CharacterSaver.Pending} pending).");
        }
        finally {
            ShutdownCompleted.Set();
        }
    }

    // Runs every queued action (packet-handler continuations, disconnects, world removals). Public so tests and Shutdown can pump it.
    public static int DrainPendingActions() {
        var ran = 0;
        while (_pendingActions.TryDequeue(out var act)) {
            ran++;
            try {
                act();
            }
            catch (Exception ex) {
                _log.Error($"Error running queued action: {ex}");
            }
        }
        return ran;
    }

    // Periodic health line (2026-09-21 audit, step 0): real ticks per second, how long a tick's work takes and how late ticks run.
    // One Info line every ReportEveryMs; cheap enough to stay on in production.
    public static class Stats {
        public const int ReportEveryMs = 10_000;
        private static int _ticks;
        public static int LateTicks;   // ticks that started more than 1.5x MsPT after the previous one (was a WARN line each; now a number in the stats line)
        private static double _workSumMs, _workMaxMs;
        private static long _intervalSumMs, _intervalMaxMs;
        private static long _lastReport = Stopwatch.GetTimestamp();

        public static void Record(double workMs, int intervalMs) {
            _ticks++;
            _workSumMs += workMs;
            if (workMs > _workMaxMs) _workMaxMs = workMs;
            _intervalSumMs += intervalMs;
            if (intervalMs > _intervalMaxMs) _intervalMaxMs = intervalMs;

            var elapsed = Stopwatch.GetElapsedTime(_lastReport).TotalMilliseconds;
            if (elapsed < ReportEveryMs)
                return;

            var tps = _ticks * 1000.0 / elapsed;
            var sockets = SocketServer.Ledger?.TakeReport() ?? default;      // raw connections on the game port, logged in or not (2026-09-22)
            _log.Info($"[STATS] tps={tps:F1} tick work avg={_workSumMs / _ticks:F2}ms max={_workMaxMs:F2}ms | tick interval avg={(double)_intervalSumMs / _ticks:F1}ms max={_intervalMaxMs}ms late={LateTicks} | users={RealmManager.Users.Count} worlds={RealmManager.Worlds.Count} queued={_pendingActions.Count} | sockets={sockets.Open} accepted={sockets.Accepted} refused={sockets.Refused + sockets.RefusedFull}");
            _ticks = 0;
            _workSumMs = _workMaxMs = 0;
            _intervalSumMs = _intervalMaxMs = 0;
            LateTicks = 0;
            _lastReport = Stopwatch.GetTimestamp();
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    public static void Enqueue(Action act) {
        _pendingActions.Enqueue(act);
    }

    private static void Update() {
        // Global and cheap - stays sequential, drained once before any per-world work.
        DrainPendingActions();

        // Stays sequential and plain foreach, deliberately: this runs in the
        // unthrottled busy-spin (every loop iteration, not gated by mspt like
        // TickWorlds is), so Parallel.ForEach's thread-pool dispatch overhead here
        // gets paid far more often than any per-world work it could parallelize is
        // worth - tried it, made CPU usage and lag measurably worse.
        foreach (var world in RealmManager.Worlds.Values) {
            try {
                world.Update();
            }
            catch (Exception ex) {
                _log.Error($"Error updating world {world.Id}: {ex}");
            }
        }

        // Kept sequential: a user doesn't necessarily have a settled World yet (Hello
        // and Load are still resolving which world they belong to), so this can't
        // cleanly shard by world the way Update/Tick above can.
        foreach (var user in RealmManager.Users.Values) {
            try {
                user.Network.HandleIncomingPackets();
                user.Network.SendSocketData();
            }
            catch (Exception ex) {
                _log.Error($"Error handling network I/O for user {user.Id}: {ex}");
            }
        }
    }

    private static void TickWorlds(RealmTime time) {
        // Every behavior/entity Tick signature downstream expects `ref RealmTime`, so
        // each parallel worker gets its own local copy to pass by ref into - avoids
        // handing multiple threads a ref to the single shared WorldTime field without
        // needing to touch any of those signatures.
        Parallel.ForEach(RealmManager.Worlds.Values, world => {
            var localTime = time;
            try {
                world.Tick(ref localTime);
            }
            catch (Exception ex) {
                _log.Error($"Error ticking world {world.Id}: {ex}");
            }
        });
    }
}