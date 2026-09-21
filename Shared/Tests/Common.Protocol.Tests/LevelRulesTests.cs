using Common.Structs;

namespace Common.Protocol.Tests;

public class LevelRulesTests {

    [Fact]
    public void TheHighestLevelIs999() => Assert.Equal(999, LevelRules.MaxLevel);

    [Theory]
    [InlineData(1, 50)]              // the original game's numbers for the early levels, unchanged
    [InlineData(2, 170)]
    [InlineData(10, 1850)]
    [InlineData(19, 5270)]
    [InlineData(20, 5750)]
    public void TheFirstTwentyLevelsAreTheOriginalCurve(int level, int expected) => Assert.Equal(expected, LevelRules.XpToNextLevel(level));

    [Fact]
    public void AfterLevelTwentyEachLevelCostsTheSameExtraAmountMore() {
        Assert.Equal(5750 + LevelRules.ExtraPerLevelAfterOriginal, LevelRules.XpToNextLevel(21));
        Assert.Equal(5750 + 100 * LevelRules.ExtraPerLevelAfterOriginal, LevelRules.XpToNextLevel(120));

        for (var level = 21; level < LevelRules.MaxLevel - 1; level++) {
            Assert.Equal(LevelRules.ExtraPerLevelAfterOriginal, LevelRules.XpToNextLevel(level + 1) - LevelRules.XpToNextLevel(level));
        }
    }

    [Fact]
    public void ALevelNeverCostsLessThanTheOneBeforeIt() {
        var previous = 0;
        for (var level = 1; level < LevelRules.MaxLevel; level++) {
            var cost = LevelRules.XpToNextLevel(level);
            Assert.True(cost >= previous, $"level {level} costs {cost}, less than the level before ({previous})");
            Assert.True(cost > 0);
            previous = cost;
        }
    }

    [Fact]
    public void AtTheHighestLevelThereIsNothingLeftToEarn() {
        Assert.Equal(0, LevelRules.XpToNextLevel(LevelRules.MaxLevel));
        Assert.Equal(0, LevelRules.XpToNextLevel(5000));
        Assert.True(LevelRules.XpToNextLevel(LevelRules.MaxLevel - 1) > 0);
        Assert.True(LevelRules.IsMaxLevel(999));
        Assert.False(LevelRules.IsMaxLevel(998));
    }

    [Fact]
    public void NothingBreaksForNonsenseLevels() {
        Assert.Equal(50, LevelRules.XpToNextLevel(0));
        Assert.Equal(50, LevelRules.XpToNextLevel(-7));
        Assert.Equal(1, LevelRules.ClampLevel(-3));
        Assert.Equal(999, LevelRules.ClampLevel(100_000));
    }

    [Fact]
    public void TheWholeClimbToLevel999FitsInAnInt() {
        // Experience is stored in an int. The original curve would need about 3.3 billion in total; this one must stay far below 2.1 billion.
        var total = LevelRules.TotalXpToReach(LevelRules.MaxLevel);
        Assert.InRange(total, 1L, int.MaxValue / 4);
        Assert.Equal(0, LevelRules.TotalXpToReach(1));
        Assert.Equal(50, LevelRules.TotalXpToReach(2));
        Assert.Equal(LevelRules.TotalXpToReach(999), LevelRules.TotalXpToReach(5000));
    }

    [Fact]
    public void TheOriginalCurveReallyWouldHaveOverflowed() {
        // Why the curve changes after level 20: the original formula's total for 999 levels does not fit an int.
        long original = 0;
        for (var l = 1; l < 999; l++) {
            original += (long) (50f + (l - 1f) * 100f * (1f + l / 10f));
        }

        Assert.True(original > int.MaxValue);
    }
}
