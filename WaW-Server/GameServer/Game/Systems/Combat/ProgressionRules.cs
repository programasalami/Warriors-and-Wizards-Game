using Common;
using Common.Resources.Xml.Descriptors;
using Common.Structs;

namespace GameServer.Game.Systems.Combat;

// The numbers behind killing things (2026-09-22, "Bugs and Todo": no XP, no levels, no fame, no regen, potions did nothing). Pure, tested in
// ProgressionRulesTests; Progression.cs applies them to a player's stats.
//
// - XP for a kill: the enemy's max HP / 10, times its XpMult (Objects.xml), at least 1. Everyone who damaged it gets the full amount.
// - Levels: LevelRules (Common.Protocol) says what a level costs; a kill can carry a player through several levels at once.
// - Stat growth per level: each class's <LevelIncrease min max>Stat</LevelIncrease> (Players.xml), rolled between min and max, never above the
//   stat's max. HP and MP refill on a level-up.
// - Fame: 1 character fame per 1,000 XP earned, whatever the level (with levels up to 999 nobody reaches "max level fame" like the original).
// - Regeneration per second: HP 1 + 0.12 x Vitality, MP 0.5 + 0.06 x Wisdom (the original game's curve), while alive.
// - A loot bag stays 60 s, then disappears with what is left in it.
public static class ProgressionRules {
    public const int XpPerFame = 1000;
    public const int LootBagLifetimeMs = 60_000;
    public const int MapEnemyRespawnMs = 5_000;      // a killable enemy placed by the map (the Nexus targets) is back this long after it dies

    public static int XpForKill(int enemyMaxHp, float xpMult) {
        var xp = (int)MathF.Round(Math.Max(0, enemyMaxHp) / 10f * (xpMult <= 0 ? 1f : xpMult));
        return Math.Max(1, xp);
    }

    public readonly record struct LevelResult(int Level, int Xp, int LevelsGained);

    // Adds `earned` XP to a player at `level` with `xp` towards the next one; walks up as many levels as that pays for.
    public static LevelResult AddXp(int level, int xp, int earned) {
        level = LevelRules.ClampLevel(level);
        xp = Math.Max(0, xp) + Math.Max(0, earned);
        var gained = 0;
        while (!LevelRules.IsMaxLevel(level)) {
            var cost = LevelRules.XpToNextLevel(level);
            if (cost <= 0 || xp < cost)
                break;
            xp -= cost;
            level++;
            gained++;
        }
        if (LevelRules.IsMaxLevel(level))
            xp = 0;                      // nothing left to earn
        return new LevelResult(level, xp, gained);
    }

    // Fame for XP: whole thousands, the remainder carried to the next kill (carry in, carry out).
    public static int FameFor(int earnedXp, ref int carry) {
        carry = Math.Max(0, carry) + Math.Max(0, earnedXp);
        var fame = carry / XpPerFame;
        carry %= XpPerFame;
        return fame;
    }

    // One level's growth of one stat: a roll between min and max, capped at the class's maximum for that stat.
    public static int Grow(int current, int min, int max, int cap, Random rng) {
        if (max < min) (min, max) = (max, min);
        var roll = rng.Next(min, max + 1);
        return Math.Min(cap, current + Math.Max(0, roll));
    }

    public static float HpRegenPerSecond(int vitality) => 1f + 0.12f * Math.Max(0, vitality);

    public static float MpRegenPerSecond(int wisdom) => 0.5f + 0.06f * Math.Max(0, wisdom);

    // How much to add this tick: the whole points of (carry + rate x seconds); the fraction stays in the carry for the next tick.
    public static int Regenerate(float perSecond, int elapsedMs, ref float carry) {
        carry += perSecond * Math.Max(0, elapsedMs) / 1000f;
        var whole = (int)carry;
        carry -= whole;
        return whole;
    }

    // The stat a <LevelIncrease> names in Players.xml (the mapping lives with the descriptor).
    public static bool TryStat(string xmlName, out StatType stat) => PlayerDesc.TryStatFromXmlName(xmlName, out stat);
}
