using Common.Database;

namespace Common.Tests;

public class ModerationRulesTests {
    [Theory]
    [InlineData("30m", 30 * 60)]
    [InlineData("12h", 12 * 3600)]
    [InlineData("7d", 7 * 86400)]
    [InlineData("2W", 14 * 86400)]
    [InlineData(" 1d ", 86400)]
    [InlineData("365d", 365 * 86400)]
    public void ParsesFiniteDurations(string text, int seconds) {
        Assert.True(ModerationRules.TryParseDuration(text, out var d));
        Assert.Equal(seconds, (int)d!.Value.TotalSeconds);
    }

    [Theory]
    [InlineData("perm")]
    [InlineData("Permanent")]
    [InlineData("forever")]
    public void PermanentHasNoEnd(string text) {
        Assert.True(ModerationRules.TryParseDuration(text, out var d));
        Assert.Null(d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("d")]
    [InlineData("0d")]
    [InlineData("-3d")]
    [InlineData("5x")]
    [InlineData("five")]
    [InlineData("366d")]
    [InlineData("99999w")]
    [InlineData("1.5h")]
    public void RejectsNonsense(string text) {
        Assert.False(ModerationRules.TryParseDuration(text, out _));
    }

    [Fact]
    public void IsActiveWhilePermanentOrNotYetExpired() {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ModerationRules.IsActive(null, true, now));
        Assert.True(ModerationRules.IsActive(now.AddMinutes(1), false, now));
        Assert.False(ModerationRules.IsActive(now, false, now));
        Assert.False(ModerationRules.IsActive(now.AddMinutes(-1), false, now));
        Assert.False(ModerationRules.IsActive(null, false, now), "no end and not permanent = lifted");
    }

    [Fact]
    public void ReasonIsTrimmedAndCapped() {
        Assert.Equal("spam", ModerationRules.CleanReason("  spam \n"));
        Assert.Equal(string.Empty, ModerationRules.CleanReason(null));
        Assert.Equal(ModerationRules.MaxReasonLength, ModerationRules.CleanReason(new string('x', 999)).Length);
    }

    [Theory]
    [InlineData(45, "45 minutes")]
    [InlineData(1, "1 minute")]
    [InlineData(60, "1 hour")]
    [InlineData(125, "2 hours 5 minutes")]
    [InlineData(2 * 1440, "2 days")]
    [InlineData(1440, "1 day")]
    public void DescribesDurations(int minutes, string expected) {
        Assert.Equal(expected, ModerationRules.Describe(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void DescribesPermanent() => Assert.Equal("permanently", ModerationRules.Describe(null));
}
