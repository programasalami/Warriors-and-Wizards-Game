using System;
using System.Globalization;

namespace Common.Database;

// Pure rules for bans and mutes, kept apart from the database so they can be tested.
public static class ModerationRules {
    public const int MaxReasonLength = 200;

    // "perm" (or "permanent" / "forever") -> no end. Otherwise a number followed by m(inutes), h(ours), d(ays) or w(eeks): 30m, 12h, 7d, 2w.
    // The longest finite ban / mute is one year. Returns false for anything else.
    public static bool TryParseDuration(string text, out TimeSpan? duration) {
        duration = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().ToLowerInvariant();
        if (text is "perm" or "permanent" or "forever")
            return true;

        if (text.Length < 2 || !int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;

        TimeSpan? span = text[^1] switch {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            'w' => TimeSpan.FromDays(7d * amount),
            _ => null
        };

        if (span == null || span > TimeSpan.FromDays(365))
            return false;

        duration = span;
        return true;
    }

    // A ban or mute is in force while it is permanent or its end lies in the future. (A permanent record has no end: null.)
    public static bool IsActive(DateTime? expiresAtUtc, bool permanent, DateTime nowUtc) => permanent || (expiresAtUtc != null && expiresAtUtc > nowUtc);

    public static string CleanReason(string reason) {
        reason = (reason ?? string.Empty).Trim();
        return reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
    }

    // "3 days", "2 hours 5 minutes", "45 minutes" - for messages to moderators and players.
    public static string Describe(TimeSpan? duration) {
        if (duration == null)
            return "permanently";
        var d = duration.Value;
        if (d.TotalDays >= 1)
            return Plural((int)Math.Round(d.TotalDays), "day");
        if (d.TotalHours >= 1) {
            var hours = (int)d.TotalHours;
            var minutes = d.Minutes;
            return minutes == 0 ? Plural(hours, "hour") : Plural(hours, "hour") + " " + Plural(minutes, "minute");
        }

        return Plural(Math.Max(1, (int)Math.Round(d.TotalMinutes)), "minute");
    }

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
