using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.Music;

public sealed record MusicTrack(string Id, string File, string Title, int LengthMs);

// What is playing right now: the track, how far into it, what comes next, and a number that changes every time the track changes (a skip, a pick, or the
// shuffle moving on), so a client can tell "the same song, still going" from "a different song".
public sealed record MusicSnapshot(MusicTrack Current, long PositionMs, MusicTrack Next, int Serial, string ChangedBy);

// The one shared in-game playlist. Everybody hears the same track. It shuffles the whole library on its own, forever, moving on when a track's time is up
// (the clock is passed in, so this class knows nothing about real time and can be tested exactly).
//
// Shuffle = a "bag": every other track is played once, in random order, before the bag is refilled, and the track that just played is never put first in a
// new bag, so nothing repeats back to back and nothing is neglected.
public sealed class MusicDirector {

    public const int HistoryLimit = 20;
    public const int MinTrackLengthMs = 1000;

    private readonly IReadOnlyList<MusicTrack> _tracks;
    private readonly Random _random;
    private readonly List<int> _bag = [];          // what comes next, in order
    private readonly List<int> _history = [];      // what played before the current track (the most recent is last), for "previous"
    private int _current;
    private long _startedAtMs;

    public int Serial { get; private set; } = 1;
    public string ChangedBy { get; private set; }

    public IReadOnlyList<MusicTrack> Tracks => _tracks;

    public MusicDirector(IReadOnlyList<MusicTrack> tracks, long nowMs, Random random) {
        if (tracks == null || tracks.Count == 0) {
            throw new ArgumentException("The music library is empty.", nameof(tracks));
        }

        if (tracks.Any(t => t.LengthMs < MinTrackLengthMs)) {
            throw new ArgumentException($"Every track must be at least {MinTrackLengthMs} ms long.", nameof(tracks));
        }

        if (tracks.Select(t => t.Id).Distinct().Count() != tracks.Count) {
            throw new ArgumentException("Track ids must be unique.", nameof(tracks));
        }

        _tracks = tracks;
        _random = random;
        _current = _random.Next(tracks.Count);
        _startedAtMs = nowMs;
        Refill();
    }

    public MusicSnapshot Snapshot(long nowMs) {
        Advance(nowMs);
        return new MusicSnapshot(_tracks[_current], nowMs - _startedAtMs, _tracks[_bag[0]], Serial, ChangedBy);
    }

    // Next / previous. `by` is who did it (shown to everyone), null when it is only the shuffle.
    public void Skip(long nowMs, bool forward, string by) {
        Advance(nowMs);

        if (forward) {
            MoveToNext(by);
        } else if (_history.Count > 0) {
            _bag.Insert(0, _current);                        // "next" from there comes back to where we were
            _current = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            Serial++;
            ChangedBy = by;
        } else {
            Serial++;                                        // nothing before it: start this one again
            ChangedBy = by;
        }

        _startedAtMs = nowMs;
    }

    // Jump to a chosen track. False if there is no such track. Choosing what is already playing changes nothing.
    public bool Set(long nowMs, string trackId, string by) {
        var index = -1;
        for (var i = 0; i < _tracks.Count; i++) {
            if (_tracks[i].Id == trackId) {
                index = i;
            }
        }

        if (index < 0) {
            return false;
        }

        Advance(nowMs);
        if (index == _current) {
            return true;
        }

        PushHistory();
        _bag.Remove(index);                                  // it has just been played, so it is not due again soon
        _current = index;
        _startedAtMs = nowMs;
        Serial++;
        ChangedBy = by;
        if (_bag.Count == 0) {
            Refill();
        }

        return true;
    }

    // Move on for as long as tracks have run out (however long nobody asked: it catches up one track at a time).
    private void Advance(long nowMs) {
        if (nowMs < _startedAtMs) {
            _startedAtMs = nowMs;                            // the clock went backwards: never a negative position
            return;
        }

        while (nowMs - _startedAtMs >= _tracks[_current].LengthMs) {
            _startedAtMs += _tracks[_current].LengthMs;
            MoveToNext(null);
        }
    }

    private void MoveToNext(string by) {
        PushHistory();
        _current = _bag[0];
        _bag.RemoveAt(0);
        if (_bag.Count == 0) {
            Refill();
        }

        Serial++;
        ChangedBy = by;
    }

    private void PushHistory() {
        _history.Add(_current);
        if (_history.Count > HistoryLimit) {
            _history.RemoveAt(0);
        }
    }

    // Everything except the track that is playing now, shuffled (with a single track in the library it simply repeats).
    private void Refill() {
        var indexes = Enumerable.Range(0, _tracks.Count).Where(i => _tracks.Count == 1 || i != _current).ToList();
        for (var i = indexes.Count - 1; i > 0; i--) {
            var j = _random.Next(i + 1);
            (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
        }

        _bag.AddRange(indexes);
    }
}

// Who may change the shared music, and how often. Changing it affects everybody, so it is limited per account and overall.
// (Later this is also where fame gets charged: skipping / picking a song will cost fame. Both are free for now.)
public static class MusicRules {

    public const int SkipFameCost = 0;
    public const int PickFameCost = 0;

    public const int AccountCooldownMs = 10_000;
    public const int GlobalCooldownMs = 2_000;

    // null = allowed; otherwise the reason to show the player. Times are milliseconds on one clock; a value of long.MinValue means "never".
    public static string CheckCooldown(long nowMs, long lastByThisAccountMs, long lastByAnyoneMs) {
        if (lastByThisAccountMs != long.MinValue && nowMs - lastByThisAccountMs < AccountCooldownMs) {
            var wait = (int)Math.Ceiling((AccountCooldownMs - (nowMs - lastByThisAccountMs)) / 1000.0);
            return $"Please wait {wait} more second{(wait == 1 ? "" : "s")} before changing the music again.";
        }

        if (lastByAnyoneMs != long.MinValue && nowMs - lastByAnyoneMs < GlobalCooldownMs) {
            return "The music was just changed by someone else. Try again in a moment.";
        }

        return null;
    }
}
