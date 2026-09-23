using Common.Database;
using Common.Database.Models;

namespace Common.Tests;

public class VaultDbTests {
    [Fact]
    public void WhatIsSavedHasEightSlotsPerChestAndSaneItemTypes() {
        var cleaned = VaultDb.Clean([
            new VaultChest { ChestId = 99, ItemTypes = [0xa00, -5, 70000, 3], ItemDatas = [1, 2] },
            null
        ]);

        Assert.Equal(2, cleaned.Length);
        Assert.Equal([0, 1], cleaned.Select(c => c.ChestId));       // renumbered by position
        Assert.Equal([0xa00, -1, -1, 3, -1, -1, -1, -1], cleaned[0].ItemTypes);
        Assert.All(cleaned[1].ItemTypes, t => Assert.Equal(-1, t));
    }

    [Fact]
    public void NoAccountCanStoreMoreThanTheMaximumNumberOfChests() {
        var many = Enumerable.Range(0, 500).Select(i => new VaultChest { ChestId = i, ItemTypes = [] }).ToArray();
        Assert.Equal(VaultDb.MaxChests, VaultDb.Clean(many).Length);
        Assert.Empty(VaultDb.Clean(null));
    }
}
