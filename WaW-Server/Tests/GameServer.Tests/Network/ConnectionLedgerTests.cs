using GameServer.Game.Network;

namespace GameServer.Tests.Network;

/// <summary>The game port's per-address connection cap and its counters (a stress test on 2026-09-22 left no trace because nothing was counted).</summary>
public class ConnectionLedgerTests {
    [Fact]
    public void CapsARemoteAddressAndCountsTheRefusals() {
        var ledger = new ConnectionLedger(3);
        Assert.True(ledger.TryAdd("1.2.3.4"));
        Assert.True(ledger.TryAdd("1.2.3.4"));
        Assert.True(ledger.TryAdd("1.2.3.4"));
        Assert.False(ledger.TryAdd("1.2.3.4"));           // the 4th is over the cap
        Assert.False(ledger.TryAdd("1.2.3.4"));
        Assert.Equal(3, ledger.OpenFrom("1.2.3.4"));

        var r = ledger.TakeReport();
        Assert.Equal(3, r.Open);
        Assert.Equal(3, r.Accepted);
        Assert.Equal(2, r.Refused);
        Assert.Equal(0, ledger.TakeReport().Accepted);      // the window counters reset, the open level does not
        Assert.Equal(3, ledger.TakeReport().Open);
    }

    [Fact]
    public void ADisconnectFreesOneSlotNotTheWholeAddress() {
        var ledger = new ConnectionLedger(2);
        ledger.TryAdd("1.2.3.4");
        ledger.TryAdd("1.2.3.4");
        ledger.Remove("1.2.3.4");                          // SocketServer used to delete the entry here: the cap could never trigger
        Assert.Equal(1, ledger.OpenFrom("1.2.3.4"));
        Assert.True(ledger.TryAdd("1.2.3.4"));
        Assert.False(ledger.TryAdd("1.2.3.4"));
        ledger.Remove("1.2.3.4");
        ledger.Remove("1.2.3.4");
        ledger.Remove("1.2.3.4");                          // one too many is harmless
        Assert.Equal(0, ledger.OpenFrom("1.2.3.4"));
        Assert.Equal(0, ledger.Open);
    }

    [Fact]
    public void TheLocalMachineIsNeverCapped() {
        // every browser player comes through the websocket bridge on the same box: they all share 127.0.0.1
        var ledger = new ConnectionLedger(2);
        for (var i = 0; i < 50; i++)
            Assert.True(ledger.TryAdd("127.0.0.1"));
        Assert.True(ledger.TryAdd("::1"));
        Assert.Equal(51, ledger.Open);
        Assert.Equal(0, ledger.TakeReport().Refused);
    }

    [Fact]
    public void AFullServerGivesTheSlotBackAndCountsIt() {
        var ledger = new ConnectionLedger(5);
        ledger.TryAdd("9.9.9.9");
        ledger.RefuseFull("9.9.9.9");
        Assert.Equal(0, ledger.OpenFrom("9.9.9.9"));
        Assert.Equal(1, ledger.TakeReport().RefusedFull);
    }

    [Fact]
    public void WarnsOnTheFirstRefusalThenEveryHundredth() {
        var ledger = new ConnectionLedger(1);
        Assert.True(ledger.ShouldWarn("5.5.5.5"));         // 1st
        for (var i = 2; i < ConnectionLedger.WarnEvery; i++)
            Assert.False(ledger.ShouldWarn("5.5.5.5"));
        Assert.True(ledger.ShouldWarn("5.5.5.5"));         // 100th
        Assert.False(ledger.ShouldWarn("5.5.5.5"));        // 101st
        Assert.True(ledger.ShouldWarn("6.6.6.6"));         // another address has its own count
        Assert.Equal(101, ledger.RefusalsFrom("5.5.5.5"));
    }

    [Fact]
    public void BadInputIsRefusedQuietly() {
        var ledger = new ConnectionLedger(0);              // a silly cap becomes 1
        Assert.Equal(1, ledger.MaxPerAddress);
        Assert.False(ledger.TryAdd(null));
        Assert.False(ledger.TryAdd(""));
        ledger.Remove(null);
        Assert.Equal(0, ledger.Open);
    }
}
