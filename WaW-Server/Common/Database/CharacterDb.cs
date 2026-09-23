using System;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Database.Models;
using Common.Structs;
using Dapper;

namespace Common.Database;

// Saves ONE character of an account. Only that character's entry in the account's JSON is replaced (jsonb_set on the array
// element), so a save made while other things about the account change - gold from a daily gift, an inbox claim, a rank - can never
// overwrite them. Characters live in the account document as an array indexed by CharId (see DbClient.GetCharacterAsync).
// 2026-09-21 audit: before this the game server never saved characters at all (equipment, potions, HP were lost on disconnect).
public static class CharacterDb {
    private static readonly Utilities.Logger _log = new(typeof(CharacterDb));

    // A copy of what will be written, with the values a live game can leave in a state we do not want to persist tidied up.
    // Returns null when the character cannot be stored (no id, no class).
    public static Character Clean(Character chr) {
        if (chr == null || chr.CharId < 0 || chr.ObjectType == 0)
            return null;

        var stats = chr.Stats == null ? null : new CharacterStats {
            Hp = chr.Stats.Hp, Mp = chr.Stats.Mp, MaxHp = chr.Stats.MaxHp, MaxMp = chr.Stats.MaxMp,
            Attack = chr.Stats.Attack, Defense = chr.Stats.Defense, Speed = chr.Stats.Speed,
            Dexterity = chr.Stats.Dexterity, Vitality = chr.Stats.Vitality, Wisdom = chr.Stats.Wisdom
        };

        if (stats != null) {
            // Death is not a real feature yet (nothing records it, the graveyard is a placeholder): a character is never stored at
            // 0 HP, or the next login would spawn it dead and disconnect it at once. Full HP on the next login instead.
            if (stats.MaxHp > 0 && (stats.Hp <= 0 || stats.Hp > stats.MaxHp))
                stats.Hp = stats.MaxHp;
            if (stats.MaxMp > 0 && (stats.Mp < 0 || stats.Mp > stats.MaxMp))
                stats.Mp = Math.Clamp(stats.Mp, 0, stats.MaxMp);
        }

        return new Character {
            CharId = chr.CharId,
            ObjectType = chr.ObjectType,
            Level = LevelRules.ClampLevel(chr.Level),
            CurrentFame = Math.Max(0, chr.CurrentFame),
            XpPoints = Math.Max(0, chr.XpPoints),
            SkinType = chr.SkinType,
            TextureOne = chr.TextureOne,
            TextureTwo = chr.TextureTwo,
            PetType = chr.PetType,
            HealthPotions = Math.Max(0, chr.HealthPotions),
            MagicPotions = Math.Max(0, chr.MagicPotions),
            IsDead = chr.IsDead,
            IsDeleted = chr.IsDeleted,
            HasBackpack = chr.HasBackpack,
            CreatedAt = chr.CreatedAt,
            ItemTypes = chr.ItemTypes ?? [],
            ItemDatas = chr.ItemDatas ?? [],
            Stats = stats,
            CombatStats = chr.CombatStats,
            DungeonStats = chr.DungeonStats,
            ExplorationStats = chr.ExplorationStats,
            KillStats = chr.KillStats
        };
    }

    // True if the row was updated; false if the account or the character slot does not exist (nothing is written then).
    public static async Task<bool> SaveAsync(int accountId, Character chr) {
        var clean = Clean(chr);
        if (clean == null)
            return false;

        var json = JsonSerializer.Serialize(clean);
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE accounts SET data = jsonb_set(data, ARRAY['Characters', @Idx], @Json::jsonb) " +
            "WHERE id=@Id AND jsonb_typeof(data->'Characters') = 'array' AND jsonb_array_length(data->'Characters') > @IdxInt",
            new { Idx = clean.CharId.ToString(), IdxInt = clean.CharId, Json = json, Id = accountId });

        if (rows == 0)
            _log.Warn($"SaveCharacter: account {accountId} has no character #{clean.CharId}; nothing written.");
        return rows > 0;
    }
}
