using System;

namespace WaWClient.Game;

// Turns frame time into a bounded number of fixed simulation steps.
//
// History (2026-09-21 audit): GameScreen used to compare an accumulator of MILLISECONDS against a step of 1/60 (a value in
// SECONDS), so every normal frame ran ~1000 fixed steps instead of 1, and a slow frame ran even more - the direct cause of the
// FPS collapse while shooting and of the stacked damage numbers. This class keeps everything in milliseconds and refuses to run
// more than MaxStepsPerFrame steps for one frame: the rest of the backlog is dropped (and counted) instead of being allowed to
// snowball. Pure and testable: no engine types.
public sealed class FixedStepper {
    public const double DefaultStepMs = 1000d / 60;
    public const int DefaultMaxStepsPerFrame = 5;

    public double StepMs { get; }
    public int MaxStepsPerFrame { get; }

    private double _accumulatedMs;

    // Steps returned by the last Advance call, and how many were dropped because the frame was too far behind.
    public int LastSteps { get; private set; }
    public int LastDropped { get; private set; }
    public long TotalDropped { get; private set; }

    public FixedStepper(double stepMs = DefaultStepMs, int maxStepsPerFrame = DefaultMaxStepsPerFrame) {
        if (stepMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepMs));
        if (maxStepsPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame));
        StepMs = stepMs;
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    // Adds a frame's elapsed time and returns how many fixed steps to run for it (0..MaxStepsPerFrame).
    public int Advance(double elapsedMs) {
        if (elapsedMs > 0)
            _accumulatedMs += elapsedMs;

        var due = (int)(_accumulatedMs / StepMs);
        var steps = Math.Min(due, MaxStepsPerFrame);
        var dropped = due - steps;

        // Consume what we run, throw away what we drop: the simulation stays at most one step behind real time.
        _accumulatedMs -= due * StepMs;
        if (_accumulatedMs < 0)
            _accumulatedMs = 0;

        LastSteps = steps;
        LastDropped = dropped;
        TotalDropped += dropped;
        return steps;
    }

    public void Reset() {
        _accumulatedMs = 0;
        LastSteps = 0;
        LastDropped = 0;
    }
}
