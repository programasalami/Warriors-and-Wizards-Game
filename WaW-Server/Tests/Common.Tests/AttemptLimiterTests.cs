using Common.Database;

namespace Common.Tests;

// Login / registration rate limiting (2026-09-21 audit, C7): a sliding window per key, cleared by a success.
public class AttemptLimiterTests {

    [Fact]
    public void BlocksAfterTheLimitInsideTheWindow() {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var limiter = new AttemptLimiter(3, TimeSpan.FromMinutes(10), () => now);

        Assert.False(limiter.Record("alice"));
        Assert.False(limiter.Record("alice"));
        Assert.False(limiter.IsBlocked("alice"));
        Assert.True(limiter.Record("alice"));       // third failure: blocked
        Assert.True(limiter.IsBlocked("alice"));
        Assert.False(limiter.IsBlocked("bob"));     // other keys unaffected
    }

    [Fact]
    public void OldAttemptsFallOutOfTheWindow() {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var limiter = new AttemptLimiter(2, TimeSpan.FromMinutes(10), () => now);

        limiter.Record("alice");
        now = now.AddMinutes(6);
        Assert.True(limiter.Record("alice"));
        now = now.AddMinutes(5);                    // the first attempt is now 11 minutes old
        Assert.False(limiter.IsBlocked("alice"));
        Assert.Equal(1, limiter.Count("alice"));
    }

    [Fact]
    public void ASuccessClearsTheKey() {
        var limiter = new AttemptLimiter(2, TimeSpan.FromMinutes(10));
        limiter.Record("alice");
        limiter.Record("alice");
        Assert.True(limiter.IsBlocked("alice"));
        limiter.Clear("alice");
        Assert.False(limiter.IsBlocked("alice"));
        Assert.Equal(0, limiter.Count("alice"));
    }

    [Fact]
    public void KeysAreCaseInsensitiveAndEmptyKeysAreIgnored() {
        var limiter = new AttemptLimiter(1, TimeSpan.FromMinutes(1));
        Assert.True(limiter.Record("Alice"));
        Assert.True(limiter.IsBlocked("alice"));
        Assert.False(limiter.Record(null));
        Assert.False(limiter.Record(""));
        Assert.False(limiter.IsBlocked(null));
    }

    [Fact]
    public void QuietKeysAreSweptAway() {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var limiter = new AttemptLimiter(5, TimeSpan.FromMinutes(1), () => now);
        limiter.Record("alice");
        now = now.AddMinutes(2);
        limiter.Record("bob");                      // triggers the sweep
        Assert.Equal(0, limiter.Count("alice"));
        Assert.Equal(1, limiter.Count("bob"));
    }
}
