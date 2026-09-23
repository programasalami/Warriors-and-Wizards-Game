using GameServer.Game;

namespace GameServer.Tests;

// Packet handlers that await (login, character load / create) resume on the game thread through this context, and the game
// thread itself never blocks on them (2026-09-21 audit, C5). These check the plumbing without a socket or a real server.
public class GameThreadSyncContextTests {

    [Fact]
    public void PostQueuesTheContinuationForTheGameThread() {
        var ctx = new GameThreadSynchronizationContext();
        var ran = false;
        ctx.Post(_ => ran = true, null);

        Assert.False(ran);                                  // nothing runs until the game loop pumps its queue
        Assert.True(GameLogic.DrainPendingActions() >= 1);
        Assert.True(ran);
    }

    [Fact]
    public void SendRunsInlineOnTheGameThread() {
        var ctx = new GameThreadSynchronizationContext();   // the creating thread counts as the game thread
        var ran = false;
        ctx.Send(_ => ran = true, null);
        Assert.True(ran);
        Assert.True(ctx.IsOnGameThread);
    }

    [Fact]
    public async Task AnAwaitedHandlerResumesThroughTheQueueAndCompletesOnlyThen() {
        var ctx = new GameThreadSynchronizationContext();
        var gate = new TaskCompletionSource();
        var afterAwait = false;

        async Task Handler() {
            await gate.Task;        // "the RPC to the AccountServer"
            afterAwait = true;      // must run back on the game thread, via the queue
        }

        // The context is installed only while the handler runs up to its first await (as on the real game thread); the test's own
        // awaits below must NOT be captured by it, or they would sit in the queue forever.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task task;
        try {
            task = Handler();
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        Assert.False(task.IsCompleted);

        gate.SetResult();
        await Task.Delay(50).ConfigureAwait(false);     // give the continuation time to be POSTED (not run)
        Assert.False(afterAwait);
        Assert.False(task.IsCompleted);

        GameLogic.DrainPendingActions();
        Assert.True(afterAwait);
        Assert.True(task.IsCompleted);
    }

    // The first live shutdown hung: the shutdown routine's own await was routed into the (already stopped) game-thread queue.
    [Fact]
    public async Task ShutdownCompletesEvenIfTheGameThreadContextIsStillInstalled() {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new GameThreadSynchronizationContext());
        try {
            var shutdown = GameLogic.ShutdownAsync();
            var finished = await Task.WhenAny(shutdown, Task.Delay(10_000).ContinueWith(_ => { }, TaskScheduler.Default)).ConfigureAwait(false);
            Assert.Same(shutdown, finished);
            Assert.True(GameLogic.ShutdownCompleted.IsSet);
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AutosaveIntervalIsAboutAMinute() {
        Assert.InRange(GameLogic.AutosaveIntervalMs, 15_000, 300_000);
    }
}
