using System;

namespace AlloyClient.Game;

// The arithmetic behind the FPS readout (2026-09-22, "fact-check the FPS readout"). Pure, no engine types, tested in FrameStatsTests.
//
// Every frame's duration goes into a ring buffer; once a second a Snapshot is taken over exactly the frames of that second:
//  - FPS = frames in the window / the window's REAL length (the old readout divided by 1.0 s although the window ran 1000 ms plus
//    however long the last frame overshot, so it always read a little high at low frame rates).
//  - avg / P90 / P99 / max frame time over the same frames (sorted once per second into a scratch array: no allocations per frame,
//    no per-frame sort).
public sealed class FrameStats {
    public const int DefaultCapacity = 8192;      // frames kept between two snapshots: enough for 8000 FPS
    public const double DefaultWindowMs = 1000;

    public readonly record struct Snapshot(double Fps, double AvgMs, double P90Ms, double P99Ms, double MaxMs, int Frames, double WindowMs);

    private readonly double[] _frames;
    private readonly double[] _scratch;
    private readonly double _windowMs;
    private int _count;
    private bool _overflowed;
    private double _elapsed;

    public FrameStats(int capacity = DefaultCapacity, double windowMs = DefaultWindowMs) {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (windowMs <= 0) throw new ArgumentOutOfRangeException(nameof(windowMs));
        _frames = new double[capacity];
        _scratch = new double[capacity];
        _windowMs = windowMs;
    }

    public int Frames => _count;
    public double ElapsedMs => _elapsed;

    // Adds one frame. Returns true when a window is full: take a Snapshot then.
    public bool Add(double frameMs) {
        if (frameMs < 0 || double.IsNaN(frameMs))
            frameMs = 0;
        if (_count < _frames.Length)
            _frames[_count] = frameMs;
        else
            _overflowed = true;       // more frames than the buffer holds: the count stays right, the percentiles use the first ones
        _count++;
        _elapsed += frameMs;
        return _elapsed >= _windowMs;
    }

    // The statistics of everything added since the last snapshot, then starts the next window.
    public Snapshot Take() {
        var frames = _count;
        var window = _elapsed;
        var kept = Math.Min(frames, _frames.Length);
        var snap = kept == 0
            ? new Snapshot(0, 0, 0, 0, 0, 0, window)
            : Compute(frames, window, kept);
        _count = 0;
        _elapsed = 0;
        _overflowed = false;
        return snap;
    }

    private Snapshot Compute(int frames, double window, int kept) {
        Array.Copy(_frames, _scratch, kept);
        var data = _scratch.AsSpan(0, kept);
        data.Sort();

        var sum = 0d;
        for (var i = 0; i < kept; i++)
            sum += data[i];

        var fps = window > 0 ? frames * 1000d / window : 0;
        return new Snapshot(fps, sum / kept, Percentile(data, 0.90), Percentile(data, 0.99), data[kept - 1], frames, window);
    }

    // Nearest-rank percentile: the value at least p of the frames are at or below.
    public static double Percentile(ReadOnlySpan<double> sorted, double p) {
        if (sorted.Length == 0) return 0;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    public bool Overflowed => _overflowed;
}
