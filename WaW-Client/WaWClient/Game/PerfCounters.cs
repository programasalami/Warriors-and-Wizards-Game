namespace WaWClient.Game;

// Cheap per-frame counters for the DEV / F5 readout (DebugStats). Everything here is a plain field written from the main thread;
// nothing allocates. The "ThisFrame" values are zeroed by GameScreen at the start of each Update; the readout samples them once a
// second. Added by the 2026-09-21 audit so the gameplay loop can be measured by the user without a profiler.
public static class PerfCounters {
    // Fixed simulation steps run for this frame and how many were dropped (see FixedStepper).
    public static int FixedStepsThisFrame;
    public static int FixedStepsDroppedThisFrame;
    public static long FixedStepsDroppedTotal;

    // World contents.
    public static int Projectiles;
    public static int ParticleGenerators;
    public static long ParticleEffectsDroppedTotal;   // refused because the particle budget was full
    public static int StatusTextsLive;
    public static long StatusTextsDroppedTotal;       // oldest texts removed to stay under the cap

    // Networking (client side).
    public static int PacketsQueuedThisFrame;
    public static int SendBufferBytes;                // bytes waiting to be sent when the frame's flush ran
    public static long PacketsDroppedTotal;           // refused because the send buffer hit its hard limit

    // Hit tests: entities examined by GetClosestEnemy/GetClosestPlayer this frame.
    public static int EntityScansThisFrame;

    // Frame time split (milliseconds), measured in GameScreen.
    public static double UpdateMs;
    public static double FixedUpdateMs;
    public static double DrawMs;

    // GPU traffic of the LAST frame (WaW.Engine.Graphics.GpuStats counts the current one; copied here at the start of the next).
    public static int DrawCallsLastFrame;
    public static long UploadBytesLastFrame;

    public static void BeginFrame() {
        FixedStepsThisFrame = 0;
        FixedStepsDroppedThisFrame = 0;
        PacketsQueuedThisFrame = 0;
        EntityScansThisFrame = 0;

        DrawCallsLastFrame = WaW.Engine.Graphics.GpuStats.DrawCalls;
        UploadBytesLastFrame = WaW.Engine.Graphics.GpuStats.UploadBytes;
        WaW.Engine.Graphics.GpuStats.Reset();
    }
}
