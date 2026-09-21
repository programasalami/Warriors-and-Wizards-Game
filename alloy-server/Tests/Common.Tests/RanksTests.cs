using Common.Database;
using Common.Database.Models;

namespace Common.Tests;

public class RanksTests {
    private static Account Acc(int id, int rank = 0, bool admin = false) => new() { Id = id, Name = "a" + id, Rank = rank, IsAdmin = admin };

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(79, false, 0)]
    [InlineData(80, false, 80)]
    [InlineData(99, false, 80)]
    [InlineData(100, false, 100)]
    [InlineData(500, false, 100)]
    [InlineData(0, true, 100)]      // the old admin flag means Owner
    public void OfRoundsDownToARealRank(int rank, bool admin, int expected) {
        Assert.Equal(expected, Ranks.Of(Acc(1, rank, admin)));
    }

    [Fact]
    public void ANewAccountIsAPlayer_AndNoAccountIsAlsoAPlayer() {
        Assert.Equal(Ranks.Player, Ranks.Of(new Account()));
        Assert.Equal(Ranks.Player, Ranks.Of(null));
    }

    [Fact]
    public void SetKeepsTheRankAndTheLegacyAdminFlagInStep() {
        var acc = Acc(1);
        Ranks.Set(acc, Ranks.Owner);
        Assert.True(acc.IsAdmin);
        Assert.Equal(Ranks.Owner, acc.Rank);

        Ranks.Set(acc, Ranks.Moderator);
        Assert.False(acc.IsAdmin);
        Assert.Equal(Ranks.Moderator, Ranks.Of(acc));

        Ranks.Set(acc, Ranks.Player);
        Assert.False(Ranks.IsModerator(acc));
    }

    [Theory]
    [InlineData("owner", 100, true)]
    [InlineData("Moderator", 80, true)]
    [InlineData(" MOD ", 80, true)]
    [InlineData("player", 0, true)]
    [InlineData("king", 0, false)]
    [InlineData("", 0, false)]
    public void ParsesRankNames(string text, int expected, bool ok) {
        Assert.Equal(ok, Ranks.TryParse(text, out var rank));
        if (ok)
            Assert.Equal(expected, rank);
    }

    [Fact]
    public void OnlyAStrictlyHigherModeratorCanModerate() {
        var owner = Acc(1, admin: true);
        var mod = Acc(2, Ranks.Moderator);
        var mod2 = Acc(3, Ranks.Moderator);
        var player = Acc(4);

        Assert.True(Ranks.CanModerate(owner, mod));
        Assert.True(Ranks.CanModerate(owner, player));
        Assert.True(Ranks.CanModerate(mod, player));

        Assert.False(Ranks.CanModerate(mod, mod2), "moderators cannot touch each other");
        Assert.False(Ranks.CanModerate(mod, owner), "nobody moderates an owner");
        Assert.False(Ranks.CanModerate(player, mod));
        Assert.False(Ranks.CanModerate(player, player), "a player can moderate nobody");
        Assert.False(Ranks.CanModerate(owner, owner), "not yourself");
        Assert.False(Ranks.CanModerate(null, player));
        Assert.False(Ranks.CanModerate(owner, null));
    }
}
