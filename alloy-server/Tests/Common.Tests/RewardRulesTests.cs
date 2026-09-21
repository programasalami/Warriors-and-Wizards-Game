using Common.Database;

namespace Common.Tests;

public class RewardRulesTests {
    private static readonly DateTime Start = new(2026, 9, 20, 21, 30, 0, DateTimeKind.Utc);      // 9:30 pm UTC: the old midnight reset was 2.5 hours away

    public class Cooldown {
        [Fact]
        public void NeverClaimedIsReady() => Assert.Equal(0, DailyCooldown.SecondsLeft(null, Start));

        [Theory]
        [InlineData(0, 24 * 3600)]
        [InlineData(1, 24 * 3600 - 1)]
        [InlineData(2.5 * 3600, 21.5 * 3600)]           // the reported case: claimed, then 2.5 hours later it must NOT be ready
        [InlineData(24 * 3600 - 1, 1)]
        [InlineData(24 * 3600, 0)]
        [InlineData(30 * 3600, 0)]
        public void ItIsAFull24HoursFromTheClaim(double secondsLater, double expectedLeft) =>
            Assert.Equal((int) expectedLeft, DailyCooldown.SecondsLeft(Start, Start.AddSeconds(secondsLater)));

        [Fact]
        public void TheMidnightThatUsedToResetItDoesNothing() {
            // claimed at 9:30 pm UTC; a moment after midnight UTC (the old reset) it is still cooling down
            var afterMidnight = new DateTime(2026, 9, 21, 0, 5, 0, DateTimeKind.Utc);
            Assert.True(DailyCooldown.SecondsLeft(Start, afterMidnight) > 20 * 3600);
        }

        [Fact]
        public void AClaimInTheFutureNeverCountsAsReady() {
            var left = DailyCooldown.SecondsLeft(Start.AddDays(3), Start);
            Assert.Equal(24 * 3600, left);        // capped at one full cooldown
        }
    }

    public class DailyGift {
        [Fact]
        public void TheFirstEverClaimStartsTheCycle() {
            var s = DailyGiftRules.Evaluate(null, 0, Start);
            Assert.True(s.CanClaim);
            Assert.Equal(0, s.Day);
            Assert.Equal(1, DailyGiftRules.StreakAfterClaim(null, 0, Start));
        }

        [Fact]
        public void OnlyOneClaimEvery24Hours() {
            var s = DailyGiftRules.Evaluate(Start, 3, Start.AddHours(2.5));
            Assert.False(s.CanClaim);
            Assert.Equal(3, s.Day);        // the next gift is day 4 (index 3)
            Assert.Equal((int) (21.5 * 3600), s.SecondsLeft);
        }

        [Fact]
        public void ClaimingAgainRightOnTimeCarriesTheStreak() {
            var s = DailyGiftRules.Evaluate(Start, 3, Start.AddHours(24));
            Assert.True(s.CanClaim);
            Assert.Equal(3, s.Day);
            Assert.Equal(4, DailyGiftRules.StreakAfterClaim(Start, 3, Start.AddHours(24)));
            Assert.True(DailyGiftRules.Evaluate(Start, 3, Start.AddHours(47)).CanClaim);
            Assert.Equal(3, DailyGiftRules.Evaluate(Start, 3, Start.AddHours(47)).Streak);
        }

        [Fact]
        public void ClaimingAsSoonAsItIsReadyWalksTheCycle() {
            // one claim exactly every 24 hours for two weeks: day 1..7, then again 1..7
            DateTime? last = null;
            var streak = 0;
            for (var i = 0; i < 14; i++) {
                var now = Start.AddHours(24 * i);
                var s = DailyGiftRules.Evaluate(last, streak, now);
                Assert.True(s.CanClaim);
                Assert.Equal(i % 7, s.Day);
                streak = DailyGiftRules.StreakAfterClaim(last, streak, now);
                last = now;
            }

            Assert.Equal(14, streak);
        }

        [Fact]
        public void ClaimingAsOftenAsAllowedNeverGivesTwoGiftsInADay() {
            // try every 5 minutes for 3 days: exactly 3 claims get through, each 24 hours after the one before
            DateTime? last = null;
            var streak = 0;
            var claims = new List<DateTime>();
            for (var minute = 0; minute <= 3 * 24 * 60; minute += 5) {
                var now = Start.AddMinutes(minute);
                if (!DailyGiftRules.Evaluate(last, streak, now).CanClaim)
                    continue;
                streak = DailyGiftRules.StreakAfterClaim(last, streak, now);
                last = now;
                claims.Add(now);
            }

            Assert.Equal(4, claims.Count);         // at 0h, 24h, 48h, 72h
            for (var i = 1; i < claims.Count; i++)
                Assert.True(claims[i] - claims[i - 1] >= TimeSpan.FromHours(24));
        }

        [Fact]
        public void WaitingMoreThanTwoDaysStartsOver() {
            var s = DailyGiftRules.Evaluate(Start, 5, Start.AddHours(48).AddMinutes(1));
            Assert.True(s.CanClaim);
            Assert.Equal(0, s.Day);
            Assert.Equal(1, DailyGiftRules.StreakAfterClaim(Start, 5, Start.AddHours(48).AddMinutes(1)));
        }

        [Fact]
        public void TheSeventhDayIsTheBiggestAndTheCycleRepeats() {
            Assert.Equal(DailyGiftRules.Days, DailyGiftRules.Cycle.Length);
            var last = DailyGiftRules.Cycle[6];
            for (var i = 0; i < 6; i++) {
                Assert.True(last.Gold > DailyGiftRules.Cycle[i].Gold);
                if (i > 0)
                    Assert.True(DailyGiftRules.Cycle[i].Gold > DailyGiftRules.Cycle[i - 1].Gold, "gold grows through the week");
            }

            Assert.Equal(DailyGiftRules.RewardFor(0), DailyGiftRules.RewardFor(7));
            Assert.Equal(DailyGiftRules.RewardFor(6), DailyGiftRules.RewardFor(13));
        }
    }

    public class Spin {
        [Fact]
        public void ASpinAlsoWaitsAFull24Hours() {
            Assert.Equal(0, DailyCooldown.SecondsLeft(null, Start));
            Assert.True(DailyCooldown.SecondsLeft(Start, Start.AddHours(2.5)) > 0);
            Assert.Equal(0, DailyCooldown.SecondsLeft(Start, Start.AddHours(24)));
        }

        [Fact]
        public void WeightsAreEachAPercentageThatAddsUpTo100() {
            Assert.Equal(100, SpinWheel.TotalWeight);
            Assert.All(SpinWheel.Prizes, p => Assert.True(p.Weight > 0));
        }

        [Fact]
        public void EveryRollLandsOnExactlyOneSegment_InProportionToItsWeight() {
            var hits = new int[SpinWheel.Prizes.Length];
            for (var roll = 0; roll < SpinWheel.TotalWeight; roll++)
                hits[SpinWheel.Pick(roll)]++;

            for (var i = 0; i < hits.Length; i++)
                Assert.Equal(SpinWheel.Prizes[i].Weight, hits[i]);
        }

        [Fact]
        public void OutOfRangeRollsStillGiveARealSegment() {
            Assert.Equal(SpinWheel.Prizes.Length - 1, SpinWheel.Pick(10_000));
            Assert.Equal(0, SpinWheel.Pick(0));
        }

        [Fact]
        public void EveryPrizePaysSomething_AndTheAverageIsModest() {
            double expected = 0;
            foreach (var p in SpinWheel.Prizes) {
                Assert.True(p.Reward.Gold > 0 || p.Reward.Fame > 0);
                expected += p.Weight / 100.0 * (p.Reward.Gold + p.Reward.Fame);
            }

            Assert.InRange(expected, 100, 400);
        }
    }

    public class Inbox {
        [Fact]
        public void CleanTrimsAndCaps() {
            Assert.Equal("hi", InboxRules.Clean("  hi \r\n", InboxRules.MaxSubject));
            Assert.Equal(InboxRules.MaxSubject, InboxRules.Clean(new string('x', 500), InboxRules.MaxSubject).Length);
            Assert.Equal(string.Empty, InboxRules.Clean(null, 10));
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(500, 25, true)]
        [InlineData(-1, 0, false)]
        [InlineData(0, -5, false)]
        [InlineData(2_000_000, 0, false)]
        public void AttachmentsMustBeSane(int gold, int fame, bool ok) => Assert.Equal(ok, InboxRules.ValidAttachment(gold, fame));
    }
}
