using System.Threading;

namespace GameServer.Game;

// Installed on the game thread by GameLogic.Run. Every `await` inside a packet handler captures it, so the code AFTER the await
// runs back on the game thread (through GameLogic.Enqueue) - only the awaited work itself (an RPC to the AccountServer, a database
// read) happens elsewhere. 2026-09-21 audit: NetworkHandler used to block the game thread on those awaits with
// GetAwaiter().GetResult(), stalling every world for each login (and for the whole RPC timeout if the AccountServer was down).
public sealed class GameThreadSynchronizationContext : SynchronizationContext {
    private readonly int _gameThreadId = Environment.CurrentManagedThreadId;

    public bool IsOnGameThread => Environment.CurrentManagedThreadId == _gameThreadId;

    public override void Post(SendOrPostCallback d, object state) {
        GameLogic.Enqueue(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object state) {
        if (IsOnGameThread) {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        GameLogic.Enqueue(() => {
            try { d(state); }
            finally { done.Set(); }
        });
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => this;
}
