using Common.Database.Models;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities.Collections;
using Common;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Network;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

/// <summary>
/// What /give does on the server (OwnerCommands.GiveCommand): EntityInventory.TryAdd into the first free general slot, and the next inventory tick must
/// publish the slot as an Inventory stat so the next EntityStats tick queues it for the NewTick packet - otherwise the client says "Gave you ..." and
/// shows nothing (the 2026-09-22 report from the browser client).
/// </summary>
public class GiveItemTests {
    private const int HealthPotion = 0xa22;

    private static AccountStats Stats() => new() { ClassStats = [new ClassStats { ObjectType = 0x031d }] };

    private static (Vault World, EntityId PlayerId) Build(int accountId) {
        RealmWorldTests.EnsureGameDataLoaded();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;

        var vault = new Vault(0, 0, Common.Resources.World.WorldLibrary.WorldConfigs["Vault"]);
        var account = new Account { Id = accountId, Name = "givetest", VaultCount = 2, Stats = Stats() };
        vault.SetupChests(account);

        var user = new User();
        user.SetGameInfo(account, 1, vault);
        user.GameInfo.Load(new Character {
            CharId = 0, ObjectType = 0x031d, Level = 1,
            ItemTypes = Enumerable.Repeat(-1, 20).ToArray(), ItemDatas = []
        }, vault);
        return (vault, user.GameInfo.PlayerId);
    }

    private static Item Item(int type) => new(XmlLibrary.ItemDescs[(ushort)type].Root);

    [Fact]
    public void GivenItemLandsInTheFirstFreeGeneralSlot() {
        var (world, playerId) = Build(9201);
        ref var inv = ref world.EntityInventories.Get(playerId);

        var slot = inv.TryAdd(Item(HealthPotion));

        Assert.Equal(4, slot);                                    // 0-3 are the equipment slots
        Assert.Equal(HealthPotion, inv.ItemTypes()[4]);
    }

    [Fact]
    public void NextTicksPublishTheGivenItemAsAnInventoryStat() {
        var (world, playerId) = Build(9202);
        world.EntityInventories.Get(playerId).TryAdd(Item(HealthPotion));

        // The world's own order (World.Tick): inventories publish into the stats, sights read StatUpdates, then EntityStats collects the changed
        // stats into StatUpdates for the NEXT sight pass. Run the two pieces the way the tick does and look at what the sight pass would see.
        var time = new RealmTime();
        world.EntityInventories.Tick(ref time);
        ref var stats = ref world.EntityStats.Get(playerId);
        Assert.Equal(HealthPotion, stats.GetInt(StatType.Inventory4));

        world.EntityStats.Tick(ref time);
        var published = new List<StatType>();
        for (var i = 0; i < stats.StatUpdateCount; i++)
            published.Add(stats.StatUpdates[i].Type);
        Assert.Contains(StatType.Inventory4, published);
        Assert.Contains(StatType.InventoryData4, published);
        Assert.True(stats.PrivateMask.IsSet((int)StatType.Inventory4), "the owner's own inventory travels under the private mask");
    }
}
