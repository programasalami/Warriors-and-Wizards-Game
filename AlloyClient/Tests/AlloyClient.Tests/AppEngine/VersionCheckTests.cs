using AlloyClient.AppEngine;
using AlloyClient.Utils;

namespace AlloyClient.Tests.AppEngine;

public class VersionCheckTests {

    public class Comparison {
        [Theory]
        [InlineData("0.3.3", "0.3.3", false)]   // same build
        [InlineData("0.3.3", "0.3.4", true)]    // server moved on
        [InlineData("0.3.4", "0.3.3", true)]    // server moved back - the game server rejects any difference, so this does too
        [InlineData("0.3.3", "0.3.3 ", true)]   // exact match only, same as GameServer's Hello check (parse trims, so this never happens in practice)
        public void OutdatedMeansDifferent(string client, string server, bool outdated) =>
            Assert.Equal(outdated, VersionCheck.IsOutdatedFor(client, server));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UnknownServerVersionIsNotAMismatch(string? server) =>
            Assert.False(VersionCheck.IsOutdatedFor("0.3.3", server!));
    }

    public class NewerThanServer {
        [Theory]
        [InlineData("0.3.4", "0.3.3", true)]     // a new build went out before the server was updated
        [InlineData("0.4.0", "0.3.9", true)]
        [InlineData("0.10.0", "0.9.0", true)]    // numeric, not alphabetical
        [InlineData("0.3.3", "0.3.4", false)]    // the client is the old one: that is the normal "update required"
        [InlineData("0.3.3", "0.3.3", false)]
        [InlineData("0.3.3", "banana", false)]   // unparseable never counts as newer
        [InlineData("0.3.3", "", false)]
        public void OnlyANumericallyHigherClientIsNewer(string client, string server, bool newer) =>
            Assert.Equal(newer, VersionCheck.IsClientNewerFor(client, server));

        [Fact]
        public void TheMessageSaysTheServerHasToCatchUpNotThatTheClientIsOld() {
            var message = VersionCheck.BuildNewerThanServerMessage("0.3.4", "0.3.3");
            Assert.Contains("newer than the server", message);
            Assert.Contains("0.3.4", message);
            Assert.Contains("0.3.3", message);
            Assert.DoesNotContain("out of date", message);
        }
    }

    public class Parsing {
        [Fact]
        public void ReadsVersionAndDownloadUrl() {
            Assert.True(VersionCheck.TryParse("<Version downloadUrl=\"https://example.com/game.zip\">0.4.0</Version>", out var version, out var url));
            Assert.Equal("0.4.0", version);
            Assert.Equal("https://example.com/game.zip", url);
        }

        [Fact]
        public void DownloadUrlIsOptional() {
            Assert.True(VersionCheck.TryParse("<Version>0.4.0</Version>", out var version, out var url));
            Assert.Equal("0.4.0", version);
            Assert.Null(url);
        }

        [Fact]
        public void TrimsWhitespaceAroundTheVersion() {
            Assert.True(VersionCheck.TryParse("<Version>\n  0.4.0 \n</Version>", out var version, out _));
            Assert.Equal("0.4.0", version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]                                  // an older server closes unknown requests with an empty body
        [InlineData("   ")]
        [InlineData("<Error>nope</Error>")]
        [InlineData("<Version/>")]
        [InlineData("<Version>   </Version>")]
        [InlineData("not xml at all <")]
        public void AnythingElseIsUnknownRatherThanAnError(string? response) {
            Assert.False(VersionCheck.TryParse(response!, out var version, out var url));
            Assert.Null(version);
            Assert.Null(url);
        }
    }

    public class Messages {
        [Fact]
        public void WebMessageNamesBothVersionsAndSuggestsARefresh() {
            var message = VersionCheck.BuildMessage("0.3.3", "0.3.4", isWeb: true, canDownload: true);
            Assert.Contains("web version", message);
            Assert.Contains("0.3.3", message);
            Assert.Contains("0.3.4", message);
            Assert.Contains("refresh", message);
            Assert.Contains("download the newest desktop client", message);
        }

        [Fact]
        public void DesktopMessageDoesNotMentionTheBrowser() {
            var message = VersionCheck.BuildMessage("0.3.3", "0.3.4", isWeb: false, canDownload: true);
            Assert.Contains("Your client is out of date", message);
            Assert.DoesNotContain("refresh", message);
        }

        [Fact]
        public void WithoutADownloadLinkTheMessageDoesNotPromiseADownloadButton() {
            var message = VersionCheck.BuildMessage("0.3.3", "0.3.4", isWeb: false, canDownload: false);
            Assert.DoesNotContain("download", message);
        }
    }

    public class Urls {
        [Theory]
        [InlineData("https://example.com/a.zip", true)]
        [InlineData("http://example.com/a.zip", true)]
        [InlineData("file:///C:/Windows/System32/calc.exe", false)]   // the address comes from the server's config: only web links are ever opened
        [InlineData("calc.exe", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyWebLinksAreOpened(string? url, bool allowed) =>
            Assert.Equal(allowed, ClientPlatform.IsHttpUrl(url!));
    }
}
