using System;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Database.Models;
using Dapper;
using Npgsql;

namespace Common.Database;

// Everything a moderator or the owner does to ANOTHER account: bans, mutes, rank changes, and mail. Each method returns null on success or a sentence that can be shown
// to the person who typed the command. Who may do what to whom is decided here (Ranks / ModerationRules), not by the caller, so no command can forget to check.
//
// Bans, mutes and ranks are written with small targeted UPDATEs on the account's JSON (jsonb_set) rather than by re-saving a whole Account, so they cannot overwrite
// anything else (gold, characters ...) that changed since the caller last loaded the account.
public static class AdminDb {
    public readonly record struct Brief(int Id, string Name, int Rank, bool IsBanned);

    public static async Task<Brief?> FindAsync(string name) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var acc = await LoadAsync(conn, name);
        return acc == null ? null : new Brief(acc.Id, acc.Name, Ranks.Of(acc), acc.IsBanned);
    }

    public static async Task<string> BanAsync(int moderatorId, string targetName, string reason, TimeSpan? duration) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var (problem, target) = await CheckAsync(conn, moderatorId, targetName);
        if (problem != null)
            return problem;

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "INSERT INTO bans (target_acc_id, moderator_acc_id, reason, created_at, expires_at, permanent) VALUES (@Target, @Mod, @Reason, @Now, @Expires, @Permanent)",
            new { Target = target.Id, Mod = moderatorId, Reason = ModerationRules.CleanReason(reason), Now = now, Expires = duration == null ? (DateTime?)null : now + duration.Value, Permanent = duration == null });
        await SetFlagAsync(conn, target.Id, "IsBanned", true);
        return null;
    }

    public static async Task<string> UnbanAsync(int moderatorId, string targetName) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var (problem, target) = await CheckAsync(conn, moderatorId, targetName);
        if (problem != null)
            return problem;

        if (!target.IsBanned)
            return $"{target.Name} is not banned.";

        // End every ban that is still running (a permanent one stops being permanent), then clear the flag.
        await conn.ExecuteAsync("UPDATE bans SET permanent=false, expires_at=@Now WHERE target_acc_id=@Target AND (permanent OR expires_at > @Now)", new { Target = target.Id, Now = DateTime.UtcNow });
        await SetFlagAsync(conn, target.Id, "IsBanned", false);
        return null;
    }

    public static async Task<string> MuteAsync(int moderatorId, string targetName, string reason, TimeSpan? duration) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var (problem, target) = await CheckAsync(conn, moderatorId, targetName);
        if (problem != null)
            return problem;

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "INSERT INTO mutes (target_acc_id, moderator_acc_id, reason, created_at, expires_at) VALUES (@Target, @Mod, @Reason, @Now, @Expires)",
            new { Target = target.Id, Mod = moderatorId, Reason = ModerationRules.CleanReason(reason), Now = now, Expires = duration == null ? (DateTime?)null : now + duration.Value });
        return null;
    }

    public static async Task<string> UnmuteAsync(int moderatorId, string targetName) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var (problem, target) = await CheckAsync(conn, moderatorId, targetName);
        if (problem != null)
            return problem;

        var ended = await conn.ExecuteAsync("UPDATE mutes SET expires_at=@Now WHERE target_acc_id=@Target AND (expires_at IS NULL OR expires_at > @Now)", new { Target = target.Id, Now = DateTime.UtcNow });
        return ended == 0 ? $"{target.Name} is not muted." : null;
    }

    // The end of the longest mute still running for this account: null = not muted, DateTime.MaxValue = muted with no end.
    public static async Task<DateTime?> ActiveMuteEndAsync(int accountId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.QueryAsync<DateTime?>("SELECT expires_at FROM mutes WHERE target_acc_id=@Id AND (expires_at IS NULL OR expires_at > @Now)", new { Id = accountId, Now = DateTime.UtcNow });
        DateTime? end = null;
        foreach (var row in rows) {
            var candidate = row ?? DateTime.MaxValue;
            if (end == null || candidate > end)
                end = candidate;
        }

        return end;
    }

    // Owner only, and never on another owner (an owner is changed by hand in the database on purpose, so no command can be used to take the game over).
    public static async Task<string> SetRankAsync(int ownerId, string targetName, int rank) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var actor = await LoadAsync(conn, ownerId);
        if (actor == null || !Ranks.IsOwner(actor))
            return "Only an Owner can change ranks.";

        var target = await LoadAsync(conn, targetName);
        if (target == null)
            return $"Player {targetName} not found.";
        if (target.Id == actor.Id)
            return "You cannot change your own rank.";
        if (Ranks.IsOwner(target))
            return "An Owner's rank can only be changed in the database.";

        var set = new Account();
        Ranks.Set(set, rank);
        await conn.ExecuteAsync(
            "UPDATE accounts SET data = jsonb_set(jsonb_set(data, '{Rank}', to_jsonb(@Rank::int)), '{IsAdmin}', to_jsonb(@Admin::boolean)) WHERE id=@Id",
            new { Rank = set.Rank, Admin = set.IsAdmin, Id = target.Id });
        return null;
    }

    // Puts a message (with an optional gold / fame gift) in a player's Inbox. From = who it says it is from.
    public static async Task<string> MailAsync(string targetName, string from, string subject, string body, int gold, int fame) {
        if (!InboxRules.ValidAttachment(gold, fame))
            return "That gold / fame amount is not allowed.";

        await using var conn = await DbClient.OpenConnectionAsync();
        var target = await LoadAsync(conn, targetName);
        if (target == null)
            return $"Player {targetName} not found.";

        await RewardsDb.AddMailAsync(conn, target.Id, from, subject, body, gold, fame);
        return null;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------------------------------

    // Common checks for anything done to another account: the moderator is real and outranks the target.
    private static async Task<(string Problem, Account Target)> CheckAsync(NpgsqlConnection conn, int moderatorId, string targetName) {
        var actor = await LoadAsync(conn, moderatorId);
        if (actor == null || !Ranks.IsModerator(actor))
            return ("You're not authorized to do that.", null);

        var target = await LoadAsync(conn, targetName);
        if (target == null)
            return ($"Player {targetName} not found.", null);
        if (target.Id == actor.Id)
            return ("You cannot do that to yourself.", null);
        if (!Ranks.CanModerate(actor, target))
            return ($"You cannot moderate {target.Name}: they are the same rank as you or higher.", null);
        return (null, target);
    }

    private static async Task<Account> LoadAsync(NpgsqlConnection conn, string name) {
        var data = await conn.ExecuteScalarAsync<string>("SELECT data FROM accounts WHERE LOWER(name)=LOWER(@Name)", new { Name = name ?? string.Empty });
        return data == null ? null : JsonSerializer.Deserialize<Account>(data);
    }

    private static async Task<Account> LoadAsync(NpgsqlConnection conn, int id) {
        var data = await conn.ExecuteScalarAsync<string>("SELECT data FROM accounts WHERE id=@Id", new { Id = id });
        return data == null ? null : JsonSerializer.Deserialize<Account>(data);
    }

    private static Task SetFlagAsync(NpgsqlConnection conn, int accountId, string property, bool value) =>
        conn.ExecuteAsync($"UPDATE accounts SET data = jsonb_set(data, '{{{property}}}', to_jsonb(@Value::boolean)) WHERE id=@Id", new { Value = value, Id = accountId });
}
