using System.Globalization;
using System.Xml.Linq;

namespace GameServer.Tests.Items;

/// <summary>
/// The first five weapon tiers for the Warrior (swords) and the Wizard (staffs): T0 is the "Used" starter, T1-T4 are Iron, Steel, Bronze, Gold.
/// Reads the shipped Equip.xml / Players.xml, so it fails if a tier goes missing, uses stock art, or stops getting stronger.
/// </summary>
public class WeaponTierTests {
    private static XElement Load(string file) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Resources", "Xml", "Data", "Xmls", file)).Root!;

    private static XElement Item(string name) =>
        Load("Equip.xml").Elements("Object").Single(e => (string?)e.Attribute("id") == name);

    private static int Type(XElement e) => Convert.ToInt32((string)e.Attribute("type")!, 16);

    private static int Number(XElement e, string path) =>
        int.Parse((string)e.Descendants(path).First(), CultureInfo.InvariantCulture);

    public static TheoryData<string[], int> Lines() => new() {
        { ["Used Sword", "Iron Sword", "Steel Sword", "Bronze Sword", "Gold Sword"], 1 },
        { ["Used Staff", "Iron Staff", "Steel Staff", "Bronze Staff", "Gold Staff"], 17 },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void EachLineHasFiveTiersWithNewArtAndGrowingDamage(string[] names, int slotType) {
        var lastMin = 0;
        var lastMax = 0;
        for (var tier = 0; tier < names.Length; tier++) {
            var item = Item(names[tier]);
            Assert.Equal(tier, Number(item, "Tier"));
            Assert.Equal(slotType, Number(item, "SlotType"));
            Assert.Equal("equipAndConsume", (string)item.Element("Texture")!.Element("File")!);      // the new item sheet
            Assert.False(string.IsNullOrWhiteSpace((string?)item.Element("Description")));

            var min = Number(item, "MinDamage");
            var max = Number(item, "MaxDamage");
            Assert.True(min > lastMin && max > lastMax, $"{names[tier]} must hit harder than the tier below it");
            (lastMin, lastMax) = (min, max);
        }
    }

    [Fact]
    public void EveryWeaponHasItsOwnPictureAndItsOwnType() {
        var names = new[] { "Used Sword", "Iron Sword", "Steel Sword", "Bronze Sword", "Gold Sword", "Used Staff", "Iron Staff", "Steel Staff", "Bronze Staff", "Gold Staff" };
        var items = names.Select(Item).ToList();
        Assert.Equal(names.Length, items.Select(Type).Distinct().Count());
        Assert.Equal(names.Length, items.Select(i => (string)i.Element("Texture")!.Element("Index")!).Distinct().Count());
        Assert.Equal(names.Length, Load("Equip.xml").Elements("Object").Select(Type).Distinct().Count() - 3);   // + potion, spell, helm: no id used twice
    }

    [Fact]
    public void WarriorAndWizardStartWithTheUsedWeapons() {
        var players = Load("Players.xml").Elements("Object").ToDictionary(e => (string)e.Attribute("id")!);

        int Starter(string cls) => Convert.ToInt32(((string)players[cls].Element("Equipment")!).Split(',')[0].Trim(), 16);

        Assert.Equal(Type(Item("Used Sword")), Starter("Warrior"));
        Assert.Equal(Type(Item("Used Staff")), Starter("Wizard"));
    }
}
