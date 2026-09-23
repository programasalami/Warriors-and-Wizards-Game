namespace WaW.Engine;

// Where the last frame's time went, measured by the desktop GameWindow loop (2026-09-22, for the FPS readout). Plain fields, main
// thread only. The browser build has its own GameWindow (requestAnimationFrame drives it) and leaves these at their defaults.
//
//  Work  = Update + Draw on the CPU (the game's own cost, GL calls included but not their execution)
//  Swap  = SwapBuffers: with VSync on this is mostly waiting for the monitor; with it off it is the GPU backlog stalling the CPU
//  Sleep = the FPS-cap sleep (0 with VSync on or no cap)
//  Gpu   = the GPU's own time for the frame from a GL timer query (the last one that has finished; -1 when queries are unavailable)
public static class FrameTiming {
    public static double WorkMs;
    public static double SwapMs;
    public static double SleepMs;
    public static double GpuMs = -1;

    // The monitor's refresh rate in Hz as the platform reports it (0 = unknown).
    public static int RefreshRate;
}
