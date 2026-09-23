using WaW.UiLib.Signals;
using WaWClient.Core;
using WaWClient.Game.Music;

namespace WaWClient.Tests.Music;

public class InGameMusicTests {

    [Fact]
    public void EnteringTheGameSwitchesTheMusicOn_EvenAfterGarbageCollection() {
        // Regression: the signal holds listeners by a weak reference to the delegate's target, so a STATIC listener was dropped on the first dispatch and the
        // Nexus kept playing the menu music. The listener must survive a collection and still fire, every time.
        var signal = new Signal<ScreenType>();
        InGameMusic.Init(signal);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        signal.Dispatch(ScreenType.Game);
        Assert.True(InGameMusic.IsInGame, "the game screen never reached the music");

        signal.Dispatch(ScreenType.Menu);
        Assert.False(InGameMusic.IsInGame);

        GC.Collect();
        signal.Dispatch(ScreenType.Game);
        Assert.True(InGameMusic.IsInGame, "the listener was lost after the first use");
        signal.Dispatch(ScreenType.Menu);
    }

    // 2026-09-21: the Realm has its own fixed track, the shared rooms follow the jukebox, the menus play the client's own choice - and a world switch
    // between them changes the music exactly once.
    [Fact]
    public void TheRealmPlaysItsOwnTrackAndTheNexusFollowsTheJukebox() {
        var played = new List<string>();
        var previous = InGameMusic.PlayFile;
        InGameMusic.PlayFile = (file, _) => played.Add(file);
        try {
            var signal = new Signal<ScreenType>();
            InGameMusic.Init(signal);
            signal.Dispatch(ScreenType.Game);
            Assert.Equal(InGameMusic.MusicMode.Playlist, InGameMusic.Mode);

            InGameMusic.OnWorld("Realm");
            Assert.Equal(InGameMusic.MusicMode.Fixed, InGameMusic.Mode);
            Assert.Equal([InGameMusic.RealmTrack], played);

            InGameMusic.OnWorld("Realm");                       // realm to realm: nothing restarts
            Assert.Single(played);

            InGameMusic.OnWorld("Nexus");                       // back to the shared room: the playlist takes over (the server is asked, nothing plays yet)
            Assert.Equal(InGameMusic.MusicMode.Playlist, InGameMusic.Mode);
            Assert.Null(InGameMusic.FixedTrack);
            Assert.Single(played);

            InGameMusic.OnWorld("Realm");
            signal.Dispatch(ScreenType.Menu);                   // leaving the game: back to the menu track
            Assert.Equal(InGameMusic.MusicMode.Menu, InGameMusic.Mode);
            Assert.Equal(InGameMusic.MenuTrack, played[^1]);
            Assert.Equal("Music/Main_Music.wav", InGameMusic.MenuTrack);
        } finally {
            InGameMusic.PlayFile = previous;
        }
    }

    [Fact]
    public void AStaticMethodListenerIsNeverCalled_WhichIsWhyTheListenerIsAnObject() {
        // Documents the trap in WaW.UiLib's Signal: this is why InGameMusic uses an instance listener.
        var signal = new Signal<int>();
        StaticSink.Calls = 0;
        signal.Add(StaticSink.Handle);
        signal.Dispatch(1);
        Assert.Equal(0, StaticSink.Calls);
    }

    private static class StaticSink {
        public static int Calls;
        public static void Handle(int _) => Calls++;
    }
}
