using System;
using System.Collections.Generic;
using System.Linq;

namespace AlloyClient.Game.Components.Admin;

// The pure parts of the admin dashboard, kept apart from the UI so they can be tested: which ranks see which tools, how a button turns into a chat command
// (the server still decides whether it is allowed - nothing here grants any power), and which art counts as new.
public static class AdminRules {
    public const int ModeratorRank = 80;
    public const int OwnerRank = 100;

    // Durations the dashboard's cycle button offers (the server's own duration syntax, see Common.Database.ModerationRules).
    public static readonly string[] Durations = ["10m", "30m", "2h", "12h", "1d", "7d", "30d", "perm"];

    public static bool IsStaff(int rank) => rank >= ModeratorRank;

    public static bool IsOwner(int rank) => rank >= OwnerRank;

    public static string RankName(int rank) => rank >= OwnerRank ? "Owner" : rank >= ModeratorRank ? "Moderator" : "Player";

    // Account names are letters only, at most 10 - anything else can never be a player, and must not smuggle extra words into a command.
    public static bool IsPlayerName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 10 && name.All(char.IsLetter);

    // A reason is one line of ordinary text: no control characters, no line breaks, at most 120 characters.
    public static string CleanReason(string reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return string.Empty;
        }

        var chars = reason.Where(c => !char.IsControl(c)).ToArray();
        var text = new string(chars).Trim();
        return text.Length > 120 ? text[..120].TrimEnd() : text;
    }

    public static string NextDuration(string current) {
        var i = Array.IndexOf(Durations, current);
        return Durations[(i + 1) % Durations.Length];
    }

    // ---- chat commands (null = the input does not make a valid command) ------------------------------------------------------------------------------------

    public static string Kick(string player) => IsPlayerName(player) ? $"/kick {player}" : null;

    public static string Find(string player) => IsPlayerName(player) ? $"/find {player}" : null;

    public static string Unmute(string player) => IsPlayerName(player) ? $"/unmute {player}" : null;

    public static string Unban(string player) => IsPlayerName(player) ? $"/unban {player}" : null;

    public static string Mute(string player, string duration, string reason) => Timed("mute", player, duration, reason);

    public static string Ban(string player, string duration, string reason) => Timed("ban", player, duration, reason);

    private static string Timed(string command, string player, string duration, string reason) {
        if (!IsPlayerName(player) || Array.IndexOf(Durations, duration) < 0) {
            return null;
        }

        var text = $"/{command} {player} {duration}";
        var why = CleanReason(reason);
        return why.Length > 0 ? text + " " + why : text;
    }

    public static string Give(string itemName) => IsSimpleName(itemName) ? $"/give {itemName.Trim()}" : null;

    public static string Spawn(int count, string entityName) => count is >= 1 and <= 50 && IsSimpleName(entityName) ? $"/spawn {count} {entityName.Trim()}" : null;

    // A game object's name as typed in a command: something visible, one line.
    private static bool IsSimpleName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 60 && !name.Any(char.IsControl);
}

// Which art is ours. A definition is "new" when its sheet is one of the sheets drawn / bought for this game; everything else is still the original source's art
// (see the vault note "Asset Replacement": the goal is to make the original count zero). A few cells on a new sheet are still original art (StockCells).
public static class ArtRules {
    private static readonly HashSet<string> NewSheets = new(StringComparer.Ordinal) {
        "grasslands", "smallplants", "mediumplants", "smalltrees", "mediumtrees", "flatprops", "largeobjects", "equipandconsume", "players"
    };

    // Original (stock) pictures that sit on a new sheet until they are replaced: "sheet:index" (lower-case sheet). Health Potion = equipAndConsume cell 8.
    private static readonly HashSet<string> StockCells = new(StringComparer.Ordinal) { "equipandconsume:8" };

    public static bool IsNewSheet(string sheet) => !string.IsNullOrEmpty(sheet) && NewSheets.Contains(sheet.ToLowerInvariant());

    public static bool IsNew(string sheet, int index) => IsNewSheet(sheet) && !StockCells.Contains(sheet.ToLowerInvariant() + ":" + index);

    public static string Label(string sheet, int index = -1) => string.IsNullOrEmpty(sheet) ? "no art" : IsNew(sheet, index) ? "NEW" : "ORIGINAL";
}
