using Common.Music;

namespace Common.Tests;

public class MusicDirectorTests {

    private static readonly MusicTrack A = new("a", "a.ogg", "Alpha", 60_000);
    private static readonly MusicTrack B = new("b", "b.ogg", "Bravo", 90_000);
    private static readonly MusicTrack C = new("c", "c.ogg", "Charlie", 120_000);
    private static readonly MusicTrack D = new("d", "d.ogg", "Delta", 30_000);

    private static MusicDirector Make(int seed = 1, params MusicTrack[] tracks) =>
        new(tracks.Length == 0 ? [A, B, C, D] : tracks, 0, new Random(seed));

    public class Construction {
        [Fact] public void EmptyLibraryIsRefused() => Assert.Throws<ArgumentException>(() => new MusicDirector([], 0, new Random(1)));

        [Fact] public void TooShortATrackIsRefused() => Assert.Throws<ArgumentException>(() => new MusicDirector([new MusicTrack("x", "x.ogg", "X", 500)], 0, new Random(1)));

        [Fact] public void DuplicateIdsAreRefused() => Assert.Throws<ArgumentException>(() => new MusicDirector([A, A], 0, new Random(1)));

        [Fact]
        public void StartsWithSomethingPlayingFromTheStart() {
            var snap = Make().Snapshot(0);
            Assert.Equal(0, snap.PositionMs);
            Assert.NotNull(snap.Current);
            Assert.NotEqual(snap.Current.Id, snap.Next.Id);
        }

        [Fact]
        public void TheSameSeedGivesTheSameOrder() {
            string Order(int seed) {
                var d = Make(seed);
                var ids = new List<string>();
                for (long t = 0; t < 2_000_000; t += 5_000) {
                    var id = d.Snapshot(t).Current.Id;
                    if (ids.Count == 0 || ids[^1] != id) ids.Add(id);
                }
                return string.Join(",", ids);
            }

            Assert.Equal(Order(7), Order(7));
        }
    }

    public class Shuffle {
        [Fact]
        public void NothingEverPlaysTwiceInARow() {
            var d = Make(3);
            string previous = null;
            var changes = 0;
            for (long t = 0; t < 5_000_000; t += 1000) {          // about 1.4 hours, watched every second
                var id = d.Snapshot(t).Current.Id;
                if (id != previous) {
                    Assert.NotEqual(previous, id);
                    previous = id;
                    changes++;
                }
            }

            Assert.True(changes > 30, $"only {changes} tracks played");
        }

        [Fact]
        public void EveryTrackKeepsGettingItsTurn() {
            var d = Make(11);
            var plays = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["c"] = 0, ["d"] = 0 };
            string previous = null;
            for (long t = 0; t < 20_000_000; t += 1000) {
                var id = d.Snapshot(t).Current.Id;
                if (id != previous) { plays[id]++; previous = id; }
            }

            Assert.All(plays.Values, count => Assert.True(count > 20, string.Join(",", plays)));
        }

        [Fact]
        public void ThePredictedNextTrackIsTheOneThatActuallyFollows() {
            var d = Make(5);
            for (var i = 0; i < 50; i++) {
                var before = d.Snapshot(i * 500_000L);
                var after = d.Snapshot(i * 500_000L + 200_000);        // longer than any track: at least one has passed
                if (after.Serial == before.Serial + 1) {
                    Assert.Equal(before.Next.Id, after.Current.Id);
                }
            }
        }

        [Fact]
        public void ASingleTrackLibraryJustRepeats() {
            var d = new MusicDirector([A], 0, new Random(1));
            Assert.Equal("a", d.Snapshot(0).Current.Id);
            Assert.Equal("a", d.Snapshot(61_000).Current.Id);
            Assert.Equal("a", d.Snapshot(61_000).Next.Id);
        }
    }

    public class Timeline {
        [Fact]
        public void PositionGrowsWithTheClockAndResetsWhenTheTrackChanges() {
            var d = Make(2);
            var first = d.Snapshot(0);
            var later = d.Snapshot(10_000);
            Assert.Equal(first.Current.Id, later.Current.Id);
            Assert.Equal(10_000, later.PositionMs);
            Assert.Equal(first.Serial, later.Serial);

            var afterTheEnd = d.Snapshot(first.Current.LengthMs + 3_000);
            Assert.NotEqual(first.Current.Id, afterTheEnd.Current.Id);
            Assert.Equal(3_000, afterTheEnd.PositionMs);          // the next track started exactly when the last ended
            Assert.Equal(first.Serial + 1, afterTheEnd.Serial);
            Assert.Null(afterTheEnd.ChangedBy);                   // the shuffle did it
        }

        [Fact]
        public void PositionIsNeverPastTheEndOfTheTrack() {
            var d = Make(9);
            for (long t = 0; t < 3_000_000; t += 777) {
                var snap = d.Snapshot(t);
                Assert.InRange(snap.PositionMs, 0, snap.Current.LengthMs - 1);
            }
        }

        [Fact]
        public void LongSilenceCatchesUpToTheSameAnswerAsWatchingTheWholeTime() {
            var watched = Make(4);
            for (long t = 0; t < 50_000_000; t += 10_000) watched.Snapshot(t);
            var skipped = Make(4);
            var a = watched.Snapshot(50_000_000);
            var b = skipped.Snapshot(50_000_000);
            Assert.Equal(a.Current.Id, b.Current.Id);
            Assert.Equal(a.PositionMs, b.PositionMs);
        }

        [Fact]
        public void AClockThatGoesBackwardsNeverGivesANegativePosition() {
            var d = Make(1);
            d.Snapshot(100_000);
            Assert.True(d.Snapshot(50_000).PositionMs >= 0);
        }
    }

    public class SkippingAndPicking {
        [Fact]
        public void NextMovesOnAndSaysWhoDidIt() {
            var d = Make(6);
            var before = d.Snapshot(5_000);
            d.Skip(5_000, forward: true, by: "Riigged");
            var after = d.Snapshot(5_000);
            Assert.Equal(before.Next.Id, after.Current.Id);       // it plays what was announced as next
            Assert.Equal(0, after.PositionMs);
            Assert.Equal("Riigged", after.ChangedBy);
            Assert.Equal(before.Serial + 1, after.Serial);
        }

        [Fact]
        public void PreviousGoesBackAndNextComesBackToWhereWeWere() {
            var d = Make(6);
            var first = d.Snapshot(0).Current.Id;
            d.Skip(1_000, true, "x");
            var second = d.Snapshot(1_000).Current.Id;

            d.Skip(2_000, false, "y");
            var back = d.Snapshot(2_000);
            Assert.Equal(first, back.Current.Id);
            Assert.Equal(second, back.Next.Id);                   // forward again returns to the track we left
            Assert.Equal("y", back.ChangedBy);
        }

        [Fact]
        public void PreviousWithNothingBeforeItRestartsTheTrack() {
            var d = Make(6);
            var start = d.Snapshot(0);
            d.Skip(20_000, false, "y");
            var again = d.Snapshot(20_000);
            Assert.Equal(start.Current.Id, again.Current.Id);
            Assert.Equal(0, again.PositionMs);
            Assert.True(again.Serial > start.Serial);             // clients restart it
        }

        [Fact]
        public void PickingATrackPlaysItAtOnce() {
            var d = Make(8);
            var target = d.Snapshot(0).Next.Id;
            Assert.True(d.Set(3_000, target, "Kel"));
            var snap = d.Snapshot(3_000);
            Assert.Equal(target, snap.Current.Id);
            Assert.Equal(0, snap.PositionMs);
            Assert.Equal("Kel", snap.ChangedBy);
            Assert.NotEqual(target, snap.Next.Id);                // just played, so not due again straight away
        }

        [Fact]
        public void PickingAnUnknownTrackIsRefusedAndChangesNothing() {
            var d = Make(8);
            var before = d.Snapshot(1_000);
            Assert.False(d.Set(1_000, "nope", "Kel"));
            var after = d.Snapshot(1_000);
            Assert.Equal(before.Current.Id, after.Current.Id);
            Assert.Equal(before.Serial, after.Serial);
        }

        [Fact]
        public void PickingWhatIsAlreadyPlayingDoesNotRestartIt() {
            var d = Make(8);
            var now = d.Snapshot(0);
            Assert.True(d.Set(7_000, now.Current.Id, "Kel"));
            var after = d.Snapshot(7_000);
            Assert.Equal(7_000, after.PositionMs);
            Assert.Equal(now.Serial, after.Serial);
        }

        [Fact]
        public void ManySkipsNeverLeaveTheBagEmpty() {
            var d = Make(12);
            for (var i = 0; i < 500; i++) {
                d.Skip(i * 100L, forward: i % 5 != 0, by: "spam");
                Assert.NotNull(d.Snapshot(i * 100L).Next);
            }
        }
    }

    public class Cooldowns {
        private const long Never = long.MinValue;

        [Fact] public void FirstChangeIsAllowed() => Assert.Null(MusicRules.CheckCooldown(50_000, Never, Never));

        [Fact]
        public void AnAccountMustWaitBetweenChanges() {
            var message = MusicRules.CheckCooldown(50_000, 45_000, Never);
            Assert.NotNull(message);
            Assert.Contains("5 more seconds", message);
        }

        [Fact] public void TheLastSecondIsSingular() => Assert.Contains("1 more second before", MusicRules.CheckCooldown(50_000, 40_500, Never));

        [Fact] public void AfterTheCooldownItIsAllowedAgain() => Assert.Null(MusicRules.CheckCooldown(50_000, 50_000 - MusicRules.AccountCooldownMs, Never));

        [Fact] public void EveryoneTogetherIsAlsoLimited() => Assert.NotNull(MusicRules.CheckCooldown(50_000, Never, 49_000));

        [Fact] public void AnotherPlayersChangeOlderThanTheGlobalLimitIsFine() => Assert.Null(MusicRules.CheckCooldown(50_000, Never, 50_000 - MusicRules.GlobalCooldownMs));

        [Fact] public void ItIsFreeForNow() => Assert.Equal(0, MusicRules.SkipFameCost + MusicRules.PickFameCost);
    }
}
