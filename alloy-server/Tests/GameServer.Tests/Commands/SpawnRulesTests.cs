using System.Xml.Linq;
using Common.Resources.Xml.Descriptors;
using GameServer.Game.Systems.Chat.Commands;

namespace GameServer.Tests.Commands;

/// <summary>"/spawn" name lookup: forgiving on case and a typed plural, unique partial matches, suggestions when several match, never a player.</summary>
public class SpawnRulesTests {
    private static ObjectDesc Desc(string id, ushort type, bool player = false, string displayId = null) {
        var e = new XElement("Object", new XElement("Class", player ? "Player" : "Character"));
        if (player) e.Add(new XElement("Player"));
        if (displayId != null) e.Add(new XElement("DisplayId", displayId));
        return new ObjectDesc(e, id, type);
    }

    private static readonly ObjectDesc[] Objects = [
        Desc("Pirate", 0x600), Desc("Pirate Captain", 0x601), Desc("Wizard", 0x300, player: true),
        Desc("DpsDummy0def", 0x902c, displayId: "DPS Dummy 0 DEF"), Desc("Weapon Rack", 0x9f01),
    ];

    [Theory]
    [InlineData("Pirate", "Pirate")]
    [InlineData("pirate", "Pirate")]
    [InlineData("PIRATES", "Pirate")]                 // "/spawn 10 pirates"
    [InlineData("pirate captain", "Pirate Captain")]
    [InlineData("DPS Dummy 0 DEF", "DpsDummy0def")]  // the display name works too
    [InlineData("rack", "Weapon Rack")]              // a unique partial match
    [InlineData("captain", "Pirate Captain")]
    public void FindsTheObject(string query, string expectedId) {
        var found = SpawnRules.Resolve(query, Objects, out var error);
        Assert.NotNull(found);
        Assert.Equal(expectedId, found.ObjectId);
        Assert.Null(error);
    }

    [Fact]
    public void SeveralMatchesAskToBeMoreSpecific() {
        var found = SpawnRules.Resolve("pir", Objects, out var error);
        Assert.Null(found);
        Assert.Contains("2 objects match", error);
        Assert.Contains("Pirate Captain", error);
    }

    [Fact]
    public void NothingMatchingSaysSo() {
        Assert.Null(SpawnRules.Resolve("dragon", Objects, out var error));
        Assert.Equal("No object named 'dragon'.", error);
        Assert.Null(SpawnRules.Resolve("   ", Objects, out error));
        Assert.Contains("/spawn", error);
    }

    [Fact]
    public void PlayersAreNeverSpawnable() {
        Assert.Null(SpawnRules.Resolve("Wizard", Objects, out var error));
        Assert.Equal("No object named 'Wizard'.", error);
        Assert.Null(SpawnRules.Resolve("wiz", Objects, out _));
    }
}
