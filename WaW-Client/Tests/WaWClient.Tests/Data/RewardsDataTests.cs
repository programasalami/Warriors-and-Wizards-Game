using WaWClient.Data;

namespace WaWClient.Tests.Data;

public class RewardsDataTests {

    public class Inbox {
        [Fact]
        public void ReadsMessagesAndTheirGifts() {
            const string xml = "<Inbox>" +
                               "<Message id=\"7\" sender=\"Owner\" subject=\"Hi\" created=\"1700000100\" read=\"false\" claimed=\"false\" gold=\"300\" fame=\"20\">Here is a gift</Message>" +
                               "<Message id=\"6\" sender=\"Team\" subject=\"Hello\" created=\"1700000000\" read=\"true\" claimed=\"true\" gold=\"500\" fame=\"0\">Welcome</Message>" +
                               "<Message id=\"5\" sender=\"Team\" subject=\"News\" created=\"1699999000\" read=\"true\" claimed=\"false\" gold=\"0\" fame=\"0\">No gift</Message></Inbox>";

            Assert.True(InboxData.TryParse(xml, out var data));
            Assert.Equal(3, data.Messages.Count);

            var gift = data.Messages[0];
            Assert.Equal(7, gift.Id);
            Assert.Equal("Here is a gift", gift.Body);
            Assert.True(gift.CanClaim);
            Assert.Equal(new RewardAmount(300, 20), gift.Gift);

            Assert.False(data.Messages[1].CanClaim);     // already claimed
            Assert.False(data.Messages[2].HasGift);
            Assert.Equal(1, data.Unread);                // only the first is unread / unclaimed
        }

        [Fact]
        public void AnEmptyInboxIsStillAnInbox() {
            Assert.True(InboxData.TryParse("<Inbox />", out var data));
            Assert.Empty(data.Messages);
            Assert.Equal(0, data.Unread);
        }

        [Fact]
        public void OneDamagedMessageDoesNotHideTheRest() {
            const string xml = "<Inbox><Message id=\"x\" created=\"1\">bad</Message><Message id=\"2\" created=\"5\" gold=\"-9\">ok</Message></Inbox>";
            Assert.True(InboxData.TryParse(xml, out var data));
            Assert.Single(data.Messages);
            Assert.Equal(0, data.Messages[0].Gold);      // negative amounts are never shown as a gift
        }

        [Theory]
        [InlineData("")]
        [InlineData("<Error>Sign in first.</Error>")]
        [InlineData("not xml")]
        [InlineData("<Board />")]
        public void OtherAnswersAreNotAnInbox(string text) => Assert.False(InboxData.TryParse(text, out _));
    }

    public class Daily {
        private static string Week() {
            var s = string.Empty;
            for (var i = 0; i < 7; i++) {
                s += $"<Gift day=\"{i}\" gold=\"{100 * (i + 1)}\" fame=\"{(i == 6 ? 50 : 0)}\"/>";
            }

            return s;
        }

        [Fact]
        public void ReadsTheStatusTheWeekAndTheWheel() {
            var xml = "<Daily giftReady=\"true\" giftDay=\"2\" streak=\"2\" spinReady=\"false\" unread=\"3\" secondsToGift=\"4000\" secondsToSpin=\"9000\">" + Week() +
                      "<Prize gold=\"100\" fame=\"0\"/><Prize gold=\"0\" fame=\"25\"/><Prize gold=\"500\" fame=\"0\"/></Daily>";

            Assert.True(DailyData.TryParse(xml, out var d));
            Assert.True(d.GiftReady);
            Assert.False(d.SpinReady);
            Assert.Equal(2, d.GiftDay);
            Assert.Equal(2, d.Streak);
            Assert.Equal(3, d.Unread);
            Assert.Equal(4000, d.SecondsToGift);
            Assert.Equal(9000, d.SecondsToSpin);
            Assert.Equal(7, d.Gifts.Count);
            Assert.Equal(new RewardAmount(700, 50), d.Gifts[6]);
            Assert.Equal(3, d.Prizes.Count);
            Assert.Equal(new RewardAmount(0, 25), d.Prizes[1]);
        }

        [Fact]
        public void AnOlderServersSharedTimerCountsForBoth() {
            var xml = "<Daily giftReady=\"false\" giftDay=\"1\" streak=\"1\" spinReady=\"false\" secondsToReset=\"7200\">" + Week() + "<Prize gold=\"1\" fame=\"0\"/></Daily>";
            Assert.True(DailyData.TryParse(xml, out var d));
            Assert.Equal(7200, d.SecondsToGift);
            Assert.Equal(7200, d.SecondsToSpin);
        }

        [Fact]
        public void TheDayIsKeptInsideTheWeek() {
            var xml = "<Daily giftReady=\"true\" giftDay=\"99\" streak=\"1\" spinReady=\"true\">" + Week() + "<Prize gold=\"1\" fame=\"0\"/></Daily>";
            Assert.True(DailyData.TryParse(xml, out var d));
            Assert.Equal(6, d.GiftDay);
        }

        [Theory]
        [InlineData("")]
        [InlineData("<Error>Sign in first.</Error>")]
        [InlineData("<Daily giftReady=\"true\"/>")]        // no week / wheel: nothing to draw
        public void WithoutTheWeekAndTheWheelItIsNotUsable(string text) => Assert.False(DailyData.TryParse(text, out _));

        [Theory]
        [InlineData(0, "1m")]
        [InlineData(59, "1m")]
        [InlineData(600, "10m")]
        [InlineData(3600, "1h 00m")]
        [InlineData(5 * 3600 + 12 * 60, "5h 12m")]
        [InlineData(-5, "1m")]
        public void CountdownIsShort(int seconds, string expected) => Assert.Equal(expected, DailyData.Countdown(seconds));
    }

    public class Results {
        [Fact]
        public void ReadsAClaim() {
            Assert.True(RewardResult.TryParse("<Claimed gold=\"300\" fame=\"20\" balanceGold=\"1300\" balanceFame=\"70\"/>", out var r));
            Assert.Equal(new RewardAmount(300, 20), r.Paid);
            Assert.Equal((1300, 70), (r.BalanceGold, r.BalanceFame));
            Assert.Equal(-1, r.SegmentIndex);
        }

        [Fact]
        public void ReadsADailyGift() {
            Assert.True(RewardResult.TryParse("<Gift day=\"3\" streak=\"4\" gold=\"300\" fame=\"0\" balanceGold=\"5\" balanceFame=\"6\"/>", out var r));
            Assert.Equal(300, r.Paid.Gold);
        }

        [Fact]
        public void ASpinSaysWhichSegmentItLandedOn() {
            Assert.True(RewardResult.TryParse("<Spin index=\"5\" gold=\"1000\" fame=\"0\" balanceGold=\"9\" balanceFame=\"9\"/>", out var r));
            Assert.Equal(5, r.SegmentIndex);
            Assert.Equal(1000, r.Paid.Gold);
        }

        [Theory]
        [InlineData("<Error>You already spun today.</Error>")]
        [InlineData("")]
        [InlineData("<Success />")]
        public void ErrorsAreNotResults(string text) => Assert.False(RewardResult.TryParse(text, out _));
    }
}
