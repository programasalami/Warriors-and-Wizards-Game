using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Common.Database.Models;
using Common.Resources.Config;
using Dapper;

namespace Common.Database;

// Read-only, public views of the game's data for The Portal (the RealmEye-style site). Everything here is safe to show to anyone:
// no e-mails, IPs, gold, inbox, potions, or account ids. Profiles are public for every account (the user's decision, 2026-09-21);
// a per-account "hide my profile" flag is the planned opt-out. Answers are cached for a short time so a busy Portal never
// hammers the game's database.
public static class PublicDb {
    public const int CacheSeconds = 60;
    public const int LeaderboardSize = 100;
    public const int SearchSize = 10;

    // ---- what the API returns ---------------------------------------------------------------------------------------------
    public sealed class Profile {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("rankName")] public string RankName { get; set; } = "";
        [JsonPropertyName("stars")] public int Stars { get; set; }
        [JsonPropertyName("fame")] public int Fame { get; set; }                  // account fame (current)
        [JsonPropertyName("totalFame")] public int TotalFame { get; set; }
        [JsonPropertyName("bestCharFame")] public int BestCharFame { get; set; }
        [JsonPropertyName("guild")] public string? Guild { get; set; }
        [JsonPropertyName("guildRank")] public int GuildRank { get; set; }
        [JsonPropertyName("created")] public string Created { get; set; } = "";
        [JsonPropertyName("lastSeen")] public string LastSeen { get; set; } = "";
        [JsonPropertyName("online")] public bool Online { get; set; }
        [JsonPropertyName("world")] public string? World { get; set; }
        [JsonPropertyName("characters")] public List<PublicCharacter> Characters { get; set; } = [];
        [JsonPropertyName("classStats")] public List<PublicClassStat> ClassStats { get; set; } = [];
    }

    public sealed class PublicCharacter {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("class")] public int Class { get; set; }            // object type of the class (see classes.json on the site)
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("fame")] public int Fame { get; set; }
        [JsonPropertyName("exp")] public int Exp { get; set; }
        [JsonPropertyName("equipment")] public int[] Equipment { get; set; } = [];   // item types of the 4 gear slots (-1 = empty)
        [JsonPropertyName("backpack")] public bool Backpack { get; set; }
        [JsonPropertyName("hp")] public int Hp { get; set; }
        [JsonPropertyName("mp")] public int Mp { get; set; }
        [JsonPropertyName("att")] public int Att { get; set; }
        [JsonPropertyName("def")] public int Def { get; set; }
        [JsonPropertyName("spd")] public int Spd { get; set; }
        [JsonPropertyName("dex")] public int Dex { get; set; }
        [JsonPropertyName("vit")] public int Vit { get; set; }
        [JsonPropertyName("wis")] public int Wis { get; set; }
    }

    public sealed class PublicClassStat {
        [JsonPropertyName("class")] public int Class { get; set; }
        [JsonPropertyName("bestLevel")] public int BestLevel { get; set; }
        [JsonPropertyName("bestFame")] public int BestFame { get; set; }
    }

    public sealed class LeaderboardRow {
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("value")] public long Value { get; set; }
        [JsonPropertyName("class")] public int? Class { get; set; }
        [JsonPropertyName("level")] public int? Level { get; set; }
        [JsonPropertyName("stars")] public int? Stars { get; set; }
        [JsonPropertyName("members")] public int? Members { get; set; }
    }

    public sealed class PublicGuild {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("fame")] public long Fame { get; set; }
        [JsonPropertyName("totalFame")] public long TotalFame { get; set; }
        [JsonPropertyName("created")] public string Created { get; set; } = "";
        [JsonPropertyName("members")] public List<GuildMember> Members { get; set; } = [];
    }

    public sealed class GuildMember {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("guildRank")] public int GuildRank { get; set; }
        [JsonPropertyName("stars")] public int Stars { get; set; }
        [JsonPropertyName("fame")] public int Fame { get; set; }
    }

    // ---- pure shaping (unit tested) ---------------------------------------------------------------------------------------
    // Accounts made before 2026-09-22 have no creation date: their first character's creation date stands in (deleted ones count, they
    // were made then too). Last Seen: the newest of the saved value and the last sign-in.
    public static void FillDates(Account acc, DateTime? lastLogin) {
        if (acc.CreatedAt == default) {
            var first = (acc.Characters ?? []).Where(c => c != null && c.CreatedAt != default).Select(c => c.CreatedAt).DefaultIfEmpty().Min();
            if (first != default)
                acc.CreatedAt = first;
        }
        if (lastLogin.HasValue && lastLogin.Value > acc.LastSeenAt)
            acc.LastSeenAt = lastLogin.Value;
    }

    public static int Stars(IEnumerable<ClassStats>? classStats, int[]? goals) {
        if (classStats == null || goals == null || goals.Length == 0)
            return 0;
        var stars = 0;
        foreach (var cs in classStats)
            foreach (var goal in goals)
                if (cs.BestFame >= goal)
                    stars++;
        return stars;
    }

    public static Profile ToProfile(Account acc, int[]? starGoals, bool online = false, string? world = null) {
        var p = new Profile {
            Name = acc.Name ?? "",
            Rank = Ranks.Of(acc),
            RankName = Ranks.Name(Ranks.Of(acc)),
            Stars = Stars(acc.Stats?.ClassStats, starGoals),
            Fame = acc.Stats?.CurrentFame ?? 0,
            TotalFame = acc.Stats?.TotalFame ?? 0,
            BestCharFame = acc.Stats?.BestCharFame ?? 0,
            Guild = string.IsNullOrEmpty(acc.GuildName) ? null : acc.GuildName,
            GuildRank = acc.GuildRank,
            Created = Date(acc.CreatedAt),
            LastSeen = Date(acc.LastSeenAt),
            Online = online,
            World = world
        };
        foreach (var c in acc.Characters ?? [])
        {
            if (c == null || c.IsDeleted || c.ObjectType == 0)
                continue;
            var gear = new int[4];
            for (var i = 0; i < 4; i++)
                gear[i] = c.ItemTypes != null && i < c.ItemTypes.Length ? c.ItemTypes[i] : -1;
            p.Characters.Add(new PublicCharacter {
                Id = c.CharId, Class = c.ObjectType, Level = c.Level, Fame = c.CurrentFame, Exp = c.XpPoints,
                Equipment = gear, Backpack = c.HasBackpack,
                Hp = c.Stats?.MaxHp ?? 0, Mp = c.Stats?.MaxMp ?? 0, Att = c.Stats?.Attack ?? 0, Def = c.Stats?.Defense ?? 0,
                Spd = c.Stats?.Speed ?? 0, Dex = c.Stats?.Dexterity ?? 0, Vit = c.Stats?.Vitality ?? 0, Wis = c.Stats?.Wisdom ?? 0
            });
        }
        foreach (var cs in acc.Stats?.ClassStats ?? [])
            p.ClassStats.Add(new PublicClassStat { Class = cs.ObjectType, BestLevel = cs.BestLevel, BestFame = cs.BestFame });
        return p;
    }

    private static string Date(DateTime d) => d == default ? "" : d.ToUniversalTime().ToString("yyyy-MM-dd");

    // ---- cache ----------------------------------------------------------------------------------------------------------------
    private static readonly ConcurrentDictionary<string, (DateTime At, string Json)> _cache = new();

    public static async Task<string> CachedAsync(string key, Func<Task<string>> produce) {
        if (_cache.TryGetValue(key, out var hit) && (DateTime.UtcNow - hit.At).TotalSeconds < CacheSeconds)
            return hit.Json;
        var json = await produce();
        _cache[key] = (DateTime.UtcNow, json);
        if (_cache.Count > 5000)
            foreach (var old in _cache.Where(kv => (DateTime.UtcNow - kv.Value.At).TotalSeconds > CacheSeconds).Select(kv => kv.Key).ToList())
                _cache.TryRemove(old, out _);
        return json;
    }

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    public static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // ---- queries ------------------------------------------------------------------------------------------------------------
    public static async Task<Account?> LoadAccountByNameAsync(string name) {
        await using var conn = await DbClient.OpenConnectionAsync();
        // Last Seen = the last sign-in the logins table records (kept for every account); Account.LastSeenAt was never written.
        var row = await conn.QueryFirstOrDefaultAsync<(int Id, string Name, string Data)>(
            "SELECT id AS Id, name AS Name, data::text AS Data FROM accounts WHERE lower(name)=lower(@Name)", new { Name = name });
        if (row.Data == null)
            return null;
        var acc = JsonSerializer.Deserialize<Account>(row.Data);
        if (acc != null) {
            acc.Id = row.Id;
            acc.Name = row.Name;
            var lastLogin = await conn.ExecuteScalarAsync<DateTime?>("SELECT last_login_at FROM logins WHERE lower(name) = lower(@Name) LIMIT 1", new { Name = row.Name });
            FillDates(acc, lastLogin);
        }
        return acc;
    }

    // Player search (2026-09-22): 1-2 letters = names that START with them; 3 or more = names that CONTAIN them anywhere, ranked: the exact
    // name first, then names starting with the text, then the rest, shorter names before longer. No guessing: only real matches.
    public const int ContainsFrom = 3;

    public static (string Pattern, string Prefix) SearchPatterns(string query) {
        var q = (query ?? "").Trim().Replace("\\", "").Replace("%", "").Replace("_", "");
        return (q.Length >= ContainsFrom ? "%" + q + "%" : q + "%", q + "%");
    }

    public static async Task<List<string>> SearchNamesAsync(string query) {
        var (pattern, prefix) = SearchPatterns(query);
        if (pattern.Trim('%').Length == 0)
            return [];
        await using var conn = await DbClient.OpenConnectionAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT name FROM accounts WHERE name ILIKE @P " +
            "ORDER BY (lower(name) = lower(@Q)) DESC, (name ILIKE @Pre) DESC, length(name), name LIMIT @N",
            new { P = pattern, Pre = prefix, Q = (query ?? "").Trim(), N = SearchSize });
        return rows.ToList();
    }

    // kind: fame (account total fame), chars (best characters by fame), level (best characters by level), guilds
    public static async Task<List<LeaderboardRow>> LeaderboardAsync(string kind, int[]? starGoals) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var list = new List<LeaderboardRow>();
        switch (kind) {
            case "fame": {
                var rows = await conn.QueryAsync<(string Name, long Value, string Data)>(
                    "SELECT name AS Name, COALESCE((data->'Stats'->>'TotalFame')::bigint, 0) AS Value, data->'Stats'->'ClassStats' AS Data " +
                    "FROM accounts ORDER BY Value DESC, name LIMIT @N", new { N = LeaderboardSize });
                var i = 0;
                foreach (var r in rows) {
                    var cs = r.Data == null ? null : JsonSerializer.Deserialize<List<ClassStats>>(r.Data);
                    list.Add(new LeaderboardRow { Rank = ++i, Name = r.Name, Value = r.Value, Stars = Stars(cs, starGoals) });
                }
                break;
            }
            case "chars":
            case "level": {
                var order = kind == "chars" ? "Fame" : "Level";
                var rows = await conn.QueryAsync<(string Name, int Class, int Level, int Fame, int Exp)>(
                    "SELECT a.name AS Name, (c->>'ObjectType')::int AS Class, (c->>'Level')::int AS Level, COALESCE((c->>'CurrentFame')::int,0) AS Fame, COALESCE((c->>'XpPoints')::int,0) AS Exp " +
                    "FROM accounts a, jsonb_array_elements(a.data->'Characters') c " +
                    "WHERE COALESCE((c->>'IsDeleted')::boolean, false) = false AND (c->>'ObjectType')::int > 0 " +
                    $"ORDER BY {order} DESC, Exp DESC, a.name LIMIT @N", new { N = LeaderboardSize });
                var i = 0;
                foreach (var r in rows)
                    list.Add(new LeaderboardRow { Rank = ++i, Name = r.Name, Value = kind == "chars" ? r.Fame : r.Level, Class = r.Class, Level = r.Level });
                break;
            }
            case "guilds": {
                var rows = await conn.QueryAsync<(string Name, long Value, int Members)>(
                    "SELECT g.name AS Name, g.total_fame AS Value, (SELECT count(*) FROM accounts a WHERE a.guild_id = g.id) AS Members " +
                    "FROM guilds g ORDER BY Value DESC, g.name LIMIT @N", new { N = LeaderboardSize });
                var i = 0;
                foreach (var r in rows)
                    list.Add(new LeaderboardRow { Rank = ++i, Name = r.Name, Value = r.Value, Members = r.Members });
                break;
            }
        }
        return list;
    }

    public static async Task<PublicGuild?> LoadGuildAsync(string name, int[]? starGoals) {
        await using var conn = await DbClient.OpenConnectionAsync();
        var g = await conn.QueryFirstOrDefaultAsync<(int Id, string Name, short Level, long CurrentFame, long TotalFame, DateTime CreatedAt)>(
            "SELECT id AS Id, name AS Name, level AS Level, current_fame AS CurrentFame, total_fame AS TotalFame, created_at AS CreatedAt FROM guilds WHERE lower(name)=lower(@Name)", new { Name = name });
        if (g.Name == null)
            return null;
        var guild = new PublicGuild { Name = g.Name, Level = g.Level, Fame = g.CurrentFame, TotalFame = g.TotalFame, Created = Date(g.CreatedAt) };
        var members = await conn.QueryAsync<(string Name, string Data)>(
            "SELECT name AS Name, data::text AS Data FROM accounts WHERE guild_id=@Id ORDER BY name", new { g.Id });
        foreach (var m in members) {
            var acc = JsonSerializer.Deserialize<Account>(m.Data);
            guild.Members.Add(new GuildMember { Name = m.Name, GuildRank = acc?.GuildRank ?? 0, Stars = Stars(acc?.Stats?.ClassStats, starGoals), Fame = acc?.Stats?.CurrentFame ?? 0 });
        }
        guild.Members = guild.Members.OrderByDescending(x => x.GuildRank).ThenBy(x => x.Name).ToList();
        return guild;
    }
}
