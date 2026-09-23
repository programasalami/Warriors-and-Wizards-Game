using Common.Structs;

namespace Common.Protocol.Tests;

// The packet id table is the wire protocol between every client and the game server. These pin the values that exist today so a
// renumbering (which would silently break every deployed client) fails a test instead.
public class PacketIdTests {

    [Fact]
    public void NoTwoPacketsShareAValue() {
        var values = Enum.GetValues<PacketId>().Select(p => (byte)p).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Theory]
    [InlineData(PacketId.Failure, 0)]
    [InlineData(PacketId.CreateSuccess, 1)]
    [InlineData(PacketId.PlayerShoot, 3)]
    [InlineData(PacketId.Move, 4)]
    [InlineData(PacketId.PlayerText, 5)]
    [InlineData(PacketId.Text, 6)]
    [InlineData(PacketId.Damage, 8)]
    [InlineData(PacketId.Update, 9)]
    [InlineData(PacketId.NewTick, 11)]
    [InlineData(PacketId.InvSwap, 12)]
    [InlineData(PacketId.UseItem, 13)]
    [InlineData(PacketId.Hello, 15)]
    [InlineData(PacketId.Goto, 16)]
    [InlineData(PacketId.Reconnect, 19)]
    [InlineData(PacketId.MapInfo, 20)]
    [InlineData(PacketId.Load, 21)]
    [InlineData(PacketId.UsePortal, 23)]
    [InlineData(PacketId.PlayerHit, 28)]
    [InlineData(PacketId.EnemyHit, 29)]
    [InlineData(PacketId.AccountList, 34)]
    [InlineData(PacketId.EnemyShoot, 40)]
    [InlineData(PacketId.Escape, 41)]
    [InlineData(PacketId.ServerProjectileProps, 48)]
    [InlineData(PacketId.TradeAccepted, 63)]
    [InlineData(PacketId.Unknown, 255)]
    public void TheWireValuesNeverChange(PacketId id, int expected) {
        Assert.Equal(expected, (byte)id);
    }

    [Fact]
    public void EveryRealPacketFitsBelowTheUnknownMarker() {
        foreach (var id in Enum.GetValues<PacketId>())
            if (id != PacketId.Unknown)
                Assert.True((byte)id < 200, $"{id} = {(byte)id}");
    }
}
