using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Common.Game;
using Common.Utilities;

namespace GameServer.Game;

public class GameLogic {
    private static readonly Logger _log = new(typeof(GameLogic));
    
    public static RealmTime WorldTime;
    public static int TPS;

    private static readonly ConcurrentQueue<Action> _pendingActions = [];
    
    public static void Run(int mspt) {
        TPS = 1000 / mspt;
        
        var lagMs = (int)(mspt * 1.5);
        var sw = Stopwatch.StartNew();
        while (true) {
            Update();

            if (sw.ElapsedMilliseconds < mspt)
                continue;

            WorldTime.ElapsedMsDelta = (int)sw.ElapsedMilliseconds;
            WorldTime.TotalElapsedMs += sw.ElapsedMilliseconds;
            WorldTime.TickCountDecimal += WorldTime.ElapsedMsDelta / (float)mspt;
            if (WorldTime.TickCountDecimal > 1) {
                var ticks = (int)WorldTime.TickCountDecimal;
                WorldTime.TickCountDecimal -= ticks;
                WorldTime.TickCount += ticks;
            }

            if (WorldTime.ElapsedMsDelta >= lagMs)
                _log.Warn($"LAGGED | MsPT: {mspt} Elapsed: {WorldTime.ElapsedMsDelta}");

            sw.Restart();

            TickWorlds(WorldTime);
        }
    }

    public static void Enqueue(Action act) {
        _pendingActions.Enqueue(act);
    }

    private static void Update() {
        // Global and cheap - stays sequential, drained once before any per-world work.
        while (_pendingActions.TryDequeue(out var act)) {
            try {
                act();
            }
            catch (Exception ex) {
                _log.Error($"Error running queued action: {ex}");
            }
        }

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