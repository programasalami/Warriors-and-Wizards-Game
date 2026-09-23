using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Database.Models;
using Dapper;

namespace Common.Database;

// Saves the contents of an account's Vault Chests. Only the "VaultChests" part of the account's JSON is replaced, so a save that happens while other things about the
// account change (gold from a daily gift, a rank ...) can never overwrite them.
public static class VaultDb {
    public const int MaxChests = 64;
    public const int Slots = 8;

    // What is written: at most MaxChests chests, each with exactly Slots item types (-1 = empty) and no more than 65535 as a type.
    public static VaultChest[] Clean(VaultChest[] chests) {
        return (chests ?? []).Take(MaxChests).Select((c, i) => new VaultChest {
            ChestId = i,
            ItemTypes = Enumerable.Range(0, Slots).Select(s => c?.ItemTypes != null && s < c.ItemTypes.Length && c.ItemTypes[s] is >= 0 and <= 0xffff ? c.ItemTypes[s] : -1).ToArray(),
            ItemDatas = c?.ItemDatas ?? []
        }).ToArray();
    }

    public static async Task SaveAsync(int accountId, VaultChest[] chests) {
        var json = JsonSerializer.Serialize(Clean(chests));
        await using var conn = await DbClient.OpenConnectionAsync();
        await conn.ExecuteAsync("UPDATE accounts SET data = jsonb_set(data, '{VaultChests}', @Json::jsonb) WHERE id=@Id", new { Json = json, Id = accountId });
    }

    public static async Task<VaultChest[]> LoadAsync(int accountId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var json = await conn.ExecuteScalarAsync<string>("SELECT data->'VaultChests' FROM accounts WHERE id=@Id", new { Id = accountId });
        return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<VaultChest[]>(json) ?? [];
    }
}
