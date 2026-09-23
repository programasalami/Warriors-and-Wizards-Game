using System;
using System.Collections.Concurrent;
using AlloyClient.AppEngine;
using AlloyClient.Data;

namespace AlloyClient.Game.Music;

// Three kinds of music, never at the same time (2026-09-21):
//   * MENUS (title screen, the book): the client's own menu track (Settings.MenuMusic, Main_Music by default) - every player picks their own later.
//   * NEXUS and the other shared rooms (Vault, Guild Hall): the JUKEBOX playlist the account server keeps for everybody, so all players hear the same song
//     (one track, Dreamtune, for now). The client only follows the server: it asks now and then what is playing (and right after anyone changes it), starts
//     the current song, and starts the next one a few seconds before the current ends so the two overlap smoothly (see MusicPlan).
//   * REALM: a fixed track of its own (RealmTrack), played locally, no server involved - until the Realm gets dynamic music per player.
// Which one applies comes from the world's music name in MapInfo (OnWorld). Entering the game blends from the menu track into the world's music, leaving
// blends back. If the server has no music (an older server) or cannot be reached, the menu music simply keeps playing in the shared rooms.
public static class InGameMusic {

    // The title-screen / loading track: the player's choice, a file in Content/Sound/Music. Not part of the in-game library.
    public static string MenuTrack => "Music/" + Settings.MenuMusic.Value;

    // The Realm's own music (the file name says so: Realm_* files are never put in the jukebox library, see Tools/Music/make_music_config.py).
    public const string RealmTrack = "Music/Realm_Pondering_The_Cosmos.mp3";
    public const string RealmWorldMusic = "Realm";

    public enum MusicMode { Menu, Playlist, Fixed }

    public static MusicMode Mode { get; private set; } = MusicMode.Menu;

    // What the fixed mode is playing (the Realm track), for tests and the Jukebox window.
    public static string FixedTrack { get; private set; }

    // How a track is started (crossfade in seconds). Tests replace it; the game uses the audio engine.
    public static Action<string, float> PlayFile = (file, seconds) => Audio.MusicChannel.FadeTo(file, seconds, seconds);

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
        FixedTrack = null;
        Mode = MusicMode.Playlist;          // until MapInfo names the world (OnWorld)
        while (Results.TryDequeue(out _)) { }
        _nextPollAt = 0;
    }

    private static void Exit() {
        if (!_inGame) {
            return;
        }

        _inGame = false;
        State = null;
        var wasPlaying = _playingId != null || FixedTrack != null;
        _playingId = null;
        FixedTrack = null;
        Mode = MusicMode.Menu;
        if (wasPlaying) {
            PlayFile(MenuTrack, MusicPlan.CrossfadeMs / 1000f);
        }
    }

    // MapInfo arrived for a world (first entry or a portal / Fast Travel switch): pick that world's kind of music.
    public static void OnWorld(string worldMusic) {
        if (!_inGame) {
            return;
        }

        if (string.Equals(worldMusic, RealmWorldMusic, StringComparison.OrdinalIgnoreCase)) {
            if (Mode == MusicMode.Fixed && FixedTrack == RealmTrack) {
                return;                     // realm to realm: keep the song going
            }

            Mode = MusicMode.Fixed;
            State = null;
            _playingId = null;
            FixedTrack = RealmTrack;
            PlayFile(RealmTrack, MusicPlan.CrossfadeMs / 1000f);
            return;
        }

        // A shared room: the jukebox playlist. Coming from the Realm the playlist is asked for at once; from another shared room it just keeps going.
        if (Mode != MusicMode.Playlist) {
            Mode = MusicMode.Playlist;
            FixedTrack = null;
            State = null;
            _playingId = null;
            _nextPollAt = 0;
        }
    }

    // Ask the server again as soon as possible (the Jukebox window calls this after someone skips or picks).
    public static void Refresh() => _nextPollAt = 0;

    // Called every frame while a game world is on screen.
    public static void Update() {
        if (!_inGame || Mode != MusicMode.Playlist) {
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
        PlayFile("Music/" + track.File, MusicPlan.CrossfadeMs / 1000f);
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
