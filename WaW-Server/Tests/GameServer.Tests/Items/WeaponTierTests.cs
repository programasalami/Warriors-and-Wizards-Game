using System.Globalization;
using System.Xml.Linq;

namespace GameServer.Tests.Items;

/// <summary>
/// The items that exist right now (2026-09-21): the Health Potion and the starter set - Old Sword / Old Staff, Old Helmet / Old Spell, Old Armor / Old Robe
/// and the Old Ring - drawn from the Dark Dungeon item sheet. The tiered Iron / Steel / Bronze / Gold weapons were removed until there is art for each
/// tier. Reads the shipped Equip.xml / Players.xml, so it fails if a starter goes missing or a class starts with the wrong gear.
/// </summary>
public class WeaponTierTests {
    private static XElement Load(string file) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Resources", "Xml", "Data", "Xmls", file)).Root!;

    private static XElement Item(string name) =>
        Load("Equip.xml").Elements("Object").Single(e => (string?)e.Attribute("id") == name);

    private static int Type(XElement e) => Convert.ToInt32((string)e.Attribute("type")!, 16);

    private static int Number(XElement e, string path) =>
        int.Parse((string)e.Descendants(path).First(), CultureInfo.InvariantCulture);

    private static readonly string[] Starters = ["Old Sword", "Old Staff", "Old Helmet", "Old Spell", "Old Armor", "Old Robe", "Old Ring"];

    [Fact]
    public void OnlyTheStarterSetAndThePotionExist() {
        var ids = Load("Equip.xml").Elements("Object").Select(e => (string)e.Attribute("id")!).ToList();
        Assert.Equal(Starters.Length + 1, ids.Count);
        Assert.Contains("Health Potion", ids);
        foreach (var name in Starters) {
            Assert.Contains(name, ids);
        }

        Assert.Equal(ids.Count, Load("Equip.xml").Elements("Object").Select(Type).Distinct().Count());       // no type id used twice
    }

    [Theory]
    [InlineData("Old Sword", 1, true)]
    [InlineData("Old Staff", 17, true)]
    [InlineData("Old Helmet", 16, false)]
    [InlineData("Old Spell", 11, false)]
    [InlineData("Old Armor", 7, false)]
    [InlineData("Old Robe", 14, false)]
    [InlineData("Old Ring", 9, false)]
    public void EveryStarterIsTierZeroOnTheItemSheetWithADescription(string name, int slotType, bool weapon) {
        var item = Item(name);
        Assert.Equal(0, Number(item, "Tier"));
        Assert.Equal(slotType, Number(item, "SlotType"));
        Assert.Equal("dungeonItems", (string)item.Element("Texture")!.Element("File")!);
        Assert.False(string.IsNullOrWhiteSpace((string?)item.Element("Description")));
        if (weapon) {
            Assert.True(Number(item, "MaxDamage") >= Number(item, "MinDamage") && Number(item, "MinDamage") > 0);
        }
    }

    [Fact]
    public void WarriorAndWizardStartWithTheirWholeSet() {
        var players = Load("Players.xml").Elements("Object").ToDictionary(e => (string)e.Attribute("id")!);

        int[] Gear(string cls) => ((string)players[cls].Element("Equipment")!).Split(',').Take(4).Select(x => Convert.ToInt32(x.Trim(), 16)).ToArray();

        // in the slot order of each class's SlotTypes: weapon, ability, armour, ring
        Assert.Equal([Type(Item("Old Sword")), Type(Item("Old Helmet")), Type(Item("Old Armor")), Type(Item("Old Ring"))], Gear("Warrior"));
        Assert.Equal([Type(Item("Old Staff")), Type(Item("Old Spell")), Type(Item("Old Robe")), Type(Item("Old Ring"))], Gear("Wizard"));
    }
}
