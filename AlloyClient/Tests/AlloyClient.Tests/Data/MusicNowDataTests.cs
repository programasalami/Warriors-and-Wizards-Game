using AlloyClient.Data;

namespace AlloyClient.Tests.Data;

public class MusicNowDataTests {

    private const string Good = "<Music serial=\"4\" changedBy=\"Riigged\" positionMs=\"12500\" current=\"b\" next=\"a\">" +
                                "<Track id=\"a\" file=\"a.ogg\" title=\"Alpha\" lengthMs=\"60000\" />" +
                                "<Track id=\"b\" file=\"b.mp3\" title=\"Bravo\" lengthMs=\"90000\" /></Music>";

    [Fact]
    public void ReadsTheLibraryAndWhatIsPlaying() {
        Assert.True(MusicNowData.TryParse(Good, out var data));
        Assert.Equal(4, data.Serial);
        Assert.Equal("Riigged", data.ChangedBy);
        Assert.Equal(12_500, data.PositionMs);
        Assert.Equal("b", data.CurrentId);
        Assert.Equal("a", data.NextId);
        Assert.Equal(2, data.Tracks.Count);
        Assert.Equal(new MusicTrackInfo("b", "b.mp3", "Bravo", 90_000), data.Find("b"));
        Assert.Null(data.Find("nope"));
    }

    [Fact]
    public void NobodyHavingChangedItMeansTheShuffleDidIt() {
        var xml = Good.Replace("changedBy=\"Riigged\"", "changedBy=\"\"");
        Assert.True(MusicNowData.TryParse(xml, out var data));
        Assert.Null(data.ChangedBy);
    }

    [Fact]
    public void AnUnknownNextTrackFallsBackToTheCurrentOne() {
        Assert.True(MusicNowData.TryParse(Good.Replace("next=\"a\"", "next=\"zzz\""), out var data));
        Assert.Equal("b", data.NextId);
    }

    [Fact]
    public void DamagedTracksAreSkippedButTheRestStay() {
        var xml = Good.Replace("</Music>", "<Track id=\"c\" file=\"c.ogg\" title=\"Broken\" lengthMs=\"abc\" /><Track id=\"\" file=\"d.ogg\" lengthMs=\"5\" /></Music>");
        Assert.True(MusicNowData.TryParse(xml, out var data));
        Assert.Equal(2, data.Tracks.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]                                                // an older server answers an unknown path with an empty page
    [InlineData("<Error>The music library is not set up on this server.</Error>")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("not xml <")]
    [InlineData("<Music serial=\"1\" positionMs=\"0\" current=\"a\" next=\"a\" />")]                                                   // no tracks
    [InlineData("<Music serial=\"1\" positionMs=\"0\" current=\"zzz\" next=\"a\"><Track id=\"a\" file=\"a.ogg\" lengthMs=\"5000\" /></Music>")]   // current not in the list
    public void AnythingElseIsNoMusicInfo(string? response) {
        Assert.False(MusicNowData.TryParse(response!, out var data));
        Assert.Null(data);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(999, "0:00")]
    [InlineData(59_000, "0:59")]
    [InlineData(60_000, "1:00")]
    [InlineData(207_552, "3:27")]
    [InlineData(-5, "0:00")]
    public void TimesReadLikeAMusicPlayer(long ms, string expected) => Assert.Equal(expected, MusicNowData.FormatTime(ms));
}
