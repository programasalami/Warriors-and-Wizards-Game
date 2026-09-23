using WaWClient.Game;

namespace WaWClient.Tests.Game;

// The fixed-update loop used to compare a millisecond accumulator against a step of 1/60 (seconds) and ran ~1000 steps per normal
// frame with no cap: the direct cause of the FPS collapse while shooting (2026-09-21 audit). These pin the intended behaviour.
public class FixedStepperTests {

    [Fact]
    public void ANormalFrameRunsExactlyOneStep() {
        var stepper = new FixedStepper();
        Assert.Equal(1, stepper.Advance(1000d / 60));
        Assert.Equal(0, stepper.LastDropped);
    }

    [Fact]
    public void AFastFrameRunsNoStepAndCarriesTheRemainder() {
        var stepper = new FixedStepper();
        Assert.Equal(0, stepper.Advance(10));     // 10 ms of a 16.7 ms step
        Assert.Equal(1, stepper.Advance(10));     // 20 ms accumulated: one step, 3.3 ms carried
        Assert.Equal(0, stepper.Advance(3));      // 6.3 ms
        Assert.Equal(1, stepper.Advance(11));     // 17.3 ms: one step
    }

    [Fact]
    public void RemainderIsCarriedExactly() {
        var stepper = new FixedStepper(stepMs: 10);
        Assert.Equal(0, stepper.Advance(4));
        Assert.Equal(0, stepper.Advance(4));
        Assert.Equal(1, stepper.Advance(4));      // 12 ms: one step, 2 ms left
        Assert.Equal(1, stepper.Advance(8));      // 10 ms: one step, 0 left
        Assert.Equal(0, stepper.Advance(9));
    }

    [Fact]
    public void ASlowFrameIsCappedAndTheBacklogIsDroppedNotCarried() {
        var stepper = new FixedStepper(stepMs: 10, maxStepsPerFrame: 5);
        Assert.Equal(5, stepper.Advance(1000));   // 100 steps due, 5 run, 95 dropped
        Assert.Equal(95, stepper.LastDropped);
        Assert.Equal(95, stepper.TotalDropped);
        Assert.Equal(1, stepper.Advance(10));     // no backlog leaks into the next frame
        Assert.Equal(0, stepper.LastDropped);
    }

    [Fact]
    public void TheOldBugWouldHaveRunAThousandStepsForOneFrame() {
        // With the old constant (1/60 "seconds") a 16.7 ms frame is 1000 steps due; the cap keeps it at a handful.
        var stepper = new FixedStepper(stepMs: 1d / 60, maxStepsPerFrame: FixedStepper.DefaultMaxStepsPerFrame);
        var steps = stepper.Advance(1000d / 60);
        Assert.Equal(FixedStepper.DefaultMaxStepsPerFrame, steps);
        Assert.True(stepper.LastDropped > 900);
    }

    [Fact]
    public void NegativeOrZeroElapsedDoesNothing() {
        var stepper = new FixedStepper(stepMs: 10);
        Assert.Equal(0, stepper.Advance(0));
        Assert.Equal(0, stepper.Advance(-50));
        Assert.Equal(1, stepper.Advance(10));
    }

    [Fact]
    public void ResetForgetsTheRemainder() {
        var stepper = new FixedStepper(stepMs: 10);
        stepper.Advance(9);
        stepper.Reset();
        Assert.Equal(0, stepper.Advance(9));
    }

    [Fact]
    public void DefaultsAreSixtyHertzInMilliseconds() {
        Assert.Equal(1000d / 60, FixedStepper.DefaultStepMs, 9);
        Assert.Equal(FixedStepper.DefaultStepMs, GameScreen.FixedUpdateStep, 9);
        Assert.True(FixedStepper.DefaultMaxStepsPerFrame is >= 2 and <= 10);
    }
}
