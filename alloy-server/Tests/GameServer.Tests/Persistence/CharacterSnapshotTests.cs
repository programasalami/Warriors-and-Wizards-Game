using Common;
using Common.Database.Models;
using GameServer.Game.Systems.Persistence;
using GameServer.Tests.Fixtures;

namespace GameServer.Tests.Persistence;

// The character record is refreshed from the live entity before every save (disconnect, world switch, autosave, shutdown).
// This checks that every stat LoadCharacterStats reads at login is written back by CaptureStats, so nothing is lost in a round trip.
public class CharacterSnapshotTests {

    [Fact]
    public void EveryLoadedStatIsWrittenBack() {
        var world = TestWorldFactory.CreateWorld();
        var id = TestWorldFactory.SpawnCharacter(world);
        ref var stats = ref world.EntityStats.Get(id);

        stats.Set(StatType.Level, 14);
        stats.Set(StatType.CharFame, 77);
        stats.Set(StatType.Experience, 1234);
        stats.Set(StatType.HealthPotionStack, 4);
        stats.Set(StatType.MagicPotionStack, 2);
        stats.Set(StatType.MaxHP, 310);
        stats.Set(StatType.HP, 123);
        stats.Set(StatType.MaxMP, 150);
        stats.Set(StatType.MP, 60);
        stats.Set(StatType.Attack, 21);
        stats.Set(StatType.Defense, 9);
        stats.Set(StatType.Speed, 30);
        stats.Set(StatType.Dexterity, 25);
        stats.Set(StatType.Vitality, 18);
        stats.Set(StatType.Wisdom, 16);

        var chr = new Character { CharId = 0, ObjectType = TestWorldFactory.CharacterObjectType };
        CharacterSnapshot.CaptureStats(ref stats, chr);

        Assert.Equal(14, chr.Level);
        Assert.Equal(77, chr.CurrentFame);
        Assert.Equal(1234, chr.XpPoints);
        Assert.Equal(4, chr.HealthPotions);
        Assert.Equal(2, chr.MagicPotions);
        Assert.NotNull(chr.Stats);
        Assert.Equal(310, chr.Stats!.MaxHp);
        Assert.Equal(123, chr.Stats.Hp);
        Assert.Equal(150, chr.Stats.MaxMp);
        Assert.Equal(60, chr.Stats.Mp);
        Assert.Equal(21, chr.Stats.Attack);
        Assert.Equal(9, chr.Stats.Defense);
        Assert.Equal(30, chr.Stats.Speed);
        Assert.Equal(25, chr.Stats.Dexterity);
        Assert.Equal(18, chr.Stats.Vitality);
        Assert.Equal(16, chr.Stats.Wisdom);
    }

    [Fact]
    public void CloneIsIndependentOfTheLiveRecord() {
        var live = new Character { CharId = 3, ObjectType = 0x300, Level = 5, ItemTypes = [1, 2, 3], Stats = new CharacterStats { Hp = 10, MaxHp = 20 } };
        var copy = CharacterSnapshot.Clone(live);

        live.Level = 99;
        live.ItemTypes[0] = 42;
        live.Stats.Hp = 0;

        Assert.Equal(5, copy.Level);
        Assert.Equal(1, copy.ItemTypes[0]);
        Assert.Equal(10, copy.Stats!.Hp);
    }
}
