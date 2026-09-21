using Alloy.UiLib.Signals;
using AlloyClient.Core;
using AlloyClient.Game.Music;

namespace AlloyClient.Tests.Music;

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

    [Fact]
    public void AStaticMethodListenerIsNeverCalled_WhichIsWhyTheListenerIsAnObject() {
        // Documents the trap in Alloy.UiLib's Signal: this is why InGameMusic uses an instance listener.
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
