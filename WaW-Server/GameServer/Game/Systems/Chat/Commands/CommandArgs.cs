using Common.Database;

namespace GameServer.Game.Systems.Chat.Commands;

// Turns the text after a chat command into its parts. Pure (no server state), so every form of every command can be tested. Each Try... returns false
// and puts a usage sentence in `error` when the text does not fit.
public static class CommandArgs {
    public const int MaxGiveCount = 8;

    // /ban <player> <duration> [reason...]   and   /mute <player> <duration> [reason...]      duration: perm, 30m, 12h, 7d, 2w
    public static bool TryModeration(string args, string usage, out string target, out TimeSpan? duration, out string reason, out string error) {
        target = null;
        duration = null;
        reason = string.Empty;

        var parts = (args ?? string.Empty).Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) {
            error = usage;
            return false;
        }

        if (!ModerationRules.TryParseDuration(parts[1], out duration)) {
            error = $"'{parts[1]}' is not a duration. Use perm, or a number with m / h / d / w (30m, 12h, 7d, 2w, at most 365d).";
            return false;
        }

        target = parts[0];
        reason = parts.Length > 2 ? ModerationRules.CleanReason(parts[2]) : string.Empty;
        error = null;
        return true;
    }

    // /give [count] <item name...>     count 1 to MaxGiveCount, default 1
    public static bool TryGive(string args, out int count, out string query, out string error) {
        count = 1;
        query = null;

        var text = (args ?? string.Empty).Trim();
        if (text.Length == 0) {
            error = "Usage: /give [count] <item name>   (part of the name is enough, e.g. /give iron sword)";
            return false;
        }

        if (int.TryParse(text, out _)) {
            error = "Which item? Usage: /give [count] <item name>";
            return false;
        }

        var space = text.IndexOf(' ');
        if (space > 0 && int.TryParse(text.AsSpan(0, space), out var n)) {
            if (n < 1 || n > MaxGiveCount) {
                error = $"You can give between 1 and {MaxGiveCount} at a time.";
                return false;
            }

            count = n;
            text = text[(space + 1)..].Trim();
        }

        if (text.Length == 0) {
            error = "Which item? Usage: /give [count] <item name>";
            return false;
        }

        query = text;
        error = null;
        return true;
    }

    // /mail <player> <gold> <fame> <message...>
    public static bool TryMail(string args, out string target, out int gold, out int fame, out string message, out string error) {
        target = null;
        gold = 0;
        fame = 0;
        message = null;

        var parts = (args ?? string.Empty).Split(' ', 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !int.TryParse(parts[1], out gold) || !int.TryParse(parts[2], out fame)) {
            error = "Usage: /mail <player> <gold> <fame> <message>   (use 0 0 for a message with no gift)";
            return false;
        }

        if (!InboxRules.ValidAttachment(gold, fame)) {
            error = $"Gold and fame must be between 0 and {InboxRules.MaxAttachment:N0}.";
            return false;
        }

        target = parts[0];
        message = parts[3];
        error = null;
        return true;
    }

    // /setrank <player> <player|moderator|owner>
    public static bool TrySetRank(string args, out string target, out int rank, out string error) {
        target = null;
        rank = Ranks.Player;

        var parts = (args ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Ranks.TryParse(parts[1], out rank)) {
            error = "Usage: /setrank <player> <player|moderator|owner>";
            return false;
        }

        target = parts[0];
        error = null;
        return true;
    }
}
