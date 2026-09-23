using Common.Database;
using GameServer.Game.Systems.Chat.Commands;

namespace GameServer.Tests.Commands;

public class CommandsTests {

    public class Permissions {
        [Theory]
        [InlineData(Ranks.Player, CommandPermissionLevel.Player, true)]
        [InlineData(Ranks.Player, CommandPermissionLevel.Moderator, false)]   // used to be open to everyone: the bug
        [InlineData(Ranks.Player, CommandPermissionLevel.Owner, false)]
        [InlineData(Ranks.Moderator, CommandPermissionLevel.Player, true)]
        [InlineData(Ranks.Moderator, CommandPermissionLevel.Moderator, true)]
        [InlineData(Ranks.Moderator, CommandPermissionLevel.Owner, false)]
        [InlineData(Ranks.Owner, CommandPermissionLevel.Player, true)]
        [InlineData(Ranks.Owner, CommandPermissionLevel.Moderator, true)]
        [InlineData(Ranks.Owner, CommandPermissionLevel.Owner, true)]
        public void ARankReachesOnlyCommandsAtOrBelowIt(int rank, CommandPermissionLevel level, bool allowed) {
            Assert.Equal(allowed, CommandManager.IsAllowed(rank, level));
        }

        [Fact]
        public void OwnerIsTheSameLevelAsAdmin() => Assert.Equal(CommandPermissionLevel.Admin, CommandPermissionLevel.Owner);

        [Fact]
        public void EveryCommandDeclaresTheRankItNeeds() {
            CommandManager.Load();

            string[] moderator = ["find", "kick", "ban", "unban", "mute", "unmute"];
            string[] owner = ["give", "setrank", "mail", "spawn", "reloadbehaviors"];
            string[] player = ["commands", "online", "rank"];

            var playerList = CommandManager.GetCommandList(Ranks.Player).ToList();
            var modList = CommandManager.GetCommandList(Ranks.Moderator).ToList();
            var ownerList = CommandManager.GetCommandList(Ranks.Owner).ToList();

            foreach (var c in player) {
                Assert.Contains("/" + c, playerList);
            }

            foreach (var c in moderator) {
                Assert.DoesNotContain("/" + c, playerList);
                Assert.Contains("/" + c, modList);
            }

            foreach (var c in owner) {
                Assert.DoesNotContain("/" + c, playerList);
                Assert.DoesNotContain("/" + c, modList);
                Assert.Contains("/" + c, ownerList);
            }
        }
    }

    public class Moderation {
        private const string Usage = "usage";

        [Fact]
        public void ParsesPlayerDurationAndReason() {
            Assert.True(CommandArgs.TryModeration("bob 7d being rude to people", Usage, out var target, out var duration, out var reason, out var error));
            Assert.Equal("bob", target);
            Assert.Equal(TimeSpan.FromDays(7), duration);
            Assert.Equal("being rude to people", reason);
            Assert.Null(error);
        }

        [Fact]
        public void ReasonIsOptional_AndPermanentHasNoDuration() {
            Assert.True(CommandArgs.TryModeration("bob perm", Usage, out var target, out var duration, out var reason, out _));
            Assert.Equal("bob", target);
            Assert.Null(duration);
            Assert.Equal(string.Empty, reason);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("bob")]
        public void TooFewPartsShowsTheUsage(string args) {
            Assert.False(CommandArgs.TryModeration(args, Usage, out _, out _, out _, out var error));
            Assert.Equal(Usage, error);
        }

        [Fact]
        public void ABadDurationSaysWhatIsAllowed() {
            Assert.False(CommandArgs.TryModeration("bob soon", Usage, out _, out _, out _, out var error));
            Assert.Contains("not a duration", error);
        }
    }

    public class Give {
        [Theory]
        [InlineData("iron sword", 1, "iron sword")]
        [InlineData("3 iron sword", 3, "iron sword")]
        [InlineData("  8   Gold Staff ", 8, "Gold Staff")]
        [InlineData("sword", 1, "sword")]
        public void ParsesAnOptionalCountThenTheName(string args, int count, string query) {
            Assert.True(CommandArgs.TryGive(args, out var c, out var q, out _));
            Assert.Equal(count, c);
            Assert.Equal(query, q);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("3")]
        [InlineData("0 sword")]
        [InlineData("99 sword")]
        public void RejectsMissingNamesAndCrazyCounts(string args) {
            Assert.False(CommandArgs.TryGive(args, out _, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }
    }

    public class Other {
        [Fact]
        public void ParsesMail() {
            Assert.True(CommandArgs.TryMail("bob 500 25 hello there, welcome!", out var target, out var gold, out var fame, out var message, out _));
            Assert.Equal(("bob", 500, 25, "hello there, welcome!"), (target, gold, fame, message));
        }

        [Theory]
        [InlineData("bob 500")]
        [InlineData("bob x y hi")]
        [InlineData("bob -5 0 hi")]
        [InlineData("bob 9999999 0 hi")]
        public void RejectsBadMail(string args) => Assert.False(CommandArgs.TryMail(args, out _, out _, out _, out _, out var error) || error == null);

        [Fact]
        public void ParsesSetRank() {
            Assert.True(CommandArgs.TrySetRank("bob moderator", out var target, out var rank, out _));
            Assert.Equal("bob", target);
            Assert.Equal(Ranks.Moderator, rank);
            Assert.False(CommandArgs.TrySetRank("bob king", out _, out _, out _));
            Assert.False(CommandArgs.TrySetRank("bob", out _, out _, out _));
        }
    }

    public class ItemSearchTests {
        private static readonly List<ItemSearch.Entry> Items = [
            new(0xa00, "Old Sword"), new(0xa01, "Iron Sword"), new(0xa02, "Steel Sword"), new(0xa03, "Bronze Sword"), new(0xa04, "Gold Sword"),
            new(0xa97, "Old Staff"), new(0xa98, "Iron Staff"), new(0xa22, "Health Potion"), new(0xa2e, "Old Spell"), new(0xa66, "Broken Helmet")
        ];

        [Fact]
        public void AnExactNameIsTheClearWinner_EvenWhenOthersContainIt() {
            var hits = ItemSearch.Find(Items, "iron sword");
            Assert.Equal(0xa01, hits[0].Type);
            Assert.True(ItemSearch.IsClearWinner(hits, "iron sword"));
        }

        [Fact]
        public void IgnoresCaseSpacesAndDashes() {
            Assert.Equal(0xa01, ItemSearch.Find(Items, "IRONSWORD")[0].Type);
            Assert.Equal(0xa01, ItemSearch.Find(Items, "iron-sword")[0].Type);
        }

        [Fact]
        public void PartOfANameFindsEveryItemThatHasIt_ShortestFirst() {
            var hits = ItemSearch.Find(Items, "sword");
            Assert.Equal(5, hits.Count);
            Assert.All(hits, h => Assert.Contains("Sword", h.Name));
            Assert.False(ItemSearch.IsClearWinner(hits, "sword"), "several swords: the caller must ask for a more exact name");
        }

        [Fact]
        public void APrefixBeatsAContains() {
            var hits = ItemSearch.Find(Items, "s");
            Assert.StartsWith("S", hits[0].Name);         // Steel Sword before names that only contain an s
        }

        [Fact]
        public void WordsInAnyOrderStillMatch() {
            var hits = ItemSearch.Find(Items, "staff iron");
            Assert.Single(hits);
            Assert.Equal(0xa98, hits[0].Type);
            Assert.True(ItemSearch.IsClearWinner(hits, "staff iron"));
        }

        [Fact]
        public void NothingMatchesNothing() {
            Assert.Empty(ItemSearch.Find(Items, "banana"));
            Assert.Empty(ItemSearch.Find(Items, "   "));
            Assert.Empty(ItemSearch.Find(Items, null));
        }
    }
}
