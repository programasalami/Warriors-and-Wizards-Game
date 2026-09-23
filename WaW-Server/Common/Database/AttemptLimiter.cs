using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Common.Database;

// Counts attempts per key (an account name, an IP address) inside a sliding window and says when a key has had too many.
// Pure and clock-injected so it can be tested exactly. Thread-safe: one lock per key list, entries pruned as they age out;
// keys that go quiet are dropped on the next sweep so the table cannot grow without limit. 2026-09-21 audit (C7 / F43): logins
// and registrations had no rate limit at all.
public sealed class AttemptLimiter {
    public int MaxAttempts { get; }
    public TimeSpan Window { get; }

    private readonly Func<DateTime> _clock;
    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSweep;

    public AttemptLimiter(int maxAttempts, TimeSpan window, Func<DateTime> clock = null) {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        MaxAttempts = maxAttempts;
        Window = window;
        _clock = clock ?? (() => DateTime.UtcNow);
        _lastSweep = _clock();
    }

    // True if this key has reached the limit (attempts inside the window >= MaxAttempts). Does not record anything.
    public bool IsBlocked(string key) {
        if (string.IsNullOrEmpty(key))
            return false;
        if (!_attempts.TryGetValue(key, out var list))
            return false;
        lock (list) {
            Prune(list, _clock());
            return list.Count >= MaxAttempts;
        }
    }

    // Records one attempt for the key; returns true if the key is now blocked.
    public bool Record(string key) {
        if (string.IsNullOrEmpty(key))
            return false;
        var now = _clock();
        var list = _attempts.GetOrAdd(key, _ => new List<DateTime>());
        bool blocked;
        lock (list) {
            Prune(list, now);
            list.Add(now);
            blocked = list.Count >= MaxAttempts;
        }
        SweepIfDue(now);
        return blocked;
    }

    // A success clears the key (a correct login after a few typos should not leave the account half-blocked).
    public void Clear(string key) {
        if (!string.IsNullOrEmpty(key))
            _attempts.TryRemove(key, out _);
    }

    public int Count(string key) {
        if (string.IsNullOrEmpty(key) || !_attempts.TryGetValue(key, out var list))
            return 0;
        lock (list) {
            Prune(list, _clock());
            return list.Count;
        }
    }

    private void Prune(List<DateTime> list, DateTime now) {
        var cutoff = now - Window;
        list.RemoveAll(t => t < cutoff);
    }

    private void SweepIfDue(DateTime now) {
        if (now - _lastSweep < Window)
            return;
        _lastSweep = now;
        foreach (var (key, list) in _attempts) {
            lock (list) {
                Prune(list, now);
                if (list.Count == 0)
                    _attempts.TryRemove(key, out _);
            }
        }
    }
}
