using System;

namespace WaWClient.Loading;

public enum WorldMilestone {
    Connected,      // socket to the game server is up (skipped when switching worlds - the connection already exists)
    MapInfo,        // the server described the world (size, name) and we initialised the map
    CreateSuccess,  // the server accepted our character and told us its object id
    PlayerSpawned,  // the local player entity arrived in an Update packet and the HUD was built
    FirstFrame      // the first frame was drawn with the local player in the world
}

// Tracks REAL progress of getting into a world - both the first entry from character select and every later world
// switch (portal -> Reconnect -> new MapInfo -> ...). Network handlers call Mark() as each thing actually happens;
// the game screen's loading cover just reads Progress. Nothing here is timed.
public static class WorldLoad {
    private static readonly float[] Weights = [12f, 22f, 14f, 30f, 22f];
    private static readonly string[] Labels = ["Connecting", "Entering the realm", "Awakening your character", "Spawning", "Building the world"];

    private static readonly bool[] Reached = new bool[Weights.Length];
    private static readonly object Lock = new();

    // True from Begin() until the cover has finished with this load.
    public static volatile bool Active;

    // True when this load is a world switch rather than the first entry.
    public static volatile bool Switching;

    // Called when a load starts: the game screen was just created (first entry) or a Reconnect arrived (switch).
    // The caller shows the loading cover (WorldLoadCover.Begin).
    public static void Begin(bool switching) {
        lock (Lock) {
            Array.Clear(Reached);
            Switching = switching;
            Active = true;

            // A switch reuses the open connection.
            if (switching) {
                Reached[(int) WorldMilestone.Connected] = true;
            }
        }

    }

    public static void Mark(WorldMilestone milestone) {
        lock (Lock) {
            if (!Active) {
                return;
            }

            Reached[(int) milestone] = true;
        }
    }

    public static float Progress {
        get {
            lock (Lock) {
                var total = 0f;
                var done = 0f;
                for (var i = 0; i < Weights.Length; i++) {
                    total += Weights[i];
                    if (Reached[i]) {
                        done += Weights[i];
                    }
                }

                return total <= 0f ? 1f : done / total;
            }
        }
    }

    public static bool IsComplete {
        get {
            lock (Lock) {
                foreach (var r in Reached) {
                    if (!r) {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    // The most recently reached milestone's label, or the next thing being waited on - handy for logs.
    public static string CurrentLabel {
        get {
            lock (Lock) {
                for (var i = 0; i < Reached.Length; i++) {
                    if (!Reached[i]) {
                        return Labels[i];
                    }
                }

                return "Ready";
            }
        }
    }

    public static void Finish() => Active = false;
}
