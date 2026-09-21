using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace Common.Database;

public sealed class BugPost {
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string Author { get; set; }
    public string Message { get; set; }
    public string Status { get; set; }
    public long CreatedAt { get; set; }      // unix seconds
}

// Storage for the Bug Board (table bug_posts, created by Schema.sql). All queries are parameterised.
public static class BugBoardDb {

    public static async Task<List<BugPost>> ListAsync() {
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.QueryAsync<BugPost>(
            "SELECT id AS Id, account_id AS AccountId, author AS Author, message AS Message, status AS Status, " +
            "EXTRACT(EPOCH FROM created_at)::bigint AS CreatedAt " +
            "FROM bug_posts ORDER BY created_at DESC, id DESC LIMIT @Limit",
            new { Limit = BugBoardRules.MaxListed });
        return rows.ToList();
    }

    // When (unix seconds) this account posted during the last hour.
    public static async Task<List<long>> RecentTimesAsync(int accountId) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.QueryAsync<long>(
            "SELECT EXTRACT(EPOCH FROM created_at)::bigint FROM bug_posts " +
            "WHERE account_id = @AccountId AND created_at > now() - interval '1 hour'",
            new { AccountId = accountId });
        return rows.ToList();
    }

    public static async Task<long> NowUnixAsync() {
        await using var conn = await DbClient.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>("SELECT EXTRACT(EPOCH FROM now())::bigint");
    }

    public static async Task<int> AddAsync(int accountId, string author, string message) {
        await using var conn = await DbClient.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "INSERT INTO bug_posts (account_id, author, message) VALUES (@AccountId, @Author, @Message) RETURNING id",
            new { AccountId = accountId, Author = author, Message = message });
    }

    public static async Task<bool> DeleteAsync(int id) {
        await using var conn = await DbClient.OpenConnectionAsync();
        return await conn.ExecuteAsync("DELETE FROM bug_posts WHERE id = @Id", new { Id = id }) > 0;
    }

    public static async Task<bool> SetStatusAsync(int id, string status) {
        await using var conn = await DbClient.OpenConnectionAsync();
        return await conn.ExecuteAsync("UPDATE bug_posts SET status = @Status WHERE id = @Id", new { Id = id, Status = status }) > 0;
    }
}
