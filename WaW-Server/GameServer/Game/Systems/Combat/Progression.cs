using Common;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Game.Network;
using GameServer.Game.Network.Messaging;
using GameServer.Game.Systems.Stats;
using GameServer.Game.Worlds;

namespace GameServer.Game.Systems.Combat;

// Applies ProgressionRules to a player: XP for a kill, level-ups with stat growth and a full heal, fame per 1,000 XP, and the floating texts
// ("+12 XP", "Level up!", "+1 Fame"). Called from EntityCombat.Death for every player that damaged the dead enemy (2026-09-22).
public static class Progression {
    private const int XpColor = 0x3CC46A;
    private const int LevelColor = 0xF2C94C;
    private const int FameColor = 0xE6A23C;

    public readonly record struct KillReward(int Xp, int LevelsGained, int Fame, int NewLevel);

    // The stats side, without any network: what the tests cover.
    public static KillReward Apply(ref EntityStats stats, PlayerDesc desc, int xp, ref int fameCarry, Random rng) {
        var level = stats.GetInt(StatType.Level);
        var result = ProgressionRules.AddXp(level, stats.GetInt(StatType.Experience), xp);
        stats.Set(StatType.Experience, result.Xp);
        stats.Set(StatType.NextLevelXp, Common.Structs.LevelRules.XpToNextLevel(result.Level));

        if (result.LevelsGained > 0) {
            stats.Set(StatType.Level, result.Level);
            for (var i = 0; i < result.LevelsGained; i++)
                GrowOnce(ref stats, desc, rng);
            stats.Set(StatType.HP, stats.GetInt(StatType.MaxHP));      // a level-up heals fully, like the original
            stats.Set(StatType.MP, stats.GetInt(StatType.MaxMP));
        }

        var fame = ProgressionRules.FameFor(xp, ref fameCarry);
        if (fame > 0)
            stats.Set(StatType.CharFame, stats.GetInt(StatType.CharFame) + fame);

        return new KillReward(xp, result.LevelsGained, fame, result.Level);
    }

    private static void GrowOnce(ref EntityStats stats, PlayerDesc desc, Random rng) {
        if (desc?.LevelIncreases == null)
            return;
        foreach (var inc in desc.LevelIncreases) {
            var cap = desc.Stats != null && desc.Stats.TryGetValue(inc.Stat, out var sd) ? sd.MaxValue : int.MaxValue;
            stats.Set(inc.Stat, ProgressionRules.Grow(stats.GetInt(inc.Stat), inc.Min, inc.Max, cap, rng));
        }
    }

    // A kill: every player in the enemy's damage records who is still in the world is rewarded.
    public static void AwardKill(World world, ref Entity enemy, ref EntityCombat combat) {
        var desc = enemy.Desc;
        var xp = ProgressionRules.XpForKill(desc.MaxHP, desc.XpMult);
        foreach (ref var record in combat.DamageRecords) {
            if (record.DamageDealt <= 0 || !world.Users.TryGetValue(record.Id, out var user))
                continue;
            ref var stats = ref world.EntityStats.Get(record.Id);
            if (stats.Id == EntityId.Null || stats.GetInt(StatType.HP) <= 0)
                continue;

            var playerDesc = XmlLibrary.PlayerDescs.TryGetValue(world.Entities.Get(record.Id).ObjectType, out var pd) ? pd : null;
            var reward = Apply(ref stats, playerDesc, xp, ref user.GameInfo.FameXpCarry, Random.Shared);

            Send(user, new Notification(record.Id, $"+{reward.Xp} XP", XpColor, 20));
            if (reward.LevelsGained > 0)
                Send(user, new Notification(record.Id, $"Level {reward.NewLevel}!", LevelColor, 30));
            if (reward.Fame > 0)
                Send(user, new Notification(record.Id, $"+{reward.Fame} Fame", FameColor, 22));
        }
    }

    private static void Send(User user, in Notification packet) {
        if (user?.Network == null)
            return;
        try { user.SendPacket(packet); } catch { }
    }
}
