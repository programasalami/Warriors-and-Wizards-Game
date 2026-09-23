using System.Diagnostics;

namespace AlloyClient.Game;

// Where the CPU time of a frame goes, section by section (2026-09-22, the "walking costs frames / a second player halves the FPS"
// investigation). Each section adds its elapsed ticks into the CURRENT frame; BeginFrame (from GameScreen.Update, the first thing of a
// frame) moves them to LastMs and clears. Plain arrays, main thread only, nothing allocates, ~20 ns per Begin/End pair.
public static class PerfSections {
    public enum Section {
        Net,            // Client.Tick: received packets applied
        Hud,            // user input, chat layer, notifications, HUD update, readout
        Fixed,          // fixed simulation steps
        MapUpdate,      // entity updates + visibility, particle gens, projectiles
        StageUpdate,    // the UI stage's own update (EnterFrame listeners of every UI sprite)
        DrawTiles,
        DrawShadows,
        DrawParticles,
        DrawModels,     // walls, props, card stars, stacked logs
        DrawEntities,   // the sprite pass (players, objects, projectiles, names, bars)
        Minimap,        // minimap texture patch
        Ui,             // the whole UI draw (SpriteRender batches)
        Count
    }

    private static readonly long[] Current = new long[(int)Section.Count];
    public static readonly double[] LastMs = new double[(int)Section.Count];
    private static readonly double TickToMs = 1000d / Stopwatch.Frequency;

    public static long Begin() => Stopwatch.GetTimestamp();

    public static void End(Section section, long start) => Current[(int)section] += Stopwatch.GetTimestamp() - start;

    public static void BeginFrame() {
        for (var i = 0; i < Current.Length; i++) {
            LastMs[i] = Current[i] * TickToMs;
            Current[i] = 0;
        }
    }
}
