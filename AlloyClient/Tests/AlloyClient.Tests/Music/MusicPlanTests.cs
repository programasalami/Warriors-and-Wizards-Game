using AlloyClient.Data;
using AlloyClient.Game.Music;

namespace AlloyClient.Tests.Music;

public class MusicPlanTests {

    private static readonly MusicTrackInfo A = new("a", "a.ogg", "Alpha", 60_000);
    private static readonly MusicTrackInfo B = new("b", "b.ogg", "Bravo", 90_000);
    private static readonly MusicTrackInfo C = new("c", "c.ogg", "Charlie", 120_000);
    private static readonly MusicTrackInfo[] Library = [A, B, C];

    private static MusicNowData State(string current, string next, long position, int serial = 1) =>
        new(serial, null, position, current, next, Library);

    public class Decisions {
        [Fact]
        public void ArrivingInTheGameStartsWhateverTheServerSays() {
            var step = MusicPlan.Decide(State("b", "c", 20_000), 1000, 1000, null);
            Assert.Equal("b", step.PlayId);
            Assert.False(step.Early);
        }

        [Fact]
        public void SomeoneChangingTheSongSwitchesEveryone() {
            var step = MusicPlan.Decide(State("c", "a", 500), 0, 500, "b");
            Assert.Equal("c", step.PlayId);
            Assert.False(step.Early);
        }

        [Fact]
        public void PlayingTheRightSongInTheMiddleDoesNothing() =>
            Assert.Equal(MusicStep.Nothing, MusicPlan.Decide(State("a", "b", 10_000), 0, 5_000, "a"));

        [Fact]
        public void TheNextSongStartsEarlyWhenTheEndIsNear() {
            // 60 s track, 58 s in => 2 s left, inside the 3 s crossfade
            var step = MusicPlan.Decide(State("a", "b", 58_000), 0, 0, "a");
            Assert.Equal("b", step.PlayId);
            Assert.True(step.Early);
            Assert.Equal(2_000 + 700, step.PollInMs);              // ask again just after the server has moved on
        }

        [Fact]
        public void TheTimeSinceTheAnswerCountsToo() {
            // the answer said 40 s in; 18.5 s of our own clock later there are 1.5 s left
            var step = MusicPlan.Decide(State("a", "b", 40_000), 10_000, 28_500, "a");
            Assert.True(step.Early);
            Assert.Equal("b", step.PlayId);
        }

        [Fact]
        public void AnAnswerThatArrivesDuringTheCrossfadeDoesNotJumpBackToTheOldSong() {
            // we already started "b" early; the answer (worked out 1.8 s before the end of "a") still says "a" is current
            var step = MusicPlan.Decide(State("a", "b", 58_200), 0, 0, "b");
            Assert.Equal(MusicStep.Nothing, step);
        }

        [Fact]
        public void ButIfEveryoneWasMovedSomewhereElseItFollows() {
            // playing "b", the answer says "c" is current with plenty of time left: someone changed the music
            var step = MusicPlan.Decide(State("c", "a", 10_000), 0, 0, "b");
            Assert.Equal("c", step.PlayId);
            Assert.False(step.Early);
        }

        [Fact]
        public void JustOutsideTheCrossfadeItWaits() =>
            Assert.Equal(MusicStep.Nothing, MusicPlan.Decide(State("a", "b", 56_900), 0, 0, "a"));   // 3.1 s left

        [Fact]
        public void APlayedOutTrackNeverLeavesTheSpeakersOnIt() {
            // the client was busy (a hitch): the track is already over on the server's timeline; it still moves to the next one at once
            var step = MusicPlan.Decide(State("a", "b", 61_000), 0, 0, "a");
            Assert.Equal("b", step.PlayId);
            Assert.Equal(700, step.PollInMs);
        }

        [Fact]
        public void AOneSongLibraryIsLeftToLoop() {
            var one = new MusicNowData(1, null, 59_000, "a", "a", [A]);
            Assert.Equal(MusicStep.Nothing, MusicPlan.Decide(one, 0, 0, "a"));
        }

        [Fact]
        public void AnUnknownCurrentTrackDoesNothing() =>
            Assert.Equal(MusicStep.Nothing, MusicPlan.Decide(new MusicNowData(1, null, 0, "zzz", "a", Library), 0, 0, "a"));
    }

    public class Simulation {
        // A made-up server: it plays A, B, C, A, B, C ... back to back, and can be told to jump to another song (a skip).
        private sealed class FakeServer {
            public readonly string[] Order = ["a", "b", "c"];
            public long CycleStart;          // when the current cycle position 0 (track a) started
            public int Serial = 1;

            private static long Length(string id) => Library.First(t => t.Id == id).LengthMs;

            public MusicNowData At(long now) {
                var cycle = Length("a") + Length("b") + Length("c");
                var t = (now - CycleStart) % cycle;
                var index = 0;
                while (t >= Length(Order[index])) { t -= Length(Order[index]); index++; }
                return new MusicNowData(Serial, null, t, Order[index], Order[(index + 1) % 3], Library);
            }

            public (string Current, string Next, long Remaining) Playing(long now) {
                var s = At(now);
                return (s.CurrentId, s.NextId, Library.First(x => x.Id == s.CurrentId).LengthMs - s.PositionMs);
            }
        }

        // Runs the client's loop (poll about every 5 s, decide every 100 ms) and reports every moment the speakers were on the wrong song.
        private static List<string> Run(FakeServer server, long durationMs, Action<long> onTick = null) {
            var problems = new List<string>();
            string playing = null;
            MusicNowData state = null;
            long heardAt = 0, nextPoll = 0;

            for (long now = 0; now < durationMs; now += 100) {
                onTick?.Invoke(now);

                if (now >= nextPoll) {
                    state = server.At(now);
                    heardAt = now;
                    nextPoll = now + 5_000;
                }

                var step = MusicPlan.Decide(state, heardAt, now, playing);
                if (step.PlayId != null) {
                    playing = step.PlayId;
                    if (step.PollInMs >= 0) {
                        nextPoll = Math.Min(nextPoll, now + step.PollInMs);
                    }
                }

                // At every moment the speakers must be on the server's current song, or already on the next one inside the crossfade window.
                var (current, next, remaining) = server.Playing(now);
                var acceptable = playing == current || (playing == next && remaining <= MusicPlan.CrossfadeMs + 700);
                if (now > 0 && !acceptable) {
                    problems.Add($"t={now}: playing {playing}, server current {current} (next {next}, {remaining} ms left)");
                }
            }

            return problems;
        }

        [Fact]
        public void HoursOfPlayNeverLeaveTheSpeakersOnTheWrongSongOrSilent() {
            var problems = Run(new FakeServer { CycleStart = 12_345 }, 3L * 3600 * 1000);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(5)));
        }

        [Fact]
        public void ASkipByAnotherPlayerIsFollowedWithinAPollAndThenItKeepsWorking() {
            var server = new FakeServer();
            const long skipAt = 100_000;
            var problems = Run(server, 30 * 60 * 1000, now => {
                if (now == skipAt) {
                    server.CycleStart -= 45_000;       // jump 45 s ahead in the playlist
                    server.Serial++;
                }
            });

            // only the few seconds until the next poll may be out of step; everything after must be right
            var late = problems.Where(p => long.Parse(p.Split(':')[0][2..]) > skipAt + 5_500).ToList();
            Assert.True(late.Count == 0, string.Join("\n", late.Take(5)));
        }
    }
}
