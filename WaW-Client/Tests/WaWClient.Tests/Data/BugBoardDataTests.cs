using WaWClient.Data;

namespace WaWClient.Tests.Data;

public class BugBoardDataTests {

    public class Parsing {
        [Fact]
        public void ReadsPostsAndTheModerationFlag() {
            const string xml = "<Board canModerate=\"true\">" +
                               "<Post id=\"7\" author=\"alice\" status=\"confirmed\" created=\"1700000100\">The tree at 12,4 floats</Post>" +
                               "<Post id=\"6\" author=\"bob\" status=\"new\" created=\"1700000000\">second</Post></Board>";

            Assert.True(BugBoardData.TryParse(xml, out var data));
            Assert.True(data.CanModerate);
            Assert.Equal(2, data.Posts.Count);
            Assert.Equal(new BugPost(7, "alice", "confirmed", 1700000100, "The tree at 12,4 floats"), data.Posts[0]);
            Assert.Equal("bob", data.Posts[1].Author);
        }

        [Fact]
        public void MarkupInAPostStaysLiteralText() {
            const string xml = "<Board canModerate=\"false\"><Post id=\"1\" author=\"x\" status=\"new\" created=\"1\">&lt;b&gt;bold&lt;/b&gt; &amp; more</Post></Board>";
            Assert.True(BugBoardData.TryParse(xml, out var data));
            Assert.Equal("<b>bold</b> & more", data.Posts[0].Message);
            Assert.False(data.CanModerate);
        }

        [Fact]
        public void AnEmptyBoardIsStillABoard() {
            Assert.True(BugBoardData.TryParse("<Board canModerate=\"false\" />", out var data));
            Assert.Empty(data.Posts);
        }

        [Fact]
        public void OneDamagedPostDoesNotHideTheRest() {
            const string xml = "<Board canModerate=\"false\">" +
                               "<Post id=\"abc\" author=\"x\" status=\"new\" created=\"1\">broken id</Post>" +
                               "<Post id=\"2\" author=\"y\" status=\"new\" created=\"nope\">broken time</Post>" +
                               "<Post id=\"3\" author=\"z\" status=\"new\" created=\"5\">fine</Post></Board>";
            Assert.True(BugBoardData.TryParse(xml, out var data));
            Assert.Single(data.Posts);
            Assert.Equal(3, data.Posts[0].Id);
        }

        [Fact]
        public void AMissingStatusMeansNew() {
            Assert.True(BugBoardData.TryParse("<Board><Post id=\"1\" author=\"x\" created=\"1\">t</Post></Board>", out var data));
            Assert.Equal(BugBoardData.StatusNew, data.Posts[0].Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]                                       // an older server answers an unknown path with an empty page
        [InlineData("   ")]
        [InlineData("<Error>Only admins can remove posts.</Error>")]
        [InlineData("<html>Bad Gateway</html>")]
        [InlineData("not xml <")]
        public void AnythingElseIsNotABoard(string? response) {
            Assert.False(BugBoardData.TryParse(response!, out var data));
            Assert.Null(data);
        }
    }

    public class Wording {
        [Theory]
        [InlineData(1000, 1000, "just now")]
        [InlineData(1000, 1059, "just now")]
        [InlineData(1000, 1060, "1 min ago")]
        [InlineData(1000, 1000 + 59 * 60, "59 min ago")]
        [InlineData(1000, 1000 + 3600, "1 h ago")]
        [InlineData(1000, 1000 + 86399, "23 h ago")]
        [InlineData(1000, 1000 + 86400 * 3, "3 d ago")]
        [InlineData(2000, 1000, "just now")]                    // a clock that runs slightly behind must not show negative time
        public void AgoReadsNaturally(long created, long now, string expected) =>
            Assert.Equal(expected, BugBoardData.Ago(created, now));

        [Theory]
        [InlineData("new", "NEW")]
        [InlineData("confirmed", "CONFIRMED")]
        [InlineData("fixed", "FIXED")]
        [InlineData("something-else", "NEW")]
        public void StatusLabels(string status, string label) =>
            Assert.Equal(label, BugBoardData.StatusLabel(status));
    }
}
