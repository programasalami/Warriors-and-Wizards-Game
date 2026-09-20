using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlloyClient.Logging;
using Microsoft.Extensions.Logging;

namespace AlloyClient.Loading;

// One unit of real loading work. Progress is the weighted fraction of steps that have ACTUALLY finished - nothing here
// is time-based. A step is either a background Task (network calls, data parsing) or an Action that must run on the
// main thread (GL textures, building screens); either kind can wait for other steps to finish first.
public sealed class LoadStep {
    public string Name { get; }
    public float Weight { get; }

    internal readonly Func<Task> StartTask;
    internal readonly Action MainThreadWork;
    internal readonly LoadStep[] After;

    internal bool Started;
    private volatile bool _done;

    public bool Done => _done;
    public bool Failed { get; internal set; }

    internal LoadStep(string name, float weight, Func<Task> startTask, Action mainThreadWork, LoadStep[] after) {
        Name = name;
        Weight = weight;
        StartTask = startTask;
        MainThreadWork = mainThreadWork;
        After = after;
    }

    internal void MarkDone() => _done = true;
}

public sealed class LoadPlan {
    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(LoadPlan));

    private readonly List<LoadStep> _steps = [];
    private float _totalWeight;

    public IReadOnlyList<LoadStep> Steps => _steps;

    // Background work (runs on the thread pool). Starts once every step in `after` is done.
    public LoadStep AddTask(string name, float weight, Func<Task> work, params LoadStep[] after) {
        var step = new LoadStep(name, weight, work, null, after);
        Add(step);
        return step;
    }

    // Work that has to run on the main thread (GL calls, building UI). At most one such step runs per frame, in the
    // order added, so the loader keeps drawing between them; each waits for the steps in `after`.
    public LoadStep AddMainThread(string name, float weight, Action work, params LoadStep[] after) {
        var step = new LoadStep(name, weight, null, work, after);
        Add(step);
        return step;
    }

    private void Add(LoadStep step) {
        _steps.Add(step);
        _totalWeight += step.Weight;
    }

    // Fraction (0..1) of the total weight that has really completed.
    public float Progress {
        get {
            if (_totalWeight <= 0f) {
                return 1f;
            }

            var done = 0f;
            foreach (var s in _steps) {
                if (s.Done) {
                    done += s.Weight;
                }
            }

            return done / _totalWeight;
        }
    }

    public bool IsComplete {
        get {
            foreach (var s in _steps) {
                if (!s.Done) {
                    return false;
                }
            }

            return true;
        }
    }

    // Called once per frame on the main thread by the loader: starts every background step whose prerequisites are
    // done, and runs the next ready main-thread step (one per frame).
    public void Tick() {
        var ranMainThread = false;

        foreach (var s in _steps) {
            if (s.Started || !PrerequisitesDone(s)) {
                continue;
            }

            if (s.StartTask != null) {
                Begin(s);
            } else if (!ranMainThread) {
                ranMainThread = true;
                s.Started = true;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try {
                    s.MainThreadWork();
                } catch (Exception e) {
                    s.Failed = true;
                    Logger.Log(LogLevel.Error, e, $"Load step '{s.Name}' failed");
                }
                s.MarkDone();
                Logger.Log(LogLevel.Information, $"[LOAD] '{s.Name}' (main thread) took {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
            }
        }
    }

    private static bool PrerequisitesDone(LoadStep s) {
        foreach (var a in s.After) {
            if (!a.Done) {
                return false;
            }
        }

        return true;
    }

    private static void Begin(LoadStep s) {
        s.Started = true;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Task task;
        try {
            task = s.StartTask();
        } catch (Exception e) {
            s.Failed = true;
            Logger.Log(LogLevel.Error, e, $"Load step '{s.Name}' failed to start");
            s.MarkDone();
            return;
        }

        // A failed step is still "done" - the loader must never hang on a broken request; failures are logged and the
        // screens that follow already handle missing data.
        task.ContinueWith(t => {
            if (t.IsFaulted) {
                s.Failed = true;
                Logger.Log(LogLevel.Error, t.Exception, $"Load step '{s.Name}' failed");
            }

            s.MarkDone();
            Logger.Log(LogLevel.Information, $"[LOAD] '{s.Name}' (background) took {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        });
    }
}
