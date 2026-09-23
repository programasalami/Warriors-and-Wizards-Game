using AlloyClient.Game;

namespace AlloyClient.Tests.Game;

// The FPS readout's arithmetic (2026-09-22): FPS over the real window length, percentiles over exactly that window's frames.
public class FrameStatsTests {

    [Fact]
    public void FpsIsFramesOverTheRealWindowLength() {
        var stats = new FrameStats();
        // 60 frames of 16.7 ms = 1002 ms: the window closes on the 60th frame
        var closed = false;
        for (var i = 0; i < 59; i++)
            Assert.False(stats.Add(16.7));
        closed = stats.Add(16.7);
        Assert.True(closed);

        var snap = stats.Take();
        Assert.Equal(60, snap.Frames);
        Assert.InRange(snap.Fps, 59.8, 59.95);               // 60 frames in 1.002 s
        Assert.InRange(snap.AvgMs, 16.69, 16.71);
        Assert.InRange(snap.MaxMs, 16.69, 16.71);
    }

    [Fact]
    public void ASlowLastFrameDoesNotInflateTheFps() {
        var stats = new FrameStats();
        for (var i = 0; i < 30; i++)
            stats.Add(20);                                   // 600 ms
        Assert.True(stats.Add(500));                         // one hitch: the window is 1100 ms long with 31 frames
        var snap = stats.Take();
        Assert.Equal(31, snap.Frames);
        Assert.InRange(snap.Fps, 28.1, 28.3);                // 31 / 1.1 s, NOT 31 "per second"
        Assert.Equal(500, snap.MaxMs);
        Assert.Equal(500, snap.P99Ms);
        Assert.Equal(20, snap.P90Ms);
    }

    [Fact]
    public void PercentilesAreNearestRank() {
        var sorted = new double[100];
        for (var i = 0; i < 100; i++)
            sorted[i] = i + 1;                               // 1..100
        Assert.Equal(90, FrameStats.Percentile(sorted, 0.90));
        Assert.Equal(99, FrameStats.Percentile(sorted, 0.99));
        Assert.Equal(100, FrameStats.Percentile(sorted, 1.0));
        Assert.Equal(1, FrameStats.Percentile(sorted, 0.0));
        Assert.Equal(0, FrameStats.Percentile([], 0.5));
    }

    [Fact]
    public void TakeStartsAFreshWindow() {
        var stats = new FrameStats();
        for (var i = 0; i < 100; i++)
            stats.Add(10);
        stats.Take();
        Assert.Equal(0, stats.Frames);
        Assert.Equal(0, stats.ElapsedMs);
        Assert.Equal(0, stats.Take().Frames);                // an empty window is harmless
    }

    [Fact]
    public void MoreFramesThanTheBufferStillCountsThemAll() {
        var stats = new FrameStats(capacity: 16);
        for (var i = 0; i < 1000; i++)
            stats.Add(1);
        var snap = stats.Take();
        Assert.Equal(1000, snap.Frames);
        Assert.InRange(snap.Fps, 999, 1001);
        Assert.True(stats.Frames == 0);
    }

    [Fact]
    public void NegativeOrNanFramesCountAsZero() {
        var stats = new FrameStats();
        stats.Add(-5);
        stats.Add(double.NaN);
        stats.Add(1000);
        var snap = stats.Take();
        Assert.Equal(3, snap.Frames);
        Assert.Equal(1000, snap.MaxMs);
    }
}
