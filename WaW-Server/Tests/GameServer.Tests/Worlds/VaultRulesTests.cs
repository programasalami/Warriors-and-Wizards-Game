using Common.Database.Models;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

public class VaultRulesTests {
    private static readonly List<VaultRules.Spot> Spots = [new(0, 0), new(10, 10), new(9, 10), new(11, 10), new(10, 5), new(20, 20)];

    [Fact]
    public void TheNearestSpotsToTheMiddleGetTheOpenChests() {
        var (open, closed) = VaultRules.Split(Spots, 10, 10, 3);

        Assert.Equal(3, open.Count);
        Assert.Equal(3, closed.Count);
        Assert.Contains(new VaultRules.Spot(10, 10), open);
        Assert.Contains(new VaultRules.Spot(9, 10), open);
        Assert.Contains(new VaultRules.Spot(11, 10), open);
        Assert.Contains(new VaultRules.Spot(0, 0), closed);
        Assert.Contains(new VaultRules.Spot(20, 20), closed);
    }

    [Fact]
    public void TheSameMapAlwaysGivesTheSameChestsInTheSamePlaces() {
        var a = VaultRules.Split(Spots, 10, 10, 2);
        var b = VaultRules.Split(Spots.AsEnumerable().Reverse().ToList(), 10, 10, 2);
        Assert.Equal(a.Open, b.Open);
        Assert.Equal(a.Closed, b.Closed);
    }

    [Theory]
    [InlineData(0, 0, 6)]
    [InlineData(-5, 0, 6)]
    [InlineData(6, 6, 0)]
    [InlineData(99, 6, 0)]
    public void TheNumberOfOpenChestsIsKeptInRange(int owned, int expectedOpen, int expectedClosed) {
        var (open, closed) = VaultRules.Split(Spots, 10, 10, owned);
        Assert.Equal(expectedOpen, open.Count);
        Assert.Equal(expectedClosed, closed.Count);
    }

    [Fact]
    public void ACleanChestHasEightSlotsAndDropsWhatTheGameNoLongerHas() {
        var cleaned = VaultRules.Clean([0xa00, 0xdead, -1, 0xa69], t => t is 0xa00 or 0xa69);
        Assert.Equal(8, cleaned.Length);
        Assert.Equal([0xa00, -1, -1, 0xa69, -1, -1, -1, -1], cleaned);
        Assert.Equal(Enumerable.Repeat(-1, 8), VaultRules.Clean(null, _ => true));
        Assert.Equal(8, VaultRules.Clean(new int[50], _ => true).Length);
    }

    [Fact]
    public void TheChestListGrowsToTheAccountsCountAndKeepsWhatWasSaved() {
        var saved = new List<VaultChest> { new() { ChestId = 0, ItemTypes = [0xa00, -1, -1, -1, -1, -1, -1, -1], ItemDatas = [] } };
        var list = VaultRules.Chests(saved, 3, _ => true);

        Assert.Equal(3, list.Count);
        Assert.Equal(0xa00, list[0].ItemTypes[0]);
        Assert.All(list[1].ItemTypes, t => Assert.Equal(-1, t));
        Assert.Equal([0, 1, 2], list.Select(c => c.ChestId));
    }

    [Fact]
    public void NoAccountEverGetsMoreThanTheMaximumChests() {
        Assert.Equal(VaultRules.MaxChests, VaultRules.Chests(null, 10_000, _ => true).Count);
        Assert.Empty(VaultRules.Chests(null, -3, _ => true));
    }

    [Fact]
    public void ContentsAreComparedSlotBySlot() {
        int[][] a = [[1, -1], [2, 3]];
        Assert.True(VaultRules.SameContents(a, [[1, -1], [2, 3]]));
        Assert.False(VaultRules.SameContents(a, [[1, -1], [2, 4]]));
        Assert.False(VaultRules.SameContents(a, [[1, -1]]));
        Assert.False(VaultRules.SameContents(a, null));
    }
}
