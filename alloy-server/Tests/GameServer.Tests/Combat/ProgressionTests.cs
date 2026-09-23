using Common;
using Common.Database.Models;
using Common.Resources.Xml;
using Common.Resources.Xml.Descriptors;
using Common.Structs;
using Common.Utilities.Collections;
using GameServer.Game;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Network;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Systems.Inventory;
using GameServer.Game.Worlds;
using GameServer.Game.Worlds.Logic;
using Common.Utilities;
using GameServer.Tests.Worlds;

namespace GameServer.Tests.Combat;

/// <summary>XP, levels, stat growth, fame, regeneration and potions - the "nothing happens in combat" items of Bugs and Todo (2026-09-22).</summary>
public class ProgressionRulesTests {
    [Theory]
    [InlineData(100, 1f, 10)]
    [InlineData(5, 1f, 1)]           // never less than 1
    [InlineData(200, 2.5f, 50)]
    [InlineData(0, 0f, 1)]
    public void XpForAKillIsATenthOfTheHealthTimesTheMultiplier(int hp, float mult, int expected) => Assert.Equal(expected, ProgressionRules.XpForKill(hp, mult));

    [Fact]
    public void XpWalksUpLevelsAndKeepsTheRemainder() {
        var cost1 = LevelRules.XpToNextLevel(1);
        var cost2 = LevelRules.XpToNextLevel(2);
        var r = ProgressionRules.AddXp(1, 0, cost1 + cost2 + 7);
        Assert.Equal(3, r.Level);
        Assert.Equal(2, r.LevelsGained);
        Assert.Equal(7, r.Xp);

        var none = ProgressionRules.AddXp(1, 0, cost1 - 1);
        Assert.Equal(1, none.Level);
        Assert.Equal(cost1 - 1, none.Xp);

        var top = ProgressionRules.AddXp(LevelRules.MaxLevel, 5, 100);
        Assert.Equal(LevelRules.MaxLevel, top.Level);
        Assert.Equal(0, top.Xp);
        Assert.Equal(0, top.LevelsGained);
    }

    [Fact]
    public void OneFamePerThousandXpWithACarry() {
        var carry = 0;
        Assert.Equal(0, ProgressionRules.FameFor(600, ref carry));
        Assert.Equal(600, carry);
        Assert.Equal(1, ProgressionRules.FameFor(500, ref carry));      // 1,100 -> 1 fame, 100 carried
        Assert.Equal(100, carry);
        Assert.Equal(2, ProgressionRules.FameFor(1900, ref carry));
        Assert.Equal(0, carry);
    }

    [Fact]
    public void GrowthStaysWithinTheRollAndUnderTheCap() {
        var rng = new Random(1);
        for (var i = 0; i < 200; i++) {
            var v = ProgressionRules.Grow(10, 2, 5, 100, rng);
            Assert.InRange(v, 12, 15);
        }
        Assert.Equal(14, ProgressionRules.Grow(13, 5, 5, 14, rng));      // capped
        Assert.Equal(14, ProgressionRules.Grow(14, 5, 5, 14, rng));      // already at the cap
        Assert.Equal(10, ProgressionRules.Grow(10, 0, 0, 14, rng));      // a 0-0 roll grows nothing (Defense)
    }

    [Fact]
    public void RegenerationAccumulatesFractions() {
        Assert.Equal(1f, ProgressionRules.HpRegenPerSecond(0));
        Assert.Equal(1f + 0.12f * 25, ProgressionRules.HpRegenPerSecond(25));
        Assert.Equal(0.5f + 0.06f * 10, ProgressionRules.MpRegenPerSecond(10));

        var carry = 0f;
        var total = 0;
        for (var tick = 0; tick < 20; tick++)                    // 20 x 50 ms = 1 s at 2.2 HP/s
            total += ProgressionRules.Regenerate(2.2f, 50, ref carry);
        Assert.Equal(2, total);
        Assert.InRange(carry, 0.19f, 0.21f);
    }

    [Fact]
    public void ThePlayerClassesDeclareTheirGrowth() {
        RealmWorldTests.EnsureGameDataLoaded();
        foreach (var desc in XmlLibrary.PlayerDescs.Values) {
            Assert.NotEmpty(desc.LevelIncreases);
            Assert.Contains(desc.LevelIncreases, i => i.Stat == StatType.MaxHP && i.Max >= i.Min && i.Min >= 0);
            Assert.Contains(desc.LevelIncreases, i => i.Stat == StatType.Vitality);      // "HpRegen" in the XML
        }
    }
}

/// <summary>The same rules applied to a real player in a real world: a kill pays XP and levels, and a potion is drunk.</summary>
public class ProgressionWorldTests {
    private const ushort Pirate = 0x600;
    private const int HealthPotion = 0xa22;

    private static AccountStats Stats() => new() { ClassStats = [new ClassStats { ObjectType = 0x031d }] };

    private static (Vault World, User User, EntityId PlayerId) Build(int accountId) {
        RealmWorldTests.EnsureGameDataLoaded();
        BehaviorLibrary.Load();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;
        var vault = new Vault(0, 0, Common.Resources.World.WorldLibrary.WorldConfigs["Vault"]);
        var account = new Account { Id = accountId, Name = "progtest", VaultCount = 2, Stats = Stats() };
        vault.SetupChests(account);
        var user = new User();
        user.SetGameInfo(account, 1, vault);
        user.GameInfo.Load(new Character {
            CharId = 0, ObjectType = 0x031d, Level = 1,
            ItemTypes = Enumerable.Repeat(-1, 20).ToArray(), ItemDatas = []
        }, vault);
        return (vault, user, user.GameInfo.PlayerId);
    }

    [Fact]
    public void KillingAnEnemyPaysXpToWhoeverDamagedIt() {
        var (world, user, playerId) = Build(9301);
        var entity = new Entity(Pirate);
        ref var pirate = ref world.EnterWorld(ref entity);
        var pirateId = pirate.Id;
        var xpExpected = ProgressionRules.XpForKill(pirate.Desc.MaxHP, pirate.Desc.XpMult);

        ref var combat = ref world.EntityCombat.Get(pirateId);
        combat.Damage(playerId, 1_000_000, user.GameInfo.Account.Id);
        var time = new RealmTime { ElapsedMsDelta = 50 };
        world.EntityCombat.Tick(ref time);                       // applies the damage, the pirate dies, the kill is rewarded

        ref var stats = ref world.EntityStats.Get(playerId);
        var level = stats.GetInt(StatType.Level);
        var xp = stats.GetInt(StatType.Experience);
        Assert.True(level > 1 || xp == xpExpected, $"level {level}, xp {xp}, expected {xpExpected} xp");
        Assert.Equal(LevelRules.XpToNextLevel(level), stats.GetInt(StatType.NextLevelXp));
        world.Update();                                          // queued removals happen between ticks
        Assert.Equal(EntityId.Null, world.Entities.Get(pirateId).Id);
    }

    [Fact]
    public void ALevelUpGrowsStatsWithinTheClassRulesAndHealsFully() {
        var (world, user, playerId) = Build(9302);
        ref var stats = ref world.EntityStats.Get(playerId);
        var desc = XmlLibrary.PlayerDescs[world.Entities.Get(playerId).ObjectType];
        var maxHpBefore = stats.GetInt(StatType.MaxHP);
        stats.Set(StatType.HP, 1);
        var carry = 0;

        var reward = Progression.Apply(ref stats, desc, LevelRules.XpToNextLevel(1), ref carry, new Random(7));

        Assert.Equal(1, reward.LevelsGained);
        Assert.Equal(2, stats.GetInt(StatType.Level));
        var hpInc = desc.LevelIncreases.First(i => i.Stat == StatType.MaxHP);
        Assert.InRange(stats.GetInt(StatType.MaxHP), maxHpBefore + hpInc.Min, maxHpBefore + hpInc.Max);
        Assert.Equal(stats.GetInt(StatType.MaxHP), stats.GetInt(StatType.HP));       // healed to full
        Assert.True(stats.GetInt(StatType.Defense) <= desc.Stats[StatType.Defense].MaxValue);
    }

    [Fact]
    public void DrinkingAHealthPotionHealsAndRemovesIt() {
        var (world, user, playerId) = Build(9303);
        ref var inv = ref world.EntityInventories.Get(playerId);
        var slot = inv.TryAdd(new Item(XmlLibrary.ItemDescs[HealthPotion].Root));
        Assert.Equal(4, slot);
        ref var stats = ref world.EntityStats.Get(playerId);
        var max = stats.GetInt(StatType.MaxHP);
        stats.Set(StatType.HP, Math.Max(1, max - 30));

        var result = UseItem.Apply(world, playerId, slot);

        Assert.True(result.Used);
        Assert.Equal(30, result.Healed);                          // capped at max HP even though the potion gives 100
        Assert.Equal(max, stats.GetInt(StatType.HP));
        Assert.Null(world.EntityInventories.Get(playerId)[slot]);
        Assert.False(UseItem.Apply(world, playerId, slot).Used);  // nothing there any more
        Assert.False(UseItem.Apply(world, playerId, 0).Used);     // the sword is not a consumable
    }
}

/// <summary>A killable enemy the map placed (the Nexus targets) is back a few seconds after it dies, at its spot, and it pays XP like any kill.</summary>
public class MapEnemyRespawnTests {
    private const ushort TargetStrong = 0x1806;

    private static List<(EntityId Id, float X, float Y)> Targets(World world) {
        var list = new List<(EntityId, float, float)>();
        foreach (ref var en in world.Entities)
            if (en.ObjectType == TargetStrong) {
                var pos = world.EntityStats.Get(en.Id).Pos;
                list.Add((en.Id, pos.X, pos.Y));
            }
        return list;
    }

    [Fact]
    public void TheNexusTargetComesBackAfterItDies() {
        RealmWorldTests.EnsureGameDataLoaded();
        BehaviorLibrary.Load();
        if (GameLogic.TPS == 0)
            GameLogic.TPS = 20;
        var nexus = RealmManager.Worlds[World.NEXUS_ID];

        var before = Targets(nexus);                             // the map may place the same target on several tiles
        Assert.NotEmpty(before);
        var victim = before[0];

        ref var combat = ref nexus.EntityCombat.Get(victim.Id);
        combat.Damage(EntityId.Null, 1_000_000, 0);              // nobody to reward: only the death matters here
        var time = new RealmTime { ElapsedMsDelta = 50 };
        nexus.EntityCombat.Tick(ref time);
        nexus.Update();
        Assert.Equal(EntityId.Null, nexus.Entities.Get(victim.Id).Id);
        Assert.Equal(before.Count - 1, Targets(nexus).Count);

        GameLogic.WorldTime.TickCount += TimeUtils.TicksFromTime(ProgressionRules.MapEnemyRespawnMs, GameLogic.TPS) + 1;
        nexus.Tick(ref time);                                     // the respawn timer fires

        var after = Targets(nexus);
        Assert.Equal(before.Count, after.Count);
        // (the entity manager may hand the freed index back, so the id is not compared)
        Assert.Contains(after, t => Math.Abs(t.X - victim.X) < 0.01f && Math.Abs(t.Y - victim.Y) < 0.01f);   // at the same spot
    }
}
