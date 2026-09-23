using WaWClient.Ui.Components.Elements;

namespace WaWClient.Tests.Game;

// The FPS readout names what holds the frame rate down (2026-09-22).
public class DebugStatsLimiterTests {
    [Fact]
    public void VSyncWinsAndNamesTheMonitor() {
        Assert.Equal("VSync 60 Hz", DebugStats.Describe(true, 144, 60));
        Assert.Equal("VSync", DebugStats.Describe(true, -1, 0));
    }

    [Fact]
    public void ACapIsNamedWhenVSyncIsOff() => Assert.Equal("cap 144", DebugStats.Describe(false, 144, 60));

    [Fact]
    public void TheCompositorIsBlamedWhenTheSwapEatsTheFrame() {
        Assert.Equal("desktop compositor: the swap waits", DebugStats.Describe(false, -1, 60, swapMs: 13, avgFrameMs: 16.7));
        Assert.Equal("no limit", DebugStats.Describe(false, -1, 60, swapMs: 0.3, avgFrameMs: 3));
        Assert.Equal("no limit", DebugStats.Describe(false, -1, 60));
    }
}
