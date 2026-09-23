using Common.Database;
using Common.Database.Models;
using Common.Structs;

namespace Common.Tests;

// The game server saves one character at a time through CharacterDb (2026-09-21 audit, C4). What gets written is a cleaned copy:
// these pin the rules that keep a bad live state (dead at 0 HP, negative counts, null arrays) out of the database.
public class CharacterDbTests {

    private static Character Live() => new() {
        CharId = 2, ObjectType = 0x0300, Level = 7, CurrentFame = 12, XpPoints = 340, HealthPotions = 3, MagicPotions = 1,
        ItemTypes = [0xa00, -1, 0xa66, -1], ItemDatas = [1, 2, 3],
        Stats = new CharacterStats { Hp = 150, MaxHp = 200, Mp = 40, MaxMp = 100, Attack = 12, Defense = 5, Speed = 10, Dexterity = 11, Vitality = 8, Wisdom = 9 }
    };

    [Fact]
    public void AHealthyCharacterIsCopiedUnchanged() {
        var live = Live();
        var clean = CharacterDb.Clean(live)!;

        Assert.NotSame(live, clean);
        Assert.Equal(live.CharId, clean.CharId);
        Assert.Equal(live.ObjectType, clean.ObjectType);
        Assert.Equal(7, clean.Level);
        Assert.Equal(12, clean.CurrentFame);
        Assert.Equal(340, clean.XpPoints);
        Assert.Equal([0xa00, -1, 0xa66, -1], clean.ItemTypes);
        Assert.Equal([1, 2, 3], clean.ItemDatas);
        Assert.Equal(150, clean.Stats!.Hp);
        Assert.Equal(200, clean.Stats.MaxHp);
        Assert.Equal(12, clean.Stats.Attack);
        Assert.Equal(9, clean.Stats.Wisdom);
    }

    [Fact]
    public void ADeadCharacterIsStoredAtFullHealthBecauseDeathIsNotBuiltYet() {
        var live = Live();
        live.Stats!.Hp = 0;
        Assert.Equal(200, CharacterDb.Clean(live)!.Stats!.Hp);

        live.Stats.Hp = -35;
        Assert.Equal(200, CharacterDb.Clean(live)!.Stats!.Hp);
    }

    [Fact]
    public void HealthAndManaNeverExceedTheirMaximum() {
        var live = Live();
        live.Stats!.Hp = 999;
        live.Stats.Mp = 999;
        var clean = CharacterDb.Clean(live)!;
        Assert.Equal(200, clean.Stats!.Hp);
        Assert.Equal(100, clean.Stats.Mp);
    }

    [Fact]
    public void NegativeCountsAndOutOfRangeLevelsAreClamped() {
        var live = Live();
        live.HealthPotions = -1;
        live.MagicPotions = -9;
        live.CurrentFame = -5;
        live.XpPoints = -1;
        live.Level = LevelRules.MaxLevel + 50;
        var clean = CharacterDb.Clean(live)!;
        Assert.Equal(0, clean.HealthPotions);
        Assert.Equal(0, clean.MagicPotions);
        Assert.Equal(0, clean.CurrentFame);
        Assert.Equal(0, clean.XpPoints);
        Assert.Equal(LevelRules.MaxLevel, clean.Level);

        live.Level = 0;
        Assert.Equal(1, CharacterDb.Clean(live)!.Level);
    }

    [Fact]
    public void NullArraysBecomeEmptyOnes() {
        var live = Live();
        live.ItemTypes = null;
        live.ItemDatas = null;
        var clean = CharacterDb.Clean(live)!;
        Assert.NotNull(clean.ItemTypes);
        Assert.NotNull(clean.ItemDatas);
        Assert.Empty(clean.ItemTypes);
    }

    [Fact]
    public void UnsaveableCharactersAreRefused() {
        Assert.Null(CharacterDb.Clean(null));
        var noId = Live(); noId.CharId = -1;
        Assert.Null(CharacterDb.Clean(noId));
        var noClass = Live(); noClass.ObjectType = 0;
        Assert.Null(CharacterDb.Clean(noClass));
    }

    [Fact]
    public void CleaningDoesNotTouchTheLiveRecord() {
        var live = Live();
        live.Stats!.Hp = 0;
        CharacterDb.Clean(live);
        Assert.Equal(0, live.Stats.Hp);     // the game's own copy is left alone
    }
}
