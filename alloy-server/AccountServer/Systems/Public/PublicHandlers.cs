using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using AccountServer.Messaging;
using Common.Messaging;
using Common.Database;
using Common.Resources.Config;

namespace AccountServer.Systems.Public;

// The Portal's read-only API (2026-09-21). Every answer is JSON, cached for PublicDb.CacheSeconds, and rate-limited per address.
// Nothing here needs a login and nothing here reveals anything private (see PublicDb). Reachable through the Portal's nginx as
// https://portal.<domain>/api/public/... ; GET with a query string or POST with a form both work (Program merges them).
public static class PublicApi {
    public static readonly AttemptLimiter PerIp = new(240, TimeSpan.FromMinutes(1));   // requests per address per minute

    public const string Blocked = "{\"error\":\"too many requests\"}";
    public const string NotFound = "{\"error\":\"not found\"}";

    public static bool Throttle(string ip) => PerIp.Record(ip);

    public static int[]? StarGoals => GameConfig.Config?.StarGoals;

    // Where the player is right now, if any connected game server knows them (null = offline / unknown).
    public static async Task<(bool Online, string? World)> PresenceAsync(string name, int accountId) {
        foreach (var proxy in IpcServer.Clients.Values) {
            try {
                var info = await proxy.GetUserInfo(name, accountId);
                if (info.HasValue)
                    return (true, info.Value.WorldName);
            }
            catch (Exception) { }
        }
        return (false, null);
    }

    public static async Task<int> OnlineCountAsync() {
        var total = 0;
        foreach (var proxy in IpcServer.Clients.Values) {
            try { total += (await proxy.GetGameServer()).PlayerCount; }
            catch (Exception) { }
        }
        return total;
    }

    public static string Clean(string? value, int max = 32) {
        value = (value ?? "").Trim();
        return value.Length > max ? value[..max] : value;
    }
}

public class PublicPlayer : RequestHandler {
    public override string Path => "/public/player";
    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return PublicApi.Blocked;
        var name = PublicApi.Clean(query["name"]);
        if (name.Length == 0) return PublicApi.NotFound;
        return await PublicDb.CachedAsync("player:" + name.ToLowerInvariant(), async () => {
            var acc = await PublicDb.LoadAccountByNameAsync(name);
            if (acc == null) return PublicApi.NotFound;
            var (online, world) = await PublicApi.PresenceAsync(acc.Name, acc.Id);
            return PublicDb.Json(PublicDb.ToProfile(acc, PublicApi.StarGoals, online, world));
        });
    }
}

public class PublicSearch : RequestHandler {
    public override string Path => "/public/search";
    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return PublicApi.Blocked;
        var q = PublicApi.Clean(query["q"], 16);
        if (q.Length == 0) return "[]";
        return await PublicDb.CachedAsync("search:" + q.ToLowerInvariant(), async () => PublicDb.Json(await PublicDb.SearchNamesAsync(q)));
    }
}

public class PublicLeaderboard : RequestHandler {
    private static readonly HashSet<string> Kinds = ["fame", "chars", "level", "guilds"];
    public override string Path => "/public/leaderboard";
    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return PublicApi.Blocked;
        var kind = PublicApi.Clean(query["kind"], 16).ToLowerInvariant();
        if (!Kinds.Contains(kind)) return PublicApi.NotFound;
        return await PublicDb.CachedAsync("board:" + kind, async () => PublicDb.Json(await PublicDb.LeaderboardAsync(kind, PublicApi.StarGoals)));
    }
}

public class PublicGuildHandler : RequestHandler {
    public override string Path => "/public/guild";
    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return PublicApi.Blocked;
        var name = PublicApi.Clean(query["name"]);
        if (name.Length == 0) return PublicApi.NotFound;
        return await PublicDb.CachedAsync("guild:" + name.ToLowerInvariant(), async () => {
            var guild = await PublicDb.LoadGuildAsync(name, PublicApi.StarGoals);
            return guild == null ? PublicApi.NotFound : PublicDb.Json(guild);
        });
    }
}

// The release history (2026-09-22): the patch notes file, newest first, with the game version each entry shipped in (when it names one) and the
// version the server runs now. Same source as the News Board in the Nexus; read by The Portal website (/releases.html) and the in-game Portal.
public class PublicReleases : RequestHandler {
    public override string Path => "/public/releases";
    public override Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return Task.FromResult(PublicApi.Blocked);
        var releases = Common.News.PatchNotes.Load().Select(n => new { date = n.Date, version = n.Version, title = n.Title, lines = n.Lines }).ToArray();
        return Task.FromResult(PublicDb.Json(new { version = GameServerConfig.Config?.Version, releases }));
    }
}

public class PublicOnline : RequestHandler {
    public override string Path => "/public/online";
    public override async Task<string> Handle(string ip, NameValueCollection query) {
        if (PublicApi.Throttle(ip)) return PublicApi.Blocked;
        return await PublicDb.CachedAsync("online", async () => PublicDb.Json(new { online = await PublicApi.OnlineCountAsync(), version = GameServerConfig.Config?.Version, starGoals = PublicApi.StarGoals ?? [] }));
    }
}
