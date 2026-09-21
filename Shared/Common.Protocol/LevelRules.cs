using System;

namespace Common.Structs;

// How much experience each level needs, and the highest level a character can reach. ONE place, used by both the game server and the client, so the two can
// never disagree.
//
// The first 20 levels keep the original game's curve exactly. Beyond 20 the original curve grows with the square of the level (it would ask for about ten million
// experience per level around 999, and about three BILLION in total - more than an int can hold), so from level 20 on each level costs a little more than the one
// before it, in a straight line. The two numbers to tune are below.
public static class LevelRules {
    public const int MaxLevel = 999;

    // Levels up to here use the original formula; the cost of level 20 -> 21 continues from what 19 -> 20 cost.
    public const int OriginalCurveUpTo = 20;

    // Every level after 20 costs this much more than the one before it.
    public const int ExtraPerLevelAfterOriginal = 150;

    // Experience needed to get from `level` to `level + 1`. 0 at the highest level (there is nothing left to earn).
    public static int XpToNextLevel(int level) {
        level = Math.Max(1, level);
        if (level >= MaxLevel) {
            return 0;
        }

        if (level <= OriginalCurveUpTo) {
            return Original(level);
        }

        return Original(OriginalCurveUpTo) + (level - OriginalCurveUpTo) * ExtraPerLevelAfterOriginal;
    }

    // Total experience earned from level 1 up to the moment `level` is reached.
    public static long TotalXpToReach(int level) {
        level = Math.Clamp(level, 1, MaxLevel);
        long total = 0;
        for (var l = 1; l < level; l++) {
            total += XpToNextLevel(l);
        }

        return total;
    }

    public static bool IsMaxLevel(int level) => level >= MaxLevel;

    public static int ClampLevel(int level) => Math.Clamp(level, 1, MaxLevel);

    // The original game's formula (levels 1 to 20).
    private static int Original(int level) => (int) (50f + (level - 1f) * 100f * (1f + level / 10f));
}
