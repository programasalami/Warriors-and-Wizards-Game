using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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

            TickWorlds(ref WorldTime);
        }
    }

    public static void Enqueue(Action act) {
        _pendingActions.Enqueue(act);
    }

    private static void Update() {
        while (_pendingActions.TryDequeue(out var act)) {
            try {
                act();
            }
            catch (Exception ex) {
                _log.Error($"Error running queued action: {ex}");
            }
        }

        foreach (var world in RealmManager.Worlds.Values) {
            try {
                world.Update();
            }
            catch (Exception ex) {
                _log.Error($"Error updating world {world.Id}: {ex}");
            }
        }

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
    
    private static void TickWorlds(ref RealmTime time) {
        foreach (var world in RealmManager.Worlds.Values)
            world.Tick(ref time);
    }
}