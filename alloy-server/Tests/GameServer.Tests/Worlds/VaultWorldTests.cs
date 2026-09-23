using Common.Database.Models;
using Common.Resources.World;
using Common.Resources.Xml;
using GameServer.Game;
using GameServer.Game.Worlds.Logic;

namespace GameServer.Tests.Worlds;

/// <summary>
/// The Vault, run against the REAL data (map, chest definitions, item list): an account's chests appear on the map with their items, only the owner may use
/// them, the rest of the chest spots stay closed, and the map is all new forest art with a reachable spawn.
/// </summary>
public class VaultWorldTests {
    private static Account AccountWith(int id, int vaultCount, params int[][] chestItems) => new() {
        Id = id,
        Name = "vaulttest",
        VaultCount = vaultCount,
        VaultChests = chestItems.Select((items, i) => new VaultChest { ChestId = i, ItemTypes = items, ItemDatas = [] }).ToList()
    };

    private static Vault NewVault() {
        RealmWorldTests.EnsureGameDataLoaded();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;        // the tick rate is only set when the server loop starts; the chests' save timer needs it
        return new Vault(0, 0, WorldLibrary.WorldConfigs["Vault"]);
    }

    [Fact]
    public void TheVaultMapIsAllNewForestArt() {
        RealmWorldTests.EnsureGameDataLoaded();
        var map = WorldLibrary.MapDatas["Vault"].Single();

        var stock = new HashSet<string>();
        foreach (var tile in map.Tiles.Cast<MapTileData>().Where(t => t != null && t.GroundType != 0xff)) {
            var name = XmlLibrary.TileDescs.TryGetValue(tile.GroundType, out var desc) ? desc.GroundId : null;
            if (name != null && !name.StartsWith("Woodland"))
                stock.Add(name);
        }

        Assert.Empty(stock);
        Assert.All(map.Entities.Select(e => XmlLibrary.ObjectDescs[e.ObjType].ObjectId).Distinct(), id => Assert.StartsWith("Forest", id));
    }

    [Fact]
    public void TheVaultMapHasASpawnAndRoomForAtLeastSixteenChests() {
        RealmWorldTests.EnsureGameDataLoaded();
        var map = WorldLibrary.MapDatas["Vault"].Single();

        Assert.NotEmpty(map.Regions[TileRegion.Spawn]);
        Assert.True(map.Regions[TileRegion.Vault].Count >= 16, "chest spots: " + map.Regions[TileRegion.Vault].Count);
    }

    [Fact]
    public void BothChestKindsExistInTheGameData() {
        RealmWorldTests.EnsureGameDataLoaded();
        Assert.True(XmlLibrary.ContainerDescs.TryGetValue(Vault.VaultChestType, out var open));
        Assert.True(open.Persistent, "an emptied Vault Chest must stay");
        Assert.Equal(8, open.SlotTypes.Length);
        Assert.True(XmlLibrary.ObjectDescs.ContainsKey(Vault.ClosedVaultChestType));
        Assert.False(XmlLibrary.ContainerDescs.ContainsKey(Vault.ClosedVaultChestType), "a closed chest has no inventory");
    }

    [Fact]
    public void AnAccountsChestsAppearWithTheirItems() {
        var vault = NewVault();
        var ironSword = 0xa00;
        var goldStaff = 0xa97;
        var account = AccountWith(1234, 2, [ironSword, -1, -1, -1, -1, -1, -1, -1], [-1, goldStaff, -1, -1, -1, -1, -1, 0xa00]);

        vault.SetupChests(account);

        Assert.Equal(2, vault.OpenChestCount);
        var contents = vault.ChestContents();
        Assert.Equal(ironSword, contents[0][0]);
        Assert.Equal(goldStaff, contents[1][1]);
        Assert.Equal(0xa00, contents[1][7]);
        Assert.Equal(8, contents[0].Length);
    }

    [Fact]
    public void EveryChestSitsInTheMiddleOfItsTile() {
        // Placed at the tile CORNER the picture was drawn half a tile up and to the left of the square the chest blocks (an invisible wall at its bottom right).
        var vault = NewVault();
        vault.SetupChests(AccountWith(9, 4));
        var spots = vault.Map.Regions[TileRegion.Vault];

        var seen = 0;
        foreach (ref var e in vault.Entities) {
            if (e.Desc.ObjectType != Vault.VaultChestType && e.Desc.ObjectType != Vault.ClosedVaultChestType)
                continue;

            ref var stats = ref vault.EntityStats.Get(e.Id);
            float x = stats.Pos.X, y = stats.Pos.Y;
            Assert.Equal(0.5f, x - (float) Math.Floor(x), 3);
            Assert.Equal(0.5f, y - (float) Math.Floor(y), 3);
            Assert.Contains(spots, p => p.X == (int) x && p.Y == (int) y);
            seen++;
        }

        Assert.Equal(spots.Count, seen);
    }

    [Fact]
    public void OnlyTheOwnerCanUseTheirChests() {
        var vault = NewVault();
        vault.SetupChests(AccountWith(77, 2));

        for (var i = 0; i < vault.OpenChestCount; i++) {
            ref var inv = ref vault.EntityInventories.Get(vault.OpenChestId(i));
            Assert.True(inv.OwnedBy(77));
            Assert.False(inv.OwnedBy(78), "another player could open this chest");
        }
    }

    [Fact]
    public void TheChestsNearestTheMiddleAreTheOpenOnes_AndTheRestStayClosed() {
        var vault = NewVault();
        vault.SetupChests(AccountWith(5, 3));

        var spots = vault.Map.Regions[TileRegion.Vault].Count;
        var closed = 0;
        foreach (ref var e in vault.Entities) {
            if (e.Desc.ObjectType == Vault.ClosedVaultChestType)
                closed++;
        }

        Assert.Equal(3, vault.OpenChestCount);
        Assert.Equal(spots - 3, closed);
    }

    [Fact]
    public void ChestsThatWereNeverSavedStartEmpty_AndUnknownItemsAreDroppedNotFatal() {
        var vault = NewVault();
        vault.SetupChests(AccountWith(6, 2, [0xa00, 0xdead, 5, 5, 5, 5, 5, 5]));       // 0xdead and 5 are not items

        var contents = vault.ChestContents();
        Assert.Equal(0xa00, contents[0][0]);
        Assert.All(contents[0].Skip(1), t => Assert.Equal(-1, t));
        Assert.All(contents[1], t => Assert.Equal(-1, t));
    }

    [Fact]
    public void ChangesAreDetectedAndWrittenBackToTheAccount() {
        var vault = NewVault();
        var account = AccountWith(9, 2);
        vault.SetupChests(account);

        ref var inv = ref vault.EntityInventories.Get(vault.OpenChestId(0));
        inv.SetItem(3, new Common.Resources.Xml.Descriptors.Item(XmlLibrary.ItemDescs[0xa69].Root));
        vault.SaveIfChanged();

        Assert.Equal(0xa69, account.VaultChests[0].ItemTypes[3]);
        Assert.Equal(2, account.VaultChests.Count);
    }
}
