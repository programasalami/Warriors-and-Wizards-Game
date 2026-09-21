using System;
using System.Collections.Concurrent;
using AlloyClient.AppEngine;
using AlloyClient.Data;

namespace AlloyClient.Game.Music;

// The music while you are IN the game (the Nexus, the Realm, the Vault ...): one shuffled playlist that the account server keeps for everybody, so all players
// hear the same song. The menus keep their own music (Main_Music, as before): entering the game blends from it into the playlist, leaving blends back.
//
// The client only follows the server: it asks now and then what is playing (and again right after anyone changes it), starts the current song, and starts the
// next one a few seconds before the current ends so the two overlap smoothly (see MusicPlan). If the server has no music (an older server) or cannot be
// reached, nothing changes and the menu music simply keeps playing.
public static class InGameMusic {

    // The title-screen / loading track. Not part of the in-game library.
    public const string MenuTrack = "Music/Main_Music.wav";

    private const long PollMs = 5_000;
    private const long RetryMs = 20_000;

    private static readonly ConcurrentQueue<AppRequests.MusicNowResult> Results = new();

    private static bool _inGame;
    private static bool _polling;
    private static long _nextPollAt;
    private static string _playingId;
    private static long _heardAt;

    // What the server last said (null until the first answer after entering the game), for the Jukebox window.
    public static MusicNowData State { get; private set; }

    public static MusicTrackInfo Playing => State?.Find(_playingId ?? State.CurrentId);

    // How far into the CURRENT song the server is right now, by our own clock.
    public static long PositionMs => State == null ? 0 : State.PositionMs + (Environment.TickCount64 - _heardAt);

    // The screen-change signal keeps its listeners by a WEAK reference to the delegate's target, and a static method has no target - so a static
    // listener is dropped the first time the signal fires (this is what left the Nexus on the menu music and the Jukebox on "Loading"). The listener
    // therefore has to be an object, and this field keeps it alive.
    private sealed class ScreenListener {
        public void OnChange(ScreenType type) => OnScreenChange(type);
    }

    private static readonly ScreenListener Listener = new();

    public static bool IsInGame => _inGame;

    public static void Init() => Init(Main.OnScreenChange);

    public static void Init(Alloy.UiLib.Signals.Signal<ScreenType> signal) => signal.Add(Listener.OnChange);

    private static void OnScreenChange(ScreenType type) {
        if (type == ScreenType.Game) {
            Enter();
        } else {
            Exit();
        }
    }

    private static void Enter() {
        if (_inGame) {
            return;         // the options menu reports "Game" again while you are already in it
        }

        _inGame = true;
        _polling = false;
        State = null;
        _playingId = null;
        while (Results.TryDequeue(out _)) { }
        _nextPollAt = 0;
    }

    private static void Exit() {
        if (!_inGame) {
            return;
        }

        _inGame = false;
        State = null;
        if (_playingId != null) {
            _playingId = null;
            Audio.MusicChannel.FadeTo(MenuTrack, MusicPlan.CrossfadeMs / 1000f, MusicPlan.CrossfadeMs / 1000f);
        }
    }

    // Ask the server again as soon as possible (the Jukebox window calls this after someone skips or picks).
    public static void Refresh() => _nextPollAt = 0;

    // Called every frame while a game world is on screen.
    public static void Update() {
        if (!_inGame) {
            return;
        }

        var now = Environment.TickCount64;

        while (Results.TryDequeue(out var result)) {
            if (result.Data != null) {
                State = result.Data;
                _heardAt = now;
            } else if (State == null) {
                _nextPollAt = now + RetryMs;        // no music on this server (yet): leave the menu music alone and check again later
            }
        }

        if (State != null) {
            var step = MusicPlan.Decide(State, _heardAt, now, _playingId);
            if (step.PlayId != null) {
                Play(step.PlayId);
                if (step.PollInMs >= 0) {
                    _nextPollAt = Math.Min(_nextPollAt, now + step.PollInMs);
                }
            }
        }

        if (!_polling && now >= _nextPollAt) {
            Poll(now);
        }
    }

    private static void Play(string trackId) {
        var track = State.Find(trackId);
        if (track == null) {
            return;
        }

        _playingId = trackId;
        var seconds = MusicPlan.CrossfadeMs / 1000f;
        Audio.MusicChannel.FadeTo("Music/" + track.File, seconds, seconds);
    }

    private static void Poll(long now) {
        _polling = true;
        _nextPollAt = now + PollMs;
        AppRequests.GetMusicNow().ContinueWith(task => {
            Results.Enqueue(task.IsCompletedSuccessfully ? task.Result : new AppRequests.MusicNowResult { Error = "Could not reach the server." });
            _polling = false;
        });
    }
}
