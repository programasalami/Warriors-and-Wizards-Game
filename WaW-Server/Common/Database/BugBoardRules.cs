using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Common.Database;

// The rules of the Bug Board that need no database, so they can be tested on their own. Everything a player writes is untrusted: it is kept as plain text,
// trimmed and capped, and posting is rate limited.
public static class BugBoardRules {

    public const int MaxLength = 200;            // the client's text box holds at most 255 characters
    public const int MaxListed = 50;             // how many of the newest posts are sent to a client
    public const int CooldownSeconds = 20;       // between two posts by the same account
    public const int MaxPerHour = 10;

    public const string StatusNew = "new";
    public const string StatusConfirmed = "confirmed";
    public const string StatusFixed = "fixed";

    public static bool IsValidStatus(string status) => status is StatusNew or StatusConfirmed or StatusFixed;

    // Plain text only: control characters and line breaks become spaces, invisible "format" characters (for example the right-to-left override that can make text
    // read backwards) are dropped, runs of spaces collapse, the ends are trimmed and it is cut to MaxLength. An empty result means "nothing to post".
    public static string Clean(string message) {
        if (string.IsNullOrEmpty(message)) {
            return string.Empty;
        }

        var text = new StringBuilder(Math.Min(message.Length, MaxLength * 2));
        var lastWasSpace = true;
        foreach (var c in message) {
            var category = char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.Format) {
                continue;
            }

            if (char.IsControl(c) || char.IsWhiteSpace(c)) {
                if (!lastWasSpace) {
                    text.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            text.Append(c);
            lastWasSpace = false;
        }

        var cleaned = text.ToString().TrimEnd();
        if (cleaned.Length > MaxLength) {
            cleaned = cleaned[..MaxLength];
            if (char.IsHighSurrogate(cleaned[^1])) {
                cleaned = cleaned[..^1];      // never leave half of an emoji
            }

            cleaned = cleaned.TrimEnd();
        }

        return cleaned;
    }

    // null = allowed; otherwise the reason to show the player. `recent` = when (unix seconds) this account posted in the last hour.
    public static string CheckRate(IReadOnlyCollection<long> recent, long nowUnix) {
        long newest = long.MinValue;
        foreach (var t in recent) {
            if (t > newest) {
                newest = t;
            }
        }

        if (recent.Count > 0 && nowUnix - newest < CooldownSeconds) {
            var wait = CooldownSeconds - (nowUnix - newest);
            return $"Please wait {wait} more second{(wait == 1 ? "" : "s")} before posting again.";
        }

        if (recent.Count >= MaxPerHour) {
            return "You have posted a lot in the last hour. Please try again later.";
        }

        return null;
    }
}
