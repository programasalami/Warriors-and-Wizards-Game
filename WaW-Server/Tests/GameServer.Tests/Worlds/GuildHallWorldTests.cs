using Common.Database.Models;
using Common.Resources.World;
using GameServer.Game;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

// The Guild Hall (2026-09-21): one shared world per guild, reachable by FAST TRAVEL, built from the real GuildHall.json + Guild0.jm.
public class GuildHallWorldTests {
    private static Account InGuild(int accountId, int guildId, string guildName) => new() { Id = accountId, Name = "member" + accountId, GuildId = guildId, GuildName = guildName };

    [Fact]
    public void TheConfigAndItsMapsLoad() {
        RealmWorldTests.EnsureGameDataLoaded();
        var config = WorldLibrary.WorldConfigs["GuildHall"];
        Assert.Equal("Guild Hall", config.DisplayName);
        Assert.Equal(4, WorldLibrary.MapDatas["GuildHall"].Length);
        var hall = new GuildHall(0, GuildHall.DefaultMap, config);
        Assert.True(hall.Map.Data.Width > 0 && hall.Map.Data.Height > 0);
    }

    [Fact]
    public void MembersOfOneGuildShareOneHallAndOthersGetTheirOwn() {
        RealmWorldTests.EnsureGameDataLoaded();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;
        var a = GuildHall.ForAccount(InGuild(1, 77, "Knights"));
        var b = GuildHall.ForAccount(InGuild(2, 77, "Knights"));
        var c = GuildHall.ForAccount(InGuild(3, 78, "Mages"));
        Assert.NotNull(a);
        Assert.Same(a, b);
        Assert.NotSame(a, c);
        Assert.Equal("Knights's Guild Hall", a.DisplayName);
        Assert.True(a.Id > 0);                                   // registered with the realm manager, so a Hello can land in it
        Assert.Same(a, RealmManager.Worlds[a.Id]);
    }

    [Fact]
    public void NoGuildMeansNoHall() {
        Assert.Null(GuildHall.ForAccount(new Account { Id = 9, Name = "loner" }));
        Assert.Null(GuildHall.ForAccount(null));
    }
}
