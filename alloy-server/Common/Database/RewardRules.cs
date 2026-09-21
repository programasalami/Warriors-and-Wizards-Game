using System;

namespace Common.Database;

// What a reward pays. Gold is the account's Credits, Fame is the account's CurrentFame (shown as the two coins in the Character Book).
public readonly record struct Reward(int Gold, int Fame);

// How long after you claim a daily reward before you can claim it again: a real 24 hours from the moment you claimed (NOT a calendar day - a reset at midnight
// would let you claim twice within a few hours, e.g. at 11 pm and again just after midnight).
public static class DailyCooldown {
    public static readonly TimeSpan Length = TimeSpan.FromHours(24);

    // Whole seconds left until the next claim is allowed (0 = ready now, or never claimed). The last claim in the future (a clock that jumped back) counts as a
    // full cooldown, never as ready.
    public static int SecondsLeft(DateTime? lastClaimUtc, DateTime nowUtc) {
        if (lastClaimUtc == null)
            return 0;

        var left = lastClaimUtc.Value + Length - nowUtc;
        if (left <= TimeSpan.Zero)
            return 0;

        return (int) Math.Min(Math.Ceiling(left.TotalSeconds), Length.TotalSeconds);
    }
}

// The Daily Gift: one gift every 24 hours; claiming again within the next 24 hours after that (so 24 to 48 hours after the last claim) builds a streak that walks
// through a 7-day cycle (day 7 is the big one, then it starts again). Leave it longer than that and the streak starts over at day 1. All pure - the database only
// stores "when it was last claimed" and "streak so far" (see RewardsDb).
public static class DailyGiftRules {
    public const int Days = 7;

    // Claim again after the cooldown but before this much time has passed since the last claim, or the streak is lost.
    public static readonly TimeSpan StreakWindow = TimeSpan.FromHours(48);

    public static readonly Reward[] Cycle = [
        new(100, 0), new(150, 0), new(200, 0), new(300, 0), new(400, 0), new(600, 0), new(1000, 50)
    ];

    // SecondsLeft: how long until the next gift can be opened (0 when CanClaim).
    public readonly record struct State(bool CanClaim, int Day, int Streak, int SecondsLeft);

    // lastClaim: when the last gift was opened (UTC, null = never), streak: consecutive gifts so far.
    // Day = which day of the cycle (0 to 6) a claim would give (or, while waiting, the one it will give when the wait is over).
    public static State Evaluate(DateTime? lastClaim, int streak, DateTime now) {
        streak = Math.Max(0, streak);
        if (lastClaim == null)
            return new State(true, 0, 0, 0);                                   // never claimed: start the cycle

        var wait = DailyCooldown.SecondsLeft(lastClaim, now);
        if (wait > 0)
            return new State(false, streak % Days, streak, wait);              // still cooling down; the streak carries on afterwards

        if (now - lastClaim.Value <= StreakWindow)
            return new State(true, streak % Days, streak, 0);                  // claimed in time: the streak carries on

        return new State(true, 0, 0, 0);                                       // left it too long: start over
    }

    // The streak after claiming right now (call only when Evaluate says CanClaim).
    public static int StreakAfterClaim(DateTime? lastClaim, int streak, DateTime now) => Evaluate(lastClaim, streak, now).Streak + 1;

    public static Reward RewardFor(int day) => Cycle[((day % Days) + Days) % Days];
}

// The Daily Spin: one spin every 24 hours (see DailyCooldown). The server rolls, so the client only animates towards the answer. The table below is also sent to the client, which
// draws one wheel segment per entry, in this order.
public static class SpinWheel {
    public readonly record struct Prize(Reward Reward, int Weight);

    // Weights add up to 100, so each weight is its own percentage chance. The order is the order around the wheel: big prizes are spread out.
    public static readonly Prize[] Prizes = [
        new(new Reward(100, 0), 22),
        new(new Reward(0, 25), 12),
        new(new Reward(500, 0), 12),
        new(new Reward(0, 50), 8),
        new(new Reward(150, 0), 20),
        new(new Reward(1000, 0), 3),         // the jackpot
        new(new Reward(250, 0), 16),
        new(new Reward(0, 100), 7),
    ];

    public static int TotalWeight {
        get {
            var total = 0;
            foreach (var prize in Prizes)
                total += prize.Weight;
            return total;
        }
    }

    // roll: 0 (inclusive) up to TotalWeight (exclusive). Returns the index into Prizes.
    public static int Pick(int roll) {
        var acc = 0;
        for (var i = 0; i < Prizes.Length; i++) {
            acc += Prizes[i].Weight;
            if (roll < acc)
                return i;
        }

        return Prizes.Length - 1;
    }
}

// Limits for messages in the Inbox.
public static class InboxRules {
    public const int MaxSubject = 60;
    public const int MaxBody = 600;
    public const int MaxMessagesPerAccount = 100;
    public const int MaxAttachment = 1_000_000;

    public static string Clean(string text, int max) {
        text = (text ?? string.Empty).Replace("\r", string.Empty).Trim();
        return text.Length > max ? text[..max].TrimEnd() : text;
    }

    // An attachment must be a sane, non-negative amount.
    public static bool ValidAttachment(int gold, int fame) => gold >= 0 && fame >= 0 && gold <= MaxAttachment && fame <= MaxAttachment;
}
