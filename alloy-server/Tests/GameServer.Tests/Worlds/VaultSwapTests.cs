using Common.Database.Models;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Network;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

/// <summary>
/// The real item swaps between a player and a chest, run through the server's own swap code in a real Vault world with a real player entity (no network):
/// putting an item in, taking it out, swapping two, another player being refused, standing too far away, and that a loot bag still disappears when emptied
/// while a Vault Chest stays. (Before the fix in EntityInventoryManager a swap with a container lost the container's item or copied the player's.)
/// </summary>
public class VaultSwapTests {
    private const int IronSword = 0xa01;
    private const int SteelSword = 0xa02;
    private const int GoldStaff = 0xa9b;
    private const int LootBag0 = 0x0500;

    // A real account always has a stats block with a class entry for each class it has played.
    private static AccountStats Stats() => new() { ClassStats = [new ClassStats { ObjectType = 0x031d }] };

    private sealed class Scene {
        public Vault Vault;
        public Account Account;
        public User User;
        public EntityId PlayerId;
        public EntityId ChestId;
        public int ChestX, ChestY;
    }

    private static Scene Build(int accountId) {
        RealmWorldTests.EnsureGameDataLoaded();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;

        var vault = new Vault(0, 0, Common.Resources.World.WorldLibrary.WorldConfigs["Vault"]);
        var account = new Account { Id = accountId, Name = "swaptest", VaultCount = 2, Stats = Stats() };
        vault.SetupChests(account);

        var user = new User();
        user.SetGameInfo(account, 1, vault);
        user.GameInfo.Load(new Character {
            CharId = 0, ObjectType = 0x031d, Level = 1,
            ItemTypes = Enumerable.Repeat(-1, 20).ToArray(), ItemDatas = []
        }, vault);

        var scene = new Scene { Vault = vault, Account = account, User = user, PlayerId = user.GameInfo.PlayerId, ChestId = vault.OpenChestId(0) };
        ref var chestStats = ref vault.EntityStats.Get(scene.ChestId);
        scene.ChestX = (int)chestStats.Pos.X;
        scene.ChestY = (int)chestStats.Pos.Y;
        MoveNextToChest(scene);
        return scene;
    }

    private static void MoveNextToChest(Scene s) {
        ref var player = ref s.Vault.Entities.Get(s.PlayerId);
        player.Move(s.Vault, s.ChestX + 1.5f, s.ChestY + 0.5f);
    }

    private static Item Item(int type) => new(XmlLibrary.ItemDescs[(ushort)type].Root);

    private static void Swap(Scene s, User user, EntityId a, int slotA, EntityId b, int slotB) {
        s.Vault.EntityInventories.EnqueueSwap(user, new SlotObjectData { ObjectId = a, SlotId = (byte)slotA }, new SlotObjectData { ObjectId = b, SlotId = (byte)slotB });
        var time = new RealmTime();
        s.Vault.EntityInventories.Tick(ref time);
        s.Vault.Update();          // entities queued to leave the world are removed here
    }

    private static int PlayerItem(Scene s, int slot) => s.Vault.EntityInventories.Get(s.PlayerId).ItemTypes()[slot];

    private static int ChestItem(Scene s, int slot) => s.Vault.EntityInventories.Get(s.ChestId).ItemTypes()[slot];

    [Fact]
    public void PuttingAnItemInAChestMovesIt_NotCopiesIt() {
        var s = Build(9101);
        s.Vault.EntityInventories.Get(s.PlayerId).SetItem(4, Item(IronSword));

        Swap(s, s.User, s.PlayerId, 4, s.ChestId, 0);

        Assert.Equal(IronSword, ChestItem(s, 0));
        Assert.Equal(-1, PlayerItem(s, 4));
    }

    [Fact]
    public void TakingAnItemOutMovesItBack_AndTheEmptyChestStaysOnTheMap() {
        var s = Build(9102);
        s.Vault.EntityInventories.Get(s.ChestId).SetItem(0, Item(IronSword));

        Swap(s, s.User, s.ChestId, 0, s.PlayerId, 5);

        Assert.Equal(IronSword, PlayerItem(s, 5));
        Assert.Equal(-1, ChestItem(s, 0));
        Assert.NotEqual(EntityId.Null, s.Vault.Entities.Get(s.ChestId).Id);      // an emptied Vault Chest is not removed like a loot bag
        Assert.NotEqual(EntityId.Null, s.Vault.EntityInventories.Get(s.ChestId).Id);
    }

    [Fact]
    public void SwappingTwoItemsExchangesThem() {
        var s = Build(9103);
        s.Vault.EntityInventories.Get(s.PlayerId).SetItem(4, Item(IronSword));
        s.Vault.EntityInventories.Get(s.ChestId).SetItem(1, Item(GoldStaff));

        Swap(s, s.User, s.PlayerId, 4, s.ChestId, 1);

        Assert.Equal(GoldStaff, PlayerItem(s, 4));
        Assert.Equal(IronSword, ChestItem(s, 1));
    }

    [Fact]
    public void MovingItemsAroundInsideAChestWorks() {
        var s = Build(9104);
        s.Vault.EntityInventories.Get(s.ChestId).SetItem(0, Item(IronSword));

        Swap(s, s.User, s.ChestId, 0, s.ChestId, 6);

        Assert.Equal(-1, ChestItem(s, 0));
        Assert.Equal(IronSword, ChestItem(s, 6));
    }

    [Fact]
    public void AnotherPlayerCannotTouchSomeoneElsesChest() {
        var s = Build(9105);
        s.Vault.EntityInventories.Get(s.ChestId).SetItem(0, Item(IronSword));

        var stranger = new User();
        stranger.SetGameInfo(new Account { Id = 424242, Name = "stranger", Stats = Stats() }, 1, s.Vault);
        stranger.GameInfo.Load(new Character { CharId = 0, ObjectType = 0x031d, Level = 1, ItemTypes = Enumerable.Repeat(-1, 20).ToArray(), ItemDatas = [] }, s.Vault);
        ref var them = ref s.Vault.Entities.Get(stranger.GameInfo.PlayerId);
        them.Move(s.Vault, s.ChestX + 0.5f, s.ChestY + 1.5f);

        Swap(s, stranger, s.ChestId, 0, stranger.GameInfo.PlayerId, 4);

        Assert.Equal(IronSword, ChestItem(s, 0));
        Assert.Equal(-1, s.Vault.EntityInventories.Get(stranger.GameInfo.PlayerId).ItemTypes()[4]);
    }

    [Fact]
    public void YouMustBeStandingNearTheChest() {
        var s = Build(9106);
        s.Vault.EntityInventories.Get(s.PlayerId).SetItem(4, Item(IronSword));
        ref var player = ref s.Vault.Entities.Get(s.PlayerId);
        player.Move(s.Vault, s.ChestX + 12f, s.ChestY + 0.5f);

        Swap(s, s.User, s.PlayerId, 4, s.ChestId, 0);

        Assert.Equal(IronSword, PlayerItem(s, 4));
        Assert.Equal(-1, ChestItem(s, 0));
    }

    [Fact]
    public void WhatYouPutInIsSavedToTheAccount() {
        var s = Build(9107);
        s.Vault.EntityInventories.Get(s.PlayerId).SetItem(4, Item(SteelSword));

        Swap(s, s.User, s.PlayerId, 4, s.ChestId, 2);
        s.Vault.SaveIfChanged();

        Assert.Equal(SteelSword, s.Account.VaultChests[0].ItemTypes[2]);
    }

    [Fact]
    public void ALootBagStillDisappearsWhenYouTakeTheLastItem() {
        var s = Build(9108);
        var bag = new Entity((ushort)LootBag0);
        ref var bagEntity = ref s.Vault.EnterWorld(ref bag);
        bagEntity.Move(s.Vault, s.ChestX + 1.5f, s.ChestY + 1.5f);
        var bagId = bagEntity.Id;
        s.Vault.EntityInventories.Get(bagId).SetItem(0, Item(IronSword));

        Swap(s, s.User, bagId, 0, s.PlayerId, 4);

        Assert.Equal(IronSword, PlayerItem(s, 4));
        Assert.Equal(EntityId.Null, s.Vault.Entities.Get(bagId).Id);       // the empty bag is gone
    }
}
