using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Common.Database;

// Replaces Account.LockOwner. Which GameServer currently owns a given account
// is ephemeral coordination state, not durable data, so it lives in Redis only
// and never touches Postgres - unlike the old LiteDB field, there's nothing
// here to forget to flush.
public static class AccountLockManager {
    private static ConnectionMultiplexer _redis;
    private static IDatabase _db;

    private static string LockKey(int accountId) => $"account_lock:{accountId}";
    private static string ServerLocksKey(Guid serverGuid) => $"gameserver_locks:{serverGuid}";

    // Atomic compare-and-set: acquires the lock if it's unheld, or if it's
    // already held by this same server (idempotent re-lock on reconnect).
    // Refuses if some other server holds it. One round trip either way, so two
    // simultaneous logins for the same account can't both win.
    private const string AcquireScript = @"
        local current = redis.call('GET', KEYS[1])
        if current == false or current == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[1])
            return 1
        end
        return 0";

    public static void Init(string connectionString) {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public static async Task<bool> TryAcquireAsync(int accountId, Guid serverGuid) {
        var result = (int)await _db.ScriptEvaluateAsync(AcquireScript,
            new RedisKey[] { LockKey(accountId) },
            new RedisValue[] { serverGuid.ToString() });

        if (result == 1)
            await _db.SetAddAsync(ServerLocksKey(serverGuid), accountId);

        return result == 1;
    }

    public static async Task<Guid?> GetOwnerAsync(int accountId) {
        var value = await _db.StringGetAsync(LockKey(accountId));
        return value.IsNullOrEmpty ? null : Guid.Parse((string)value!);
    }

    public static async Task ReleaseAsync(int accountId) {
        await _db.KeyDeleteAsync(LockKey(accountId));
    }

    // Releases every account a given GameServer instance was holding - used on
    // clean disconnect (previously Accounts.UpdateMany(... LockOwner == ServerId)).
    public static async Task ReleaseAllForServerAsync(Guid serverGuid) {
        var setKey = ServerLocksKey(serverGuid);
        var accountIds = await _db.SetMembersAsync(setKey);
        foreach (var id in accountIds)
            await _db.KeyDeleteAsync(LockKey((int)id));
        await _db.KeyDeleteAsync(setKey);
    }

    // Cold-path startup sweep (mirrors the old ReleaseStaleLocksAsync): releases
    // every lock owned by a server GUID that isn't in the currently-connected set.
    // Returns how many stale servers' locks were released, for logging.
    public static async Task<int> ReleaseStaleLocksAsync(IReadOnlyCollection<Guid> liveServerIds) {
        var released = 0;
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        await foreach (var key in server.KeysAsync(pattern: "gameserver_locks:*")) {
            var guidPart = key.ToString().Substring("gameserver_locks:".Length);
            if (Guid.TryParse(guidPart, out var serverGuid) && !liveServerIds.Contains(serverGuid)) {
                await ReleaseAllForServerAsync(serverGuid);
                released++;
            }
        }
        return released;
    }
}
