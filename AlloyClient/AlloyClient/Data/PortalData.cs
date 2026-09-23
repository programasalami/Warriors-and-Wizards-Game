using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AlloyClient.Data;

// What the in-client Portal shows: the account server's public, read-only answers (/public/...), the same JSON the website reads.
// Every TryParse is forgiving: a missing field is a zero / empty value, and anything that is not the expected shape (an <Error>
// page from an older server, "not found", broken text) is "no data" with the server's sentence when it gave one.
public sealed record PortalCharacter(int Id, int Class, int Level, int Fame, int Exp, int[] Equipment, bool Backpack,
                                     int Hp, int Mp, int Att, int Def, int Spd, int Dex, int Vit, int Wis) {
    // The eight stats in the order the game shows them everywhere (HP MP ATT DEF SPD DEX VIT WIS).
    public int[] Stats => [Hp, Mp, Att, Def, Spd, Dex, Vit, Wis];
}

public sealed record PortalClassStat(int Class, int BestLevel, int BestFame);

public sealed record PortalProfile(string Name, int Rank, string RankName, int Stars, int Fame, int TotalFame, int BestCharFame, string Guild, int GuildRank,
                                   string Created, string LastSeen, bool Online, string World, IReadOnlyList<PortalCharacter> Characters, IReadOnlyList<PortalClassStat> ClassStats);

public sealed record PortalRow(int Rank, string Name, long Value, int Class, int Level, int Stars, int Members);

public sealed record PortalGuildMember(string Name, int GuildRank, int Stars, int Fame);

public sealed record PortalGuild(string Name, int Level, long Fame, long TotalFame, string Created, IReadOnlyList<PortalGuildMember> Members);

public sealed record PortalOnline(int Online, string Version, int[] StarGoals);

// The release history (/public/releases, 2026-09-22): one patch-notes entry each, newest first; Version = the game version it shipped in, or "".
public sealed record PortalRelease(string Date, string Version, string Title, IReadOnlyList<string> Lines);

public sealed record PortalReleases(string Version, IReadOnlyList<PortalRelease> Releases);

public static class PortalData {

    public const string NotFound = "not found";

    // The sentence in {"error": "..."} or null when the answer is not an error object.
    public static string ErrorOf(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) {
                return e.GetString();
            }
        } catch (Exception) {
            // not json at all
        }

        return null;
    }

    // /public/releases: { version, releases: [ { date, version, title, lines[] } ] } (2026-09-22)
    public static bool TryParseReleases(string json, out PortalReleases data) {
        data = null;
        try {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("releases", out var list) || list.ValueKind != JsonValueKind.Array) {
                return false;
            }

            var releases = new List<PortalRelease>();
            foreach (var e in list.EnumerateArray()) {
                var lines = new List<string>();
                if (e.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
                    foreach (var l in ls.EnumerateArray())
                        lines.Add(l.GetString() ?? "");
                releases.Add(new PortalRelease(Str(e, "date"), Str(e, "version"), Str(e, "title"), lines));
            }

            data = new PortalReleases(Str(r, "version"), releases);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static bool TryParseOnline(string json, out PortalOnline data) {
        data = null;
        try {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("online", out _)) {
                return false;
            }

            data = new PortalOnline(Int(r, "online"), Str(r, "version"), IntArray(r, "starGoals"));
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static bool TryParseProfile(string json, out PortalProfile data) {
        data = null;
        try {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("name", out _) || r.TryGetProperty("error", out _)) {
                return false;
            }

            var characters = new List<PortalCharacter>();
            if (r.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array) {
                foreach (var c in chars.EnumerateArray()) {
                    var gear = IntArray(c, "equipment");
                    if (gear.Length < 4) {
                        var padded = new[] { -1, -1, -1, -1 };
                        Array.Copy(gear, padded, gear.Length);
                        gear = padded;
                    }

                    characters.Add(new PortalCharacter(Int(c, "id"), Int(c, "class"), Int(c, "level"), Int(c, "fame"), Int(c, "exp"), gear, Bool(c, "backpack"),
                        Int(c, "hp"), Int(c, "mp"), Int(c, "att"), Int(c, "def"), Int(c, "spd"), Int(c, "dex"), Int(c, "vit"), Int(c, "wis")));
                }
            }

            var classStats = new List<PortalClassStat>();
            if (r.TryGetProperty("classStats", out var stats) && stats.ValueKind == JsonValueKind.Array) {
                foreach (var s in stats.EnumerateArray()) {
                    classStats.Add(new PortalClassStat(Int(s, "class"), Int(s, "bestLevel"), Int(s, "bestFame")));
                }
            }

            data = new PortalProfile(Str(r, "name"), Int(r, "rank"), Str(r, "rankName"), Int(r, "stars"), Int(r, "fame"), Int(r, "totalFame"), Int(r, "bestCharFame"),
                Str(r, "guild"), Int(r, "guildRank"), Str(r, "created"), Str(r, "lastSeen"), Bool(r, "online"), Str(r, "world"), characters, classStats);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static bool TryParseNames(string json, out List<string> names) {
        names = null;
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return false;
            }

            names = [];
            foreach (var e in doc.RootElement.EnumerateArray()) {
                if (e.ValueKind == JsonValueKind.String) {
                    names.Add(e.GetString());
                }
            }

            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static bool TryParseRows(string json, out List<PortalRow> rows) {
        rows = null;
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return false;
            }

            rows = [];
            foreach (var e in doc.RootElement.EnumerateArray()) {
                rows.Add(new PortalRow(Int(e, "rank"), Str(e, "name"), Long(e, "value"), Int(e, "class"), Int(e, "level"), Int(e, "stars"), Int(e, "members")));
            }

            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static bool TryParseGuild(string json, out PortalGuild data) {
        data = null;
        try {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("members", out var members) || r.TryGetProperty("error", out _)) {
                return false;
            }

            var list = new List<PortalGuildMember>();
            if (members.ValueKind == JsonValueKind.Array) {
                foreach (var m in members.EnumerateArray()) {
                    list.Add(new PortalGuildMember(Str(m, "name"), Int(m, "guildRank"), Int(m, "stars"), Int(m, "fame")));
                }
            }

            data = new PortalGuild(Str(r, "name"), Int(r, "level"), Long(r, "fame"), Long(r, "totalFame"), Str(r, "created"), list);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static string GuildRankName(int rank) => rank >= 40 ? "Founder" : rank >= 30 ? "Leader" : rank >= 20 ? "Officer" : rank >= 10 ? "Member" : "Initiate";

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int[] IntArray(JsonElement e, string name) {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var list = new List<int>();
        foreach (var x in v.EnumerateArray()) {
            list.Add(x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : 0);
        }

        return list.ToArray();
    }
}
