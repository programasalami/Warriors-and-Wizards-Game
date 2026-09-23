using System.Xml.Linq;
using Common;
using Common.Database.Models;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Network;
using GameServer.Game.Worlds.Logic;
using GameServer.Tests.Worlds;

namespace GameServer.Tests.Items;

/// <summary>
/// The Skill slot (2026-09-23): player slot 20 (InventoryLayout.SkillSlot) takes only SlotType 30 items, is never filled by loot or /give,
/// travels to the client as its own stats (Skill0 / SkillData0, not InventoryData0 + 20 = Glow), and a character saved with the old 20 slots
/// gets its item array grown instead of an out-of-range crash on the first save.
/// </summary>
public class SkillSlotTests {
    private const int HealthPotion = 0xa22;

    private static (Vault World, EntityId PlayerId) Build(int accountId, int savedSlots = 20) {
        RealmWorldTests.EnsureGameDataLoaded();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;

        var vault = new Vault(0, 0, Common.Resources.World.WorldLibrary.WorldConfigs["Vault"]);
        var account = new Account { Id = accountId, Name = "skilltest", VaultCount = 2, Stats = new AccountStats { ClassStats = [new ClassStats { ObjectType = 0x031d }] } };
        vault.SetupChests(account);

        var user = new User();
        user.SetGameInfo(account, 1, vault);
        user.GameInfo.Load(new Character {
            CharId = 0, ObjectType = 0x031d, Level = 1,
            ItemTypes = Enumerable.Repeat(-1, savedSlots).ToArray(), ItemDatas = []
        }, vault);
        return (vault, user.GameInfo.PlayerId);
    }

    private static Item Potion() => new(XmlLibrary.ItemDescs[(ushort)HealthPotion].Root);

    // No Skill item exists in the game data yet, so the tests make one in memory.
    private static Item Skill() => new(XElement.Parse($"<Object type=\"0xfe01\" id=\"Test Skill\"><Class>Equipment</Class><SlotType>{InventoryLayout.SkillSlotType}</SlotType></Object>"));

    [Fact]
    public void ThePlayerHasTheSkillSlotAndOnlySkillsFit() {
        var (world, playerId) = Build(9301);
        ref var inv = ref world.EntityInventories.Get(playerId);

        Assert.Equal(InventoryLayout.PlayerSlots, inv.ItemTypes().Length);
        Assert.False(inv.IsEquippable(Potion(), InventoryLayout.SkillSlot));
        Assert.True(inv.IsEquippable(Skill(), InventoryLayout.SkillSlot));

        inv.SetItem(InventoryLayout.SkillSlot, Potion());         // refused: wrong kind
        Assert.Equal(-1, inv.ItemTypes()[InventoryLayout.SkillSlot]);
        inv.SetItem(InventoryLayout.SkillSlot, Skill());
        Assert.Equal(0xfe01, inv.ItemTypes()[InventoryLayout.SkillSlot]);
    }

    [Fact]
    public void GivenItemsNeverLandInTheSkillSlot() {
        var (world, playerId) = Build(9302);
        ref var inv = ref world.EntityInventories.Get(playerId);

        while (inv.TryAdd(Potion()) >= 0) { }

        Assert.Equal(-1, inv.ItemTypes()[InventoryLayout.SkillSlot]);
    }

    [Fact]
    public void TheSkillSlotIsPublishedAsItsOwnStats() {
        var (world, playerId) = Build(9303);
        world.EntityInventories.Get(playerId).SetItem(InventoryLayout.SkillSlot, Skill());

        var time = new RealmTime();
        world.EntityInventories.Tick(ref time);
        ref var stats = ref world.EntityStats.Get(playerId);

        Assert.Equal(0xfe01, stats.GetInt(StatType.Skill0));
        Assert.NotEqual(0xfe01, stats.GetInt(StatType.Glow));     // InventoryData0 + 20 would have landed on Glow
        Assert.True(StatData.IsStringStat(StatType.SkillData0));
    }

    [Fact]
    public void SavingACharacterFromBeforeTheSkillSlotGrowsItsItemArray() {
        var (world, playerId) = Build(9304, savedSlots: 20);
        ref var inv = ref world.EntityInventories.Get(playerId);
        inv.SetItem(InventoryLayout.SkillSlot, Skill());

        var chr = new Character { CharId = 0, ObjectType = 0x031d, ItemTypes = Enumerable.Repeat(-1, 20).ToArray(), ItemDatas = [] };
        inv.Save(chr);

        Assert.Equal(InventoryLayout.PlayerSlots, chr.ItemTypes.Length);
        Assert.Equal(0xfe01, chr.ItemTypes[InventoryLayout.SkillSlot]);
    }
}
