using AlloyClient.Data;

namespace AlloyClient.Tests.Data;

// The in-client Portal reads the account server's public JSON (the same answers the website gets).
public class PortalDataTests {

    private const string ProfileJson = """
        {"name":"Riigged","rank":100,"rankName":"Owner","stars":3,"fame":120,"totalFame":500,"bestCharFame":300,"guild":"Knights","guildRank":30,
         "created":"2026-09-01","lastSeen":"2026-09-21","online":true,"world":"Nexus",
         "characters":[{"id":0,"class":782,"level":20,"fame":300,"exp":4000,"equipment":[2711,2606,-1,2594],"backpack":true,
                        "hp":670,"mp":385,"att":75,"def":25,"spd":50,"dex":75,"vit":40,"wis":60}],
         "classStats":[{"class":782,"bestLevel":20,"bestFame":300},{"class":797,"bestLevel":5,"bestFame":20}]}
        """;

    [Fact]
    public void ParsesAProfile() {
        Assert.True(PortalData.TryParseProfile(ProfileJson, out var p));
        Assert.Equal("Riigged", p.Name);
        Assert.Equal("Owner", p.RankName);
        Assert.Equal(3, p.Stars);
        Assert.Equal("Knights", p.Guild);
        Assert.True(p.Online);
        Assert.Equal("Nexus", p.World);
        var c = Assert.Single(p.Characters);
        Assert.Equal(782, c.Class);
        Assert.Equal(new[] { 2711, 2606, -1, 2594 }, c.Equipment);
        Assert.Equal(new[] { 670, 385, 75, 25, 50, 75, 40, 60 }, c.Stats);
        Assert.True(c.Backpack);
        Assert.Equal(2, p.ClassStats.Count);
    }

    [Fact]
    public void AShortEquipmentListIsPaddedToFourSlots() {
        Assert.True(PortalData.TryParseProfile("""{"name":"A","characters":[{"id":1,"class":782,"equipment":[5]}]}""", out var p));
        Assert.Equal(new[] { 5, -1, -1, -1 }, p.Characters[0].Equipment);
    }

    [Fact]
    public void ErrorsAndJunkAreNotProfiles() {
        Assert.False(PortalData.TryParseProfile("""{"error":"not found"}""", out _));
        Assert.False(PortalData.TryParseProfile("<Error>old server</Error>", out _));
        Assert.False(PortalData.TryParseProfile("", out _));
        Assert.Equal("not found", PortalData.ErrorOf("""{"error":"not found"}"""));
        Assert.Null(PortalData.ErrorOf("[1,2]"));
        Assert.Null(PortalData.ErrorOf("<Error/>"));
    }

    [Fact]
    public void ParsesLeaderboardRowsNamesGuildAndOnline() {
        Assert.True(PortalData.TryParseRows("""[{"rank":1,"name":"A","value":500,"stars":3},{"rank":2,"name":"G","value":9,"members":4}]""", out var rows));
        Assert.Equal(2, rows.Count);
        Assert.Equal(500, rows[0].Value);
        Assert.Equal(3, rows[0].Stars);
        Assert.Equal(4, rows[1].Members);

        Assert.True(PortalData.TryParseNames("""["Ann","Bob"]""", out var names));
        Assert.Equal(["Ann", "Bob"], names);
        Assert.False(PortalData.TryParseNames("""{"error":"x"}""", out _));

        Assert.True(PortalData.TryParseGuild("""{"name":"Knights","level":2,"fame":10,"totalFame":20,"created":"2026-09-02","members":[{"name":"A","guildRank":40,"stars":1,"fame":5}]}""", out var g));
        Assert.Equal("Knights", g.Name);
        Assert.Equal("Founder", PortalData.GuildRankName(g.Members[0].GuildRank));

        Assert.True(PortalData.TryParseOnline("""{"online":3,"version":"0.3.5","starGoals":[20,150,400,800,2000]}""", out var o));
        Assert.Equal(3, o.Online);
        Assert.Equal(5, o.StarGoals.Length);
        Assert.False(PortalData.TryParseOnline("""{"error":"too many requests"}""", out _));
    }

    [Fact]
    public void GuildRankNamesMatchTheServersEnum() {
        Assert.Equal("Initiate", PortalData.GuildRankName(0));
        Assert.Equal("Member", PortalData.GuildRankName(10));
        Assert.Equal("Officer", PortalData.GuildRankName(20));
        Assert.Equal("Leader", PortalData.GuildRankName(30));
        Assert.Equal("Founder", PortalData.GuildRankName(40));
    }
}
