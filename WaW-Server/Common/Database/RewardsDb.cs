using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace Common.Database;

// The Inbox, the Daily Gift and the Daily Spin (Character Book pages). Gold / fame are added to the account with one atomic UPDATE on its JSON, in the same
// transaction that marks the message claimed / the day used - so nothing can be claimed twice, even by two requests at once, and a crash cannot lose or duplicate a reward.
// The daily rewards use a real 24-hour cooldown from the moment of the last claim (see DailyCooldown); "now" is passed in so tests can choose it.
public static class RewardsDb {
    public readonly record struct Message(int Id, string Sender, string Subject, string Body, int Gold, int Fame, long CreatedUnix, bool IsRead, bool Claimed);

    public readonly record struct DailyStatus(bool GiftReady, int GiftDay, int Streak, bool SpinReady, int Unread, int SecondsToGift, int SecondsToSpin);

    public readonly record struct GiftResult(string Error, int Day, int Streak, Reward Reward);

    public readonly record struct SpinResult(string Error, int Index, Reward Reward);

    // The account's current gold and fame (what the Character Book shows), read straight from the database.
    public static async Task<(int Gold, int Fame)> BalanceAsync(int accId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var row = await conn.QueryFirstOrDefaultAsync<ClaimRow>(
            "SELECT COALESCE((data#>>'{Stats,CurrentCredits}')::int, 0) AS Gold, COALESCE((data#>>'{Stats,CurrentFame}')::int, 0) AS Fame FROM accounts WHERE id=@Acc", new { Acc = accId });
        return row == null ? (0, 0) : (row.Gold, row.Fame);
    }

    public static DateTime UtcNow() => DateTime.UtcNow;

    // ---- Inbox -----------------------------------------------------------------------------------------------------------------------------------------------

    public static async Task<List<Message>> ListAsync(int accId, int limit = 50) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.QueryAsync<MessageRow>(
            "SELECT id AS Id, sender AS Sender, subject AS Subject, body AS Body, gold AS Gold, fame AS Fame, EXTRACT(EPOCH FROM created_at)::bigint AS Created, is_read AS IsRead, claimed AS Claimed FROM inbox_messages " +
            "WHERE acc_id=@Acc ORDER BY created_at DESC, id DESC LIMIT @Limit", new { Acc = accId, Limit = limit });
        return rows.Select(r => new Message(r.Id, r.Sender, r.Subject, r.Body, r.Gold, r.Fame, r.Created, r.IsRead, r.Claimed)).ToList();
    }

    public static async Task AddMailAsync(NpgsqlConnection conn, int accId, string from, string subject, string body, int gold, int fame) {
        await conn.ExecuteAsync(
            "INSERT INTO inbox_messages (acc_id, sender, subject, body, gold, fame) VALUES (@Acc, @From, @Subject, @Body, @Gold, @Fame)",
            new {
                Acc = accId,
                From = InboxRules.Clean(from, 32),
                Subject = InboxRules.Clean(subject, InboxRules.MaxSubject),
                Body = InboxRules.Clean(body, InboxRules.MaxBody),
                Gold = gold,
                Fame = fame
            });

        // Keep the box from growing forever: drop the oldest messages nothing is left to claim on.
        await conn.ExecuteAsync(
            "DELETE FROM inbox_messages WHERE id IN (SELECT id FROM inbox_messages WHERE acc_id=@Acc AND (claimed OR (gold=0 AND fame=0)) " +
            "ORDER BY created_at DESC, id DESC OFFSET @Max)", new { Acc = accId, Max = InboxRules.MaxMessagesPerAccount });
    }

    public static async Task AddMailAsync(int accId, string from, string subject, string body, int gold, int fame) {
        await using var conn = await DbClient.OpenConnectionAsync();
        await AddMailAsync(conn, accId, from, subject, body, gold, fame);
    }

    public static async Task<bool> MarkReadAsync(int accId, int messageId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        return await conn.ExecuteAsync("UPDATE inbox_messages SET is_read=true WHERE id=@Id AND acc_id=@Acc", new { Id = messageId, Acc = accId }) > 0;
    }

    // Takes the gold / fame attached to a message. Null = there was nothing (wrong id, not yours, already claimed or no attachment).
    public static async Task<Reward?> ClaimAsync(int accId, int messageId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var row = await conn.QueryFirstOrDefaultAsync<ClaimRow>(
            "UPDATE inbox_messages SET claimed=true, is_read=true WHERE id=@Id AND acc_id=@Acc AND NOT claimed AND (gold > 0 OR fame > 0) RETURNING gold AS Gold, fame AS Fame",
            new { Id = messageId, Acc = accId }, tx);
        if (row == null)
            return null;

        await AddRewardAsync(conn, tx, accId, row.Gold, row.Fame);
        await tx.CommitAsync();
        return new Reward(row.Gold, row.Fame);
    }

    // A message with something still to claim cannot be deleted (it would throw the gift away by accident).
    public static async Task<string> DeleteAsync(int accId, int messageId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var deleted = await conn.ExecuteAsync("DELETE FROM inbox_messages WHERE id=@Id AND acc_id=@Acc AND (claimed OR (gold=0 AND fame=0))", new { Id = messageId, Acc = accId });
        if (deleted > 0)
            return null;
        var exists = await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM inbox_messages WHERE id=@Id AND acc_id=@Acc)", new { Id = messageId, Acc = accId });
        return exists ? "Claim the gift in this message before deleting it." : "That message is already gone.";
    }

    // ---- Daily Gift / Daily Spin -------------------------------------------------------------------------------------------------------------------------------

    public static async Task<DailyStatus> StatusAsync(int accId, DateTime now) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var (lastGift, streak, lastSpin) = await ReadDailyAsync(conn, null, accId, forUpdate: false);
        var gift = DailyGiftRules.Evaluate(lastGift, streak, now);
        var spinWait = DailyCooldown.SecondsLeft(lastSpin, now);
        var unread = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inbox_messages WHERE acc_id=@Acc AND (NOT is_read OR (NOT claimed AND (gold > 0 OR fame > 0)))", new { Acc = accId });
        return new DailyStatus(gift.CanClaim, gift.Day, gift.Streak, spinWait == 0, unread, gift.SecondsLeft, spinWait);
    }

    public static async Task<GiftResult> ClaimGiftAsync(int accId, DateTime now) {
        await using var conn = await DbClient.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("INSERT INTO daily_rewards (acc_id) VALUES (@Acc) ON CONFLICT DO NOTHING", new { Acc = accId }, tx);

        var (lastGift, streak, _) = await ReadDailyAsync(conn, tx, accId, forUpdate: true);
        var state = DailyGiftRules.Evaluate(lastGift, streak, now);
        if (!state.CanClaim)
            return new GiftResult($"You already opened your daily gift. The next one is ready in {Wait(state.SecondsLeft)}.", state.Day, state.Streak, default);

        var reward = DailyGiftRules.RewardFor(state.Day);
        var newStreak = state.Streak + 1;
        await conn.ExecuteAsync("UPDATE daily_rewards SET last_gift_at=@At, gift_streak=@Streak WHERE acc_id=@Acc", new { At = AsUtc(now), Streak = newStreak, Acc = accId }, tx);
        await AddRewardAsync(conn, tx, accId, reward.Gold, reward.Fame);
        await tx.CommitAsync();
        return new GiftResult(null, state.Day, newStreak, reward);
    }

    // The roll is made here (server side) with a secure random number; the client only plays the animation for the answer.
    public static async Task<SpinResult> SpinAsync(int accId, DateTime now) {
        await using var conn = await DbClient.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("INSERT INTO daily_rewards (acc_id) VALUES (@Acc) ON CONFLICT DO NOTHING", new { Acc = accId }, tx);

        var (_, _, lastSpin) = await ReadDailyAsync(conn, tx, accId, forUpdate: true);
        var wait = DailyCooldown.SecondsLeft(lastSpin, now);
        if (wait > 0)
            return new SpinResult($"You already spun. The next spin is ready in {Wait(wait)}.", -1, default);

        var index = SpinWheel.Pick(RandomNumberGenerator.GetInt32(SpinWheel.TotalWeight));
        var reward = SpinWheel.Prizes[index].Reward;
        await conn.ExecuteAsync("UPDATE daily_rewards SET last_spin_at=@At WHERE acc_id=@Acc", new { At = AsUtc(now), Acc = accId }, tx);
        await AddRewardAsync(conn, tx, accId, reward.Gold, reward.Fame);
        await tx.CommitAsync();
        return new SpinResult(null, index, reward);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------------------------------

    private sealed class MessageRow { public int Id { get; set; } public string Sender { get; set; } public string Subject { get; set; } public string Body { get; set; } public int Gold { get; set; } public int Fame { get; set; } public long Created { get; set; } public bool IsRead { get; set; } public bool Claimed { get; set; } }
    private sealed class ClaimRow { public int Gold { get; set; } public int Fame { get; set; } }
    private sealed class DailyRow { public long? Gift { get; set; } public int Streak { get; set; } public long? Spin { get; set; } }

    // The moment of each last claim, as UTC. A claim made before the 24-hour rule only has a DAY on record: it counts as noon UTC of that day.
    private static async Task<(DateTime? LastGift, int Streak, DateTime? LastSpin)> ReadDailyAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int accId, bool forUpdate) {
        var row = await conn.QueryFirstOrDefaultAsync<DailyRow>(
            "SELECT EXTRACT(EPOCH FROM COALESCE(last_gift_at, (last_gift_day::timestamp + interval '12 hours') AT TIME ZONE 'UTC'))::bigint AS Gift, gift_streak AS Streak, " +
            "EXTRACT(EPOCH FROM COALESCE(last_spin_at, (last_spin_day::timestamp + interval '12 hours') AT TIME ZONE 'UTC'))::bigint AS Spin " +
            "FROM daily_rewards WHERE acc_id=@Acc" + (forUpdate ? " FOR UPDATE" : string.Empty),
            new { Acc = accId }, tx);
        if (row == null)
            return (null, 0, null);
        return (FromUnix(row.Gift), row.Streak, FromUnix(row.Spin));
    }

    private static DateTime? FromUnix(long? seconds) => seconds == null ? null : DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime;

    private static DateTime AsUtc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);

    // "5h 12m" / "42m" for the "already claimed" messages.
    private static string Wait(int seconds) {
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        return hours > 0 ? $"{hours}h {minutes:00}m" : $"{Math.Max(1, minutes)}m";
    }

    // One atomic UPDATE: gold goes to Credits, fame to CurrentFame (and both lifetime totals).
    private static Task AddRewardAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int accId, int gold, int fame) =>
        conn.ExecuteAsync(
            "UPDATE accounts SET data = jsonb_set(jsonb_set(jsonb_set(jsonb_set(data, " +
            "'{Stats,CurrentCredits}', to_jsonb(COALESCE((data#>>'{Stats,CurrentCredits}')::int, 0) + @Gold)), " +
            "'{Stats,TotalCredits}', to_jsonb(COALESCE((data#>>'{Stats,TotalCredits}')::int, 0) + @Gold)), " +
            "'{Stats,CurrentFame}', to_jsonb(COALESCE((data#>>'{Stats,CurrentFame}')::int, 0) + @Fame)), " +
            "'{Stats,TotalFame}', to_jsonb(COALESCE((data#>>'{Stats,TotalFame}')::int, 0) + @Fame)) WHERE id=@Acc",
            new { Gold = gold, Fame = fame, Acc = accId }, tx);
}
