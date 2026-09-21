using Common.Database;

namespace Common.Tests;

public class BugBoardRulesTests {

    public class Clean {
        [Theory]
        [InlineData("hello", "hello")]
        [InlineData("  padded  ", "padded")]
        [InlineData("two   spaces", "two spaces")]
        [InlineData("line one\nline two", "line one line two")]
        [InlineData("tab\tseparated\r\nand crlf", "tab separated and crlf")]
        public void KeepsPlainTextTidy(string input, string expected) =>
            Assert.Equal(expected, BugBoardRules.Clean(input));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n\n\t")]
        public void NothingToPostGivesAnEmptyString(string? input) =>
            Assert.Equal(string.Empty, BugBoardRules.Clean(input!));

        [Fact]
        public void ControlAndInvisibleFormattingCharactersAreRemoved() {
            var sneaky = "abc‮def​ghi\u0000jkl";      // right-to-left override, zero-width space, NUL
            Assert.Equal("abcdefghi jkl", BugBoardRules.Clean(sneaky));
        }

        [Fact]
        public void TooLongIsCutToTheLimit() {
            var cleaned = BugBoardRules.Clean(new string('a', 5000));
            Assert.Equal(BugBoardRules.MaxLength, cleaned.Length);
        }

        [Fact]
        public void CuttingNeverLeavesHalfAnEmoji() {
            var text = new string('a', BugBoardRules.MaxLength - 1) + "\U0001F41B" + "tail";   // an emoji is two UTF-16 characters
            var cleaned = BugBoardRules.Clean(text);
            Assert.True(cleaned.Length <= BugBoardRules.MaxLength);
            Assert.False(char.IsHighSurrogate(cleaned[^1]));
        }

        [Fact]
        public void MarkupIsLeftAsPlainTextForTheClientToDrawLiterally() {
            Assert.Equal("<b>bold</b> & \"quoted\"", BugBoardRules.Clean("<b>bold</b> & \"quoted\""));
        }
    }

    public class Rate {
        private const long Now = 1_000_000;

        [Fact]
        public void FirstPostIsAllowed() => Assert.Null(BugBoardRules.CheckRate([], Now));

        [Fact]
        public void PostingAgainTooSoonIsRefusedWithTheWait() {
            var message = BugBoardRules.CheckRate([Now - 5], Now);
            Assert.NotNull(message);
            Assert.Contains("wait 15 more seconds", message);
        }

        [Fact]
        public void TheOneSecondWaitIsSingular() =>
            Assert.Contains("1 more second before", BugBoardRules.CheckRate([Now - (BugBoardRules.CooldownSeconds - 1)], Now));

        [Fact]
        public void AfterTheCooldownAPostIsAllowedAgain() =>
            Assert.Null(BugBoardRules.CheckRate([Now - BugBoardRules.CooldownSeconds], Now));

        [Fact]
        public void TooManyInAnHourIsRefusedEvenWhenSpreadOut() {
            var spread = Enumerable.Range(0, BugBoardRules.MaxPerHour).Select(i => Now - 60 - i * 300L).ToList();
            Assert.NotNull(BugBoardRules.CheckRate(spread, Now));
        }

        [Fact]
        public void JustUnderTheHourlyLimitIsAllowed() {
            var spread = Enumerable.Range(0, BugBoardRules.MaxPerHour - 1).Select(i => Now - 60 - i * 300L).ToList();
            Assert.Null(BugBoardRules.CheckRate(spread, Now));
        }
    }

    public class Status {
        [Theory]
        [InlineData("new", true)]
        [InlineData("confirmed", true)]
        [InlineData("fixed", true)]
        [InlineData("Confirmed", false)]     // exact, lower-case only
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("done; DROP TABLE bug_posts", false)]
        public void OnlyTheThreeKnownStatusesAreAccepted(string? status, bool valid) =>
            Assert.Equal(valid, BugBoardRules.IsValidStatus(status!));
    }
}
