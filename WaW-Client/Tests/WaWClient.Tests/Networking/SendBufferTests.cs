using WaWClient.Networking;
using WaWClient.Networking.Packets;
using Common.Structs;

namespace WaWClient.Tests.Networking;

// The client's outgoing buffer was a fixed 64 KB with no bounds check: a burst of packets in one frame (the old runaway hit loop
// queued ~1000 EnemyHit packets per hit) threw out of QueuePacket on the main thread and froze the client (2026-09-21 audit).
// Now it grows up to a hard limit and drops (and counts) instead of throwing.
public class SendBufferTests {

    // A packet whose body is `size` bytes of filler.
    private sealed class Filler(int size) : IOutgoingPacket {
        public PacketId PacketId => PacketId.Move;
        public void Write(ref SpanWriter writer) {
            for (var i = 0; i < size; i++)
                writer.Write((byte)0xAB);
        }
    }

    [Fact]
    public void GrowsPastTheInitialSizeInsteadOfThrowing() {
        using var state = new SocketSendState();
        var perPacket = 1000 + 5;
        var packets = SocketSendState.InitialBufferBytes / perPacket + 10;   // more than the initial buffer holds

        for (var i = 0; i < packets; i++)
            Assert.True(state.WritePacket(new Filler(1000), 1));

        Assert.Equal(packets * perPacket, state.PendingBytes);
        Assert.True(state.Capacity > SocketSendState.InitialBufferBytes);
        Assert.Equal(0, state.DroppedPackets);
    }

    [Fact]
    public void DropsAndCountsAtTheHardLimitWithoutThrowing() {
        using var state = new SocketSendState();
        var written = 0;
        var dropped = 0;

        // Keep writing 60 KB packets until they are refused; must never throw and never exceed the limit.
        for (var i = 0; i < 40; i++) {
            if (state.WritePacket(new Filler(60_000), 1)) written++;
            else dropped++;
        }

        Assert.True(written > 0);
        Assert.True(dropped > 0);
        Assert.Equal(dropped, state.DroppedPackets);
        Assert.True(state.PendingBytes <= SocketSendState.MaxBufferBytes);
        Assert.True(state.Capacity <= SocketSendState.MaxBufferBytes);
    }

    [Fact]
    public void ADroppedPacketLeavesEarlierBytesIntact() {
        using var state = new SocketSendState();
        Assert.True(state.WritePacket(new Filler(10), 7));
        var before = state.PendingBytes;

        // One packet larger than the whole limit can never be written.
        Assert.False(state.WritePacket(new Filler(SocketSendState.MaxBufferBytes + 1), 7));
        Assert.Equal(before, state.PendingBytes);
        Assert.Equal(1, state.DroppedPackets);
    }

    [Fact]
    public void ResetEmptiesThePendingBytes() {
        using var state = new SocketSendState();
        state.WritePacket(new Filler(100), 1);
        state.Reset();
        Assert.Equal(0, state.PendingBytes);
    }
}
