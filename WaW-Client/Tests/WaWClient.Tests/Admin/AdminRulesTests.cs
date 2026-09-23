using WaWClient.Game.Components.Admin;

namespace WaWClient.Tests.Admin;

public class AdminRulesTests {

    [Theory]
    [InlineData(0, false, false, "Player")]
    [InlineData(79, false, false, "Player")]
    [InlineData(80, true, false, "Moderator")]
    [InlineData(99, true, false, "Moderator")]
    [InlineData(100, true, true, "Owner")]
    public void RanksDecideWhatTheDashboardShows(int rank, bool staff, bool owner, string name) {
        Assert.Equal(staff, AdminRules.IsStaff(rank));
        Assert.Equal(owner, AdminRules.IsOwner(rank));
        Assert.Equal(name, AdminRules.RankName(rank));
    }

    [Theory]
    [InlineData("Bob", true)]
    [InlineData("abcdefghij", true)]
    [InlineData("abcdefghijk", false)]      // 11 letters: no such account
    [InlineData("bob1", false)]
    [InlineData("bob smith", false)]
    [InlineData("bob;/ban", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyRealLookingPlayerNamesAreAccepted(string name, bool ok) => Assert.Equal(ok, AdminRules.IsPlayerName(name));

    [Fact]
    public void BuildsTheModerationCommands() {
        Assert.Equal("/kick Bob", AdminRules.Kick("Bob"));
        Assert.Equal("/find Bob", AdminRules.Find("Bob"));
        Assert.Equal("/unmute Bob", AdminRules.Unmute("Bob"));
        Assert.Equal("/unban Bob", AdminRules.Unban("Bob"));
        Assert.Equal("/ban Bob 1d", AdminRules.Ban("Bob", "1d", ""));
        Assert.Equal("/ban Bob perm griefing the nexus", AdminRules.Ban("Bob", "perm", "  griefing the nexus "));
        Assert.Equal("/mute Bob 30m spam", AdminRules.Mute("Bob", "30m", "spam"));
    }

    [Fact]
    public void InvalidInputMakesNoCommand() {
        Assert.Null(AdminRules.Kick("bad name"));
        Assert.Null(AdminRules.Ban("Bob", "forever-ish", "x"));
        Assert.Null(AdminRules.Ban("", "1d", "x"));
        Assert.Null(AdminRules.Mute("Bob;/ban", "1d", "x"));
        Assert.Null(AdminRules.Give(""));
        Assert.Null(AdminRules.Spawn(0, "Pirate"));
        Assert.Null(AdminRules.Spawn(99, "Pirate"));
    }

    [Fact]
    public void AReasonCanNeverStartASecondCommand() {
        // a line break or control character is removed, so the reason stays part of the SAME chat line
        var command = AdminRules.Ban("Bob", "1d", "rude\n/give gold sword\r\n");
        Assert.DoesNotContain('\n', command);
        Assert.DoesNotContain('\r', command);
        Assert.StartsWith("/ban Bob 1d rude", command);
    }

    [Fact]
    public void ReasonsAreTrimmedAndCapped() {
        Assert.Equal(string.Empty, AdminRules.CleanReason(null));
        Assert.Equal(string.Empty, AdminRules.CleanReason("   "));
        Assert.Equal(120, AdminRules.CleanReason(new string('x', 500)).Length);
    }

    [Fact]
    public void TheDurationButtonCyclesThroughEveryChoiceAndWrapsAround() {
        var d = AdminRules.Durations[0];
        var seen = new List<string> { d };
        for (var i = 0; i < AdminRules.Durations.Length - 1; i++) {
            d = AdminRules.NextDuration(d);
            seen.Add(d);
        }

        Assert.Equal(AdminRules.Durations, seen);
        Assert.Equal(AdminRules.Durations[0], AdminRules.NextDuration(AdminRules.Durations[^1]));
        Assert.Equal(AdminRules.Durations[0], AdminRules.NextDuration("garbage"));
    }

    [Fact]
    public void EveryOfferedDurationIsOneTheServerAccepts() {
        // the server's rule lives in Common.Database.ModerationRules; the tokens here must stay inside its syntax
        foreach (var d in AdminRules.Durations) {
            Assert.True(d == "perm" || System.Text.RegularExpressions.Regex.IsMatch(d, "^[1-9][0-9]*[mhdw]$"), d);
        }
    }

    [Fact]
    public void GiveAndSpawnKeepTheNameOnOneLine() {
        Assert.Equal("/give Iron Sword", AdminRules.Give(" Iron Sword "));
        Assert.Equal("/spawn 3 Pirate", AdminRules.Spawn(3, "Pirate"));
        Assert.Null(AdminRules.Give("Iron\nSword"));
    }

    [Theory]
    [InlineData("largeObjects", true)]
    [InlineData("grasslands", true)]
    [InlineData("dungeonItems", true)]
    [InlineData("guildHall", false)]
    [InlineData("icons", false)]
    [InlineData("players", true)]
    [InlineData("skins", true)]
    [InlineData("npcs", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NewArtIsOurSheetsOnly(string sheet, bool isNew) => Assert.Equal(isNew, ArtRules.IsNewSheet(sheet));

    [Fact]
    public void ArtLabels() {
        Assert.Equal("NEW", ArtRules.Label("dungeonItems", 18, "Old Sword"));
        Assert.Equal("NEW", ArtRules.Label("dungeonItems", 15, "Old Robe"));                // shares the armour's picture on purpose (2026-09-22): not a stand-in
        Assert.Equal("PLACEHOLDER", ArtRules.Label("dungeonItems", 19, "DpsDummy0def"));   // the XML id, not the DisplayId, is what the list holds
        Assert.Equal("NEW", ArtRules.Label("dungeonItems", 19, "DPS Dummy 0 def"));        // a display name is unknown to the list (the dashboard passes the id)
        Assert.Equal("PLACEHOLDER", ArtRules.Label("dungeon", 54, "Table"));
        Assert.Equal("ORIGINAL", ArtRules.Label("icons", 3));                                // a retired sheet name would mean old art crept back
        Assert.Equal("no art", ArtRules.Label(null));
        Assert.True(ArtRules.IsPlaceholder("Wood Panel Wall"));
        Assert.False(ArtRules.IsPlaceholder("Forest Oak"));
    }
}
