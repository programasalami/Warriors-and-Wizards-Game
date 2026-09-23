using Common.Database;
using Common.Database.Models;

namespace Common.Tests;

// The Portal's public profile (2026-09-21): what is shown, what is never shown, and the star count.
public class PublicProfileTests {

    [Fact]
    public void OldAccountsBorrowTheirFirstCharactersDateAndLastSeenIsTheLastSignIn() {
        var acc = new Account { Name = "Old", Characters = [
            new Character { CharId = 1, CreatedAt = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc) },
            new Character { CharId = 2, CreatedAt = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), IsDeleted = true } ] };
        PublicDb.FillDates(acc, new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), acc.CreatedAt);
        Assert.Equal(new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc), acc.LastSeenAt);

        var fresh = new Account { Name = "New", CreatedAt = new DateTime(2026, 9, 22, 1, 0, 0, DateTimeKind.Utc) };
        PublicDb.FillDates(fresh, null);
        Assert.Equal(new DateTime(2026, 9, 22, 1, 0, 0, DateTimeKind.Utc), fresh.CreatedAt);      // a real date is kept
        Assert.Equal(default, fresh.LastSeenAt);                                                   // never signed in: stays empty
    }

    [Theory]
    [InlineData("a", "a%")]              // 1-2 letters: starts with
    [InlineData("ab", "ab%")]
    [InlineData("abc", "%abc%")]         // 3+: anywhere
    [InlineData(" Ri% ", "Ri%")]         // wildcards typed by the player are removed
    [InlineData("r_ig", "%rig%")]
    public void SearchMatchesTheStartForShortTextAndAnywhereFromThreeLetters(string query, string pattern) =>
        Assert.Equal(pattern, PublicDb.SearchPatterns(query).Pattern);


    private static Account Sample() => new() {
        Id = 10, Name = "Riigged", Rank = 100, GuildName = "Knights", GuildRank = 3,
        CreatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), LastSeenAt = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        Stats = new AccountStats {
            CurrentFame = 120, TotalFame = 500, BestCharFame = 300, CurrentCredits = 9999, TotalCredits = 12345,
            ClassStats = [new ClassStats { ObjectType = 0x030e, BestLevel = 20, BestFame = 300 }, new ClassStats { ObjectType = 0x031d, BestLevel = 5, BestFame = 20 }]
        },
        Characters = [
            new Character { CharId = 0, ObjectType = 0x030e, Level = 20, CurrentFame = 300, XpPoints = 4000, HasBackpack = true, HealthPotions = 6,
                            ItemTypes = [0xa97, 0xa2e, -1, 0xa22, -1, -1, -1, -1, -1, -1, -1, -1],
                            Stats = new CharacterStats { MaxHp = 670, Hp = 12, MaxMp = 385, Attack = 75, Defense = 25, Speed = 50, Dexterity = 75, Vitality = 40, Wisdom = 60 } },
            new Character { CharId = 1, ObjectType = 0x031d, Level = 5, IsDeleted = true },
            new Character { CharId = 2, ObjectType = 0 }
        ]
    };

    [Fact]
    public void ShowsThePublicThingsAndOnlyLiveCharacters() {
        var p = PublicDb.ToProfile(Sample(), [20, 150, 400, 800], online: true, world: "Nexus");
        Assert.Equal("Riigged", p.Name);
        Assert.Equal("Owner", p.RankName);
        Assert.Equal("Knights", p.Guild);
        Assert.Equal(500, p.TotalFame);
        Assert.Equal("2026-09-01", p.Created);
        Assert.True(p.Online);
        Assert.Equal("Nexus", p.World);
        Assert.Single(p.Characters);                       // the deleted one and the empty slot are not shown
        var c = p.Characters[0];
        Assert.Equal(0x030e, c.Class);
        Assert.Equal([0xa97, 0xa2e, -1, 0xa22], c.Equipment);   // the four gear slots only, never the inventory
        Assert.Equal(670, c.Hp);                            // max HP, not the current (low) HP
        Assert.True(c.Backpack);
        Assert.Equal(2, p.ClassStats.Count);
    }

    [Fact]
    public void StarsCountEveryGoalReachedByEveryClass() {
        var goals = new[] { 20, 150, 400, 800 };
        Assert.Equal(2 + 1, PublicDb.Stars(Sample().Stats!.ClassStats, goals));   // Wizard 300 reaches 20 and 150; Warrior 20 reaches 20
    }

    [Fact]
    public void StarsAreZeroWithoutGoalsOrClasses() {
        Assert.Equal(0, PublicDb.Stars(null, [1, 2]));
        Assert.Equal(0, PublicDb.Stars([new ClassStats { BestFame = 999 }], null));
        Assert.Equal(0, PublicDb.Stars([new ClassStats { BestFame = 999 }], []));
    }

    [Fact]
    public void TheJsonNeverContainsPrivateFields() {
        var json = PublicDb.Json(PublicDb.ToProfile(Sample(), [20]));
        Assert.DoesNotContain("Credits", json);
        Assert.DoesNotContain("9999", json);
        Assert.DoesNotContain("HealthPotions", json);
        Assert.DoesNotContain("\"id\":10", json);          // the account id is not exposed (character ids are)
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"name\":\"Riigged\"", json);
    }
}
