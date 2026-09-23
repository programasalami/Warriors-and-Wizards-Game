namespace Alloy.Engine.Graphics;

// Per-frame GPU traffic counters for the FPS readout (2026-09-22): how many draw calls the frame issued and how many bytes it
// uploaded into GPU buffers. Incremented at the draw / upload sites (Render, TileChunkMesh, SpriteRender, InstanceAttributeBuffer),
// read and zeroed once per frame by the client's PerfCounters.BeginFrame. Plain fields, main thread only, nothing allocates.
public static class GpuStats {
    public static int DrawCalls;
    public static long UploadBytes;

    public static void Reset() {
        DrawCalls = 0;
        UploadBytes = 0;
    }
}
