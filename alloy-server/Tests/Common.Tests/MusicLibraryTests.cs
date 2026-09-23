using Common.Music;
using Common.Resources.Config;

namespace Common.Tests;

// The library file the server shuffles (musicConfig.xml, made by Tools/Music/make_music_config.py) must match the audio the clients actually have.
public class MusicLibraryTests {

    private static string FindClientMusicFolder() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
            var candidate = Path.Combine(dir.FullName, "AlloyClient", "AlloyClient", "Content", "Sound", "Music");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }

        return null;      // running outside the repository: nothing to compare with
    }

    [Fact]
    public void TheLibraryLoadsAndIsUsable() {
        var tracks = MusicConfig.Config.Tracks;
        Assert.NotEmpty(tracks);
        Assert.All(tracks, t => Assert.True(t.LengthMs >= MusicDirector.MinTrackLengthMs, $"{t.Id} is too short"));
        Assert.Equal(tracks.Count, tracks.Select(t => t.Id).Distinct().Count());
        _ = new MusicDirector(tracks, 0, new Random(1));      // the same checks the server makes when it starts
    }

    [Fact]
    public void TheMenuMusicIsNotInTheInGameLibrary() {
        Assert.DoesNotContain(MusicConfig.Config.Tracks, t => t.File.StartsWith("Main_Music", StringComparison.OrdinalIgnoreCase)
                                                            || t.File.StartsWith("Menu_Music", StringComparison.OrdinalIgnoreCase)
                                                            || t.File.StartsWith("Realm_", StringComparison.OrdinalIgnoreCase));
    }

    // 2026-09-21: the jukebox plays Dreamtune alone until more songs are chosen for it.
    [Fact]
    public void TheJukeboxLibraryIsDreamtuneForNow() {
        var track = Assert.Single(MusicConfig.Config.Tracks);
        Assert.Equal("Dreamtune.ogg", track.File);
    }

    [Fact]
    public void EveryTrackHasItsAudioFileInTheClientContent() {
        var folder = FindClientMusicFolder();
        if (folder == null) {
            return;
        }

        foreach (var track in MusicConfig.Config.Tracks) {
            Assert.True(File.Exists(Path.Combine(folder, track.File)), $"musicConfig.xml lists '{track.File}' but it is not in Content/Sound/Music");
        }
    }

    [Fact]
    public void EveryAudioFileIsEitherInTheLibraryOrIsMenuMusic() {
        var folder = FindClientMusicFolder();
        if (folder == null) {
            return;
        }

        var listed = MusicConfig.Config.Tracks.Select(t => t.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder).Select(Path.GetFileName)) {
            var menu = file!.StartsWith("Main_Music", StringComparison.OrdinalIgnoreCase) || file.StartsWith("Menu_Music", StringComparison.OrdinalIgnoreCase);
            var world = file.StartsWith("Realm_", StringComparison.OrdinalIgnoreCase);       // a world's own fixed music (the client plays it by itself)
            Assert.True(menu || world || listed.Contains(file), $"'{file}' is in Content/Sound/Music but not in musicConfig.xml: run Tools/Music/make_music_config.py");
        }
    }
}
