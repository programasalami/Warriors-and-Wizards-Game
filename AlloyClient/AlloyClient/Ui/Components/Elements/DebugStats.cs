using System;
using Alloy.Engine;
using AlloyClient.Core;
using AlloyClient.Game;
using AlloyClient.Logging;
using AlloyClient.Rendering;
using Alloy.UiLib;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Microsoft.Extensions.Logging;

namespace AlloyClient.Ui.Components.Elements;

// The DEV / F5 readout. Rewritten 2026-09-22 ("fact-check the FPS readout"):
//  - FPS and the frame-time percentiles are over the last ONE second (FrameStats: frames / the window's real length, no per-frame sort).
//  - The limiter is named next to the FPS (VSync at the monitor's rate / an FPS cap / none), because on this PC VSync pins the number at 60
//    whatever the game costs.
//  - Where the frame's time goes: CPU work (update / fixed / draw), the GPU's own time (GL timer query), the swap wait, the cap sleep.
//  - "Unlimited" = what the frame rate would be without the limiter: 1000 / the larger of CPU work and GPU time. That is the number to
//    push up on a VSynced machine; friends with VSync off see it directly as their FPS.
//  - Draw calls and uploaded bytes per frame (GpuStats), the memory / world / loop rows as before.
//  - Every 10 s the same numbers go to the console log ([FPS] ...) so a run can be read back afterwards.
public class DebugStats : Sprite {

    private const int Outline = 3;
    private const int Indent = 8;
    private const int LogEverySeconds = 10;

    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(DebugStats));

    private readonly FrameStats _frames = new();
    private int _secondsSinceLog;

    private readonly SimpleText _fps = Row("FPS: 0", 2);
    private readonly SimpleText _frameTimes = Row("Frame ms (1 s): avg 0  P90 0  P99 0  max 0", Indent);
    private readonly SimpleText _cpu = Row("CPU work: 0 ms (update 0 / fixed 0 / draw 0)", Indent);
    private readonly SimpleText _gpu = Row("GPU: - ms  swap wait: 0 ms  sleep: 0 ms", Indent);
    private readonly SimpleText _unlimited = Row("Unlimited: -", Indent);
    private readonly SimpleText _traffic = Row("Draw calls: 0, uploads: 0 KB/frame", Indent);
    private readonly SimpleText _memory = Row("Memory", 2);
    private readonly SimpleText _gcAlloc = Row("Total: 0 MB", Indent);
    private readonly SimpleText _gcAllocDelta = Row("Allocated: 0 KB/s", Indent);
    private readonly SimpleText _gcCounts = Row("Collections gen0/1/2: 0/0/0", Indent);
    private readonly SimpleText _world = Row("World", 2);
    private readonly SimpleText _tiles = Row("Tiles: 0", Indent);
    private readonly SimpleText _shadows = Row("Shadows: 0", Indent);
    private readonly SimpleText _entities = Row("Entities: 0", Indent);
    private readonly SimpleText _particles = Row("Particles: 0", Indent);
    private readonly SimpleText _ui = Row("Ui: 0", Indent);
    private readonly SimpleText _loop = Row("Loop", 2);
    private readonly SimpleText _fixedSteps = Row("Fixed steps: 0 (dropped 0)", Indent);
    private readonly SimpleText _projectiles = Row("Projectiles: 0", Indent);
    private readonly SimpleText _particleGens = Row("Particle gens: 0 (dropped 0)", Indent);
    private readonly SimpleText _statusTexts = Row("Status texts: 0 (dropped 0)", Indent);
    private readonly SimpleText _packets = Row("Packets/frame: 0, send buf 0 B", Indent);
    private readonly SimpleText _scans = Row("Entity scans/frame: 0", Indent);

    private long _lastGcBytes;

    private static SimpleText Row(string text, int x) =>
        new(new TextConfig { Text = text, X = x, FontSize = 16, FontType = FontType.Bold, OutlineThickness = Outline, Anchor = UiAnchor.LeftTop });

    public DebugStats() {
        SimpleText[] rows = [
            _fps, _frameTimes, _cpu, _gpu, _unlimited, _traffic,
            _memory, _gcAlloc, _gcAllocDelta, _gcCounts,
            _world, _tiles, _shadows, _entities, _particles, _ui,
            _loop, _fixedSteps, _projectiles, _particleGens, _statusTexts, _packets, _scans
        ];
        var y = 4;
        foreach (var row in rows) {
            AddChild(row);
            row.Y = y;
            y += (int)row.Height + 4;
        }
    }

    public void Update(GameTime gameTime) {
        if (!_frames.Add(gameTime.ElapsedMs))
            return;

        var snap = _frames.Take();
        var limiter = Limiter(snap.AvgMs);
        var cpu = FrameTiming.WorkMs;
        var gpu = FrameTiming.GpuMs;
        var busiest = Math.Max(cpu, gpu);
        var unlimited = busiest > 0 ? 1000d / busiest : 0;

        _fps.SetText($"FPS: {snap.Fps:F1}  ({limiter})");
        _frameTimes.SetText($"Frame ms (1 s): avg {snap.AvgMs:F2}  P90 {snap.P90Ms:F2}  P99 {snap.P99Ms:F2}  max {snap.MaxMs:F2}");
        _cpu.SetText($"CPU work: {cpu:F2} ms (update {PerfCounters.UpdateMs:F2} / fixed {PerfCounters.FixedUpdateMs:F2} / draw {PerfCounters.DrawMs:F2})");
        _gpu.SetText(gpu >= 0
            ? $"GPU: {gpu:F2} ms  swap wait: {FrameTiming.SwapMs:F2} ms  sleep: {FrameTiming.SleepMs:F2} ms"
            : $"GPU: n/a  swap wait: {FrameTiming.SwapMs:F2} ms  sleep: {FrameTiming.SleepMs:F2} ms");
        _unlimited.SetText(unlimited > 0
            ? $"Unlimited: ~{unlimited:F0} FPS (1000 / {(cpu >= gpu ? "CPU" : "GPU")} {busiest:F2} ms)"
            : "Unlimited: -");
        _traffic.SetText($"Draw calls: {PerfCounters.DrawCallsLastFrame}, uploads: {PerfCounters.UploadBytesLastFrame / 1024} KB/frame");

        _gcAlloc.SetText($"Total: {GC.GetTotalMemory(false) / 1_000_000d:F2} MB");
        var gcBytes = GC.GetTotalAllocatedBytes();
        _gcAllocDelta.SetText($"Allocated: {(gcBytes - _lastGcBytes) / 1000d:F1} KB/s");
        _lastGcBytes = gcBytes;
        _gcCounts.SetText($"Collections gen0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");

        _tiles.SetText($"Tiles: {Render.LastDrawCountTiles}");
        _shadows.SetText($"Shadows: {Render.LastDrawCountShadows}");
        _entities.SetText($"Entities: {Render.LastDrawCountEntities}");
        _particles.SetText($"Particles: {Render.LastDrawParticleCount}");
        _ui.SetText($"Ui: {UiRender.LastRenderCount} (batches {Alloy.UiLib.Rendering.SpriteRender.LastBatchCount})");
        _fixedSteps.SetText($"Fixed steps: {PerfCounters.FixedStepsThisFrame} (dropped {PerfCounters.FixedStepsDroppedTotal})");
        _projectiles.SetText($"Projectiles: {PerfCounters.Projectiles}");
        _particleGens.SetText($"Particle gens: {PerfCounters.ParticleGenerators} (dropped {PerfCounters.ParticleEffectsDroppedTotal})");
        _statusTexts.SetText($"Status texts: {PerfCounters.StatusTextsLive} (dropped {PerfCounters.StatusTextsDroppedTotal})");
        _packets.SetText($"Packets/frame: {PerfCounters.PacketsQueuedThisFrame}, send buf {PerfCounters.SendBufferBytes} B, dropped {PerfCounters.PacketsDroppedTotal}");
        _scans.SetText($"Entity scans/frame: {PerfCounters.EntityScansThisFrame}");

        if (++_secondsSinceLog >= LogEverySeconds) {
            _secondsSinceLog = 0;
            Logger.Info($"[FPS] {snap.Fps:F1} ({limiter}) frame avg {snap.AvgMs:F2} p99 {snap.P99Ms:F2} max {snap.MaxMs:F2} ms | cpu {cpu:F2} gpu {gpu:F2} swap {FrameTiming.SwapMs:F2} sleep {FrameTiming.SleepMs:F2} ms | unlimited ~{unlimited:F0} | draws {PerfCounters.DrawCallsLastFrame} uploads {PerfCounters.UploadBytesLastFrame / 1024} KB | ents {Render.LastDrawCountEntities} tiles {Render.LastDrawCountTiles} ui {UiRender.LastRenderCount}");
        }
    }

    // What holds the frame rate down, in words: the monitor (VSync), a cap from the options, the desktop compositor, or nothing.
    public static string Limiter(double avgFrameMs) => Describe(Settings.VSync, Settings.FpsCap, FrameTiming.RefreshRate, FrameTiming.SwapMs, avgFrameMs);

    // With VSync off and no cap, a WINDOWED game on Windows can still be paced by the desktop compositor: SwapBuffers blocks until the
    // desktop's next refresh (no tearing either, the compositor shows whole frames). That shows as most of the frame spent in the swap.
    public static string Describe(bool vsync, int fpsCap, int refreshRate, double swapMs = 0, double avgFrameMs = 0) {
        if (vsync)
            return refreshRate > 0 ? $"VSync {refreshRate} Hz" : "VSync";
        if (fpsCap > 0)
            return $"cap {fpsCap}";
        if (avgFrameMs > 0 && swapMs > avgFrameMs * 0.5)
            return "desktop compositor: the swap waits";
        return "no limit";
    }
}
