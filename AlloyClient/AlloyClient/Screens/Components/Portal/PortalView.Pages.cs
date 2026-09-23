using System;
using System.Collections.Generic;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Screens.Components.Portal;

// The live pages: Home, a player's profile, a guild, and the leaderboards. Everything here comes from the account server's public API.
public sealed partial class PortalView {

    private const int TableRowH = 56;
    private const int ListRowH = 32;

    private static readonly (string Kind, string Title, string Tab)[] Boards = [
        ("fame", "TOP PLAYERS BY FAME", "PLAYERS"), ("chars", "TOP CHARACTERS BY FAME", "CHARACTERS"), ("level", "TOP CHARACTERS BY LEVEL", "LEVEL"), ("guilds", "TOP GUILDS BY FAME", "GUILDS")
    ];

    #region Home

    private void BuildHomePage() {
        // No headline on the home page (by request): just the online count on the left, MY PROFILE on the right, then the three boards.
        var onlineLine = _online != null ? $"{N(_online.Online)} online   -   game version {_online.Version}" : _onlineError != null ? "The game servers are not answering right now." : "Counting players...";
        var y = ContentTop + 12;
        _content.AddChild(Text(onlineLine, SmallSize + 1, ContentLeft, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        var account = GlobalData.Contains<LoginData>() ? GlobalData.Get<AccountData>() : null;
        _content.AddChild(BuildTextButton("RELEASE HISTORY", SmallSize, ContentRight, y, UiAnchor.MiddleRight, () => Go(Page.Releases)));
        if (account != null && !string.IsNullOrEmpty(account.Name)) {
            _content.AddChild(BuildTextButton("MY PROFILE", SmallSize, ContentRight - MeasureWidth("RELEASE HISTORY", SmallSize) - 36, y, UiAnchor.MiddleRight, () => OpenPlayer(account.Name)));
        }

        _content.AddChild(Rule(ContentCenterX, ContentTop + 44, ContentW));

        // Three columns: the top ten of each board, with a link to the full list.
        const int columns = 3;
        const int gap = 24;
        var colW = (ContentW - gap * (columns - 1)) / columns;
        var top = ContentTop + 66;
        string[] kinds = ["fame", "chars", "guilds"];
        string[] titles = ["TOP PLAYERS", "TOP CHARACTERS", "TOP GUILDS"];
        for (var c = 0; c < columns; c++) {
            var x = ContentLeft + c * (colW + gap);
            var kind = kinds[c];
            _content.AddChild(Text(titles[c], BodySize, x, top, UiAnchor.LeftTop));
            _content.AddChild(Rule(x + colW / 2, top + 32, colW));
            _content.AddChild(BuildTextButton("FULL LIST >", SmallSize - 2, x + colW, top + 8, UiAnchor.RightTop, () => Go(Page.Top, kind), InkSoft));

            var rows = Board(kind, out var error);
            if (rows == null) {
                _content.AddChild(Text(error ?? "Loading...", SmallSize, x + colW / 2, top + 80, UiAnchor.Middle, color: InkSoft, outline: 1, maxWidth: colW - 20));
                continue;
            }

            if (rows.Count == 0) {
                _content.AddChild(Text("Nobody yet.", SmallSize, x + colW / 2, top + 80, UiAnchor.Middle, color: InkSoft, outline: 1));
                continue;
            }

            var rowY = top + 44;
            for (var i = 0; i < Math.Min(10, rows.Count); i++) {
                _content.AddChild(HomeRow(rows[i], kind, x, rowY, colW));
                rowY += ListRowH;
            }
        }
    }

    private Container HomeRow(PortalRow r, string kind, int x, int y, int width) {
        var isGuild = kind == "guilds";
        var row = BuildRowButton(x, y, width, ListRowH - 3, () => { if (isGuild) Go(Page.Guild, r.Name); else OpenPlayer(r.Name); }, 0.05f);
        var cy = (ListRowH - 3) / 2;
        row.AddChild(Text(r.Rank.ToString(), SmallSize, 8, cy, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        row.AddChild(Text(r.Name, SmallSize + 1, 40, cy, UiAnchor.MiddleLeft, outline: 1, maxWidth: width - 200));
        var detail = isGuild ? Plural(r.Members, "member") : kind == "chars" ? $"{ClassName(r.Class)} {r.Level}" : $"{r.Stars}/{MaxStars()} stars";
        row.AddChild(Text(detail, SmallSize - 3, width - 84, cy, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
        row.AddChild(Text(N(r.Value), SmallSize, width - 8, cy, UiAnchor.MiddleRight, outline: 1));
        return row;
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    #endregion

    #region Data fetching (cached for the life of the screen)

    private readonly HashSet<string> _inFlight = new();
    private readonly Dictionary<string, string> _errors = new();

    // A leaderboard, from the cache or requested now (null while loading / after a failure, with the reason in error).
    private List<PortalRow> Board(string kind, out string error) {
        error = null;
        if (_boards.TryGetValue(kind, out var rows)) {
            return rows;
        }

        var key = "board:" + kind;
        if (_errors.TryGetValue(key, out error)) {
            return null;
        }

        Fetch(key, () => PortalRequests.GetLeaderboard(kind), r => { if (r.Data != null) _boards[kind] = r.Data; });
        return null;
    }

    private PortalProfile Profile(string name, out string error) {
        error = null;
        if (_profiles.TryGetValue(name, out var profile)) {
            return profile;
        }

        var key = "player:" + name.ToLowerInvariant();
        if (_errors.TryGetValue(key, out error)) {
            return null;
        }

        Fetch(key, () => PortalRequests.GetPlayer(name), r => { if (r.Data != null) _profiles[name] = r.Data; });
        return null;
    }

    private PortalGuild Guild(string name, out string error) {
        error = null;
        if (_guilds.TryGetValue(name, out var guild)) {
            return guild;
        }

        var key = "guild:" + name.ToLowerInvariant();
        if (_errors.TryGetValue(key, out error)) {
            return null;
        }

        Fetch(key, () => PortalRequests.GetGuild(name), r => { if (r.Data != null) _guilds[name] = r.Data; });
        return null;
    }

    // Runs one request per key at a time; the answer is stored whatever page is open by then, and the page is redrawn if it is still the same one.
    private void Fetch<T>(string key, Func<System.Threading.Tasks.Task<PortalRequests.Result<T>>> request, Action<PortalRequests.Result<T>> store) where T : class {
        if (!_inFlight.Add(key)) {
            return;
        }

        var route = _route;
        _ = RunAsync(request, result => {
            _inFlight.Remove(key);
            if (result.Data == null) {
                _errors[key] = result.Error ?? PortalRequests.Offline;
            } else {
                _errors.Remove(key);
                store(result);
            }

            if (_route == route) {
                Render();
            }
        });
    }

    private void Forget(string key) {
        _errors.Remove(key);
        if (key.StartsWith("board:", StringComparison.Ordinal)) {
            _boards.Remove(key[6..]);
        } else if (key.StartsWith("player:", StringComparison.Ordinal)) {
            _profiles.Remove(key[7..]);
        } else if (key.StartsWith("guild:", StringComparison.Ordinal)) {
            _guilds.Remove(key[6..]);
        }
    }

    #endregion

    #region Player

    private void BuildPlayerPage(string name) {
        var p = Profile(name, out var error);
        if (p == null) {
            PageTitle(name.ToUpperInvariant());
            if (error == null) {
                Notice($"Looking up {name}...");
            } else if (error == PortalData.NotFound) {
                Notice($"No player called \"{name}\" exists.");
            } else {
                Notice(error, color: Bad);
                _content.AddChild(BuildScrollButton("TRY AGAIN", ContentCenterX, ContentTop + 180, 190, 44, () => { Forget("player:" + name.ToLowerInvariant()); Render(); }));
            }

            return;
        }

        var presence = p.Online ? "online now" + (string.IsNullOrEmpty(p.World) ? "" : " in " + p.World) : "last seen " + (string.IsNullOrEmpty(p.LastSeen) ? "never" : p.LastSeen);
        PageTitle(p.Name.ToUpperInvariant() + (p.Rank >= 80 ? "   [" + p.RankName.ToUpperInvariant() + "]" : ""), presence);
        _content.AddChild(RefreshLink(() => { Forget("player:" + name.ToLowerInvariant()); Forget("player:" + p.Name.ToLowerInvariant()); Render(); }));
        if (p.Online) {
            _content.AddChild(Box(ContentRight - MeasureWidth(presence, SmallSize) - 18, ContentTop + 15, 10, 10, 1f, Good));
        }

        // Account facts: three rows of two label / value pairs on the left, the class records on the right.
        var left = ContentLeft;
        var y0 = ContentTop + 66;
        const int rowStep = 26;
        const int pairW = 300;
        (string Label, string Value)[] facts = [
            ("CHARACTERS", p.Characters.Count.ToString()), ("RANK", $"{p.Stars}/{MaxStars()} stars"),
            ("FAME", $"{N(p.Fame)}  ({N(p.TotalFame)} total)"), ("BEST CHARACTER", $"{N(p.BestCharFame)} fame"),
            ("CREATED", string.IsNullOrEmpty(p.Created) ? "?" : p.Created), ("GUILD", null)
        ];
        for (var i = 0; i < facts.Length; i++) {
            var x = left + (i % 2) * pairW;
            var y = y0 + (i / 2) * rowStep;
            _content.AddChild(Text(facts[i].Label, SmallSize - 3, x, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            if (facts[i].Label == "GUILD") {
                if (string.IsNullOrEmpty(p.Guild)) {
                    _content.AddChild(Text("none", SmallSize, x + 118, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
                } else {
                    var guild = p.Guild;
                    _content.AddChild(BuildTextButton(guild, SmallSize, x + 118, y, UiAnchor.MiddleLeft, () => Go(Page.Guild, guild), maxWidth: 130));
                    _content.AddChild(Text(PortalData.GuildRankName(p.GuildRank), SmallSize - 4, x + pairW - 10, y, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
                }
            } else {
                _content.AddChild(Text(facts[i].Value, SmallSize, x + 118, y, UiAnchor.MiddleLeft, outline: 1));
            }
        }

        var recordsX = ContentLeft + 640;
        _content.AddChild(Text("CLASS RECORDS", SmallSize - 3, recordsX, y0, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        if (p.ClassStats.Count == 0) {
            _content.AddChild(Text("none yet", SmallSize, recordsX + 130, y0, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        }

        var goals = _online?.StarGoals ?? [];
        for (var i = 0; i < Math.Min(3, p.ClassStats.Count); i++) {
            var cs = p.ClassStats[i];
            var y = y0 + i * rowStep;
            var stars = 0;
            foreach (var g in goals) {
                if (cs.BestFame >= g) {
                    stars++;
                }
            }

            var classType = cs.Class;
            _content.AddChild(BuildTextButton(ClassName(classType), SmallSize, recordsX + 130, y, UiAnchor.MiddleLeft, () => Go(Page.Class, classType.ToString())));
            _content.AddChild(Text($"LV {cs.BestLevel}   {N(cs.BestFame)} fame   {stars}/{goals.Length} stars", SmallSize - 2, recordsX + 230, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        }

        // The characters table.
        var tableTop = y0 + 3 * rowStep + 14;
        _content.AddChild(Rule(ContentCenterX, tableTop, ContentW));
        var headY = tableTop + 14;
        _content.AddChild(Text("CHARACTER", SmallSize - 4, ContentLeft + 56, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("LVL", SmallSize - 4, ContentLeft + 236, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("FAME", SmallSize - 4, ContentLeft + 296, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("EXP", SmallSize - 4, ContentLeft + 386, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("EQUIPMENT", SmallSize - 4, ContentLeft + 484, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("STATS (gold = maxed)", SmallSize - 4, ContentLeft + 664, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("MAXED", SmallSize - 4, ContentRight - 4, headY, UiAnchor.MiddleRight, color: InkSoft, outline: 1));

        if (p.Characters.Count == 0) {
            _content.AddChild(Text("No living characters.", BodySize, ContentCenterX, headY + 70, UiAnchor.Middle, color: InkSoft, outline: 1));
            return;
        }

        var rowsTop = headY + 16;
        var perPage = Math.Max(1, (ContentBottom - 28 - rowsTop) / TableRowH);
        var (first, count) = Pager(p.Characters.Count, perPage);
        for (var i = 0; i < count; i++) {
            _content.AddChild(CharacterRow(p.Characters[first + i], ContentLeft, rowsTop + i * TableRowH));
        }
    }

    private Container CharacterRow(PortalCharacter c, int x, int y) {
        var h = TableRowH - 4;
        var cy = h / 2;
        var classType = c.Class;
        var row = BuildRowButton(x, y, ContentW, h, null, 0.06f);
        var portrait = Portrait(classType, 26, cy, 46);
        if (portrait != null) {
            row.AddChild(portrait);
        }

        row.AddChild(BuildTextButton(ClassName(classType), SmallSize + 1, 56, cy - (c.Backpack ? 8 : 0), UiAnchor.MiddleLeft, () => Go(Page.Class, classType.ToString()), maxWidth: 170));
        if (c.Backpack) {
            row.AddChild(Text("with backpack", SmallSize - 5, 56, cy + 10, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        }

        row.AddChild(Text(c.Level.ToString(), SmallSize, 236, cy, UiAnchor.MiddleLeft, outline: 1));
        row.AddChild(Text(N(c.Fame), SmallSize, 296, cy, UiAnchor.MiddleLeft, outline: 1));
        row.AddChild(Text(N(c.Exp), SmallSize, 386, cy, UiAnchor.MiddleLeft, outline: 1));

        // The four gear slots (an empty slot is a dashed-looking dark square); each item is a link to its wiki page.
        var slotNames = ObjectLibraryHelper.ClassSlots(classType);
        for (var s = 0; s < 4; s++) {
            var sx = 484 + s * 44;
            var type = s < c.Equipment.Length ? c.Equipment[s] : -1;
            var slot = new Container { X = sx, Y = cy - 20 };
            slot.MouseEnabled = type > 0;
            slot.AddChild(Box(0, 0, 40, 40, type > 0 ? 0.18f : 0.08f));
            if (type > 0) {
                slot.AddChild(ItemIcon(type, 20, 20, 36));
                var frame = Box(0, 0, 40, 40, 0f, Hover);
                slot.AddChild(frame);
                var down = false;
                var itemType = type;
                slot.AddEventListener(MouseEvent.MouseOver, () => frame.Alpha = 0.25f);
                slot.AddEventListener(MouseEvent.MouseOut, () => frame.Alpha = 0f);
                slot.AddEventListener(MouseEvent.LeftDown, () => down = true);
                slot.AddEventListener(MouseEvent.LeftUp, () => { if (down) Go(Page.Item, itemType.ToString()); down = false; });
            } else if (s < slotNames.Count) {
                slot.AddChild(Text(Abbrev(SlotName(slotNames[s])), SmallSize - 6, 20, 20, UiAnchor.Middle, color: InkSoft, outline: 1));
            }

            row.AddChild(slot);
        }

        // Eight stat chips; a maxed one is filled gold.
        var max = ClassMaxStats(classType);
        var stats = c.Stats;
        var maxed = 0;
        for (var i = 0; i < 8; i++) {
            var chipX = 664 + i * 54;
            var isMax = max != null && max[i] > 0 && stats[i] >= max[i];
            if (isMax) {
                maxed++;
            }

            row.AddChild(Box(chipX, cy - 19, 50, 38, isMax ? 0.85f : 0.1f, isMax ? Gold : ChipColor));
            row.AddChild(Text(StatLabels[i], SmallSize - 7, chipX + 25, cy - 9, UiAnchor.Middle, color: isMax ? Ink : InkSoft, outline: 1));
            row.AddChild(Text(stats[i].ToString(), SmallSize - 1, chipX + 25, cy + 7, UiAnchor.Middle, color: Ink, outline: 1));
        }

        row.AddChild(Text($"{maxed}/8", SmallSize + 1, ContentW - 4, cy, UiAnchor.MiddleRight, color: maxed == 8 ? Gold : Ink, outline: 1));
        return row;
    }

    private static string Abbrev(string slotName) => slotName.Length <= 5 ? slotName.ToUpperInvariant() : slotName[..4].ToUpperInvariant();

    #endregion

    #region Guild

    private void BuildGuildPage(string name) {
        var g = Guild(name, out var error);
        if (g == null) {
            PageTitle(name.ToUpperInvariant());
            if (error == null) {
                Notice($"Looking up {name}...");
            } else if (error == PortalData.NotFound) {
                Notice($"No guild called \"{name}\" exists.");
            } else {
                Notice(error, color: Bad);
                _content.AddChild(BuildScrollButton("TRY AGAIN", ContentCenterX, ContentTop + 180, 190, 44, () => { Forget("guild:" + name.ToLowerInvariant()); Render(); }));
            }

            return;
        }

        PageTitle(g.Name.ToUpperInvariant(), string.IsNullOrEmpty(g.Created) ? null : "founded " + g.Created);
        _content.AddChild(RefreshLink(() => { Forget("guild:" + name.ToLowerInvariant()); Render(); }));

        var y = ContentTop + 66;
        (string Label, string Value)[] facts = [("MEMBERS", g.Members.Count.ToString()), ("LEVEL", g.Level.ToString()), ("FAME", $"{N(g.Fame)}  ({N(g.TotalFame)} total)")];
        for (var i = 0; i < facts.Length; i++) {
            var x = ContentLeft + i * 300;
            _content.AddChild(Text(facts[i].Label, SmallSize - 3, x, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            _content.AddChild(Text(facts[i].Value, SmallSize, x + 90, y, UiAnchor.MiddleLeft, outline: 1));
        }

        var tableTop = y + 24;
        _content.AddChild(Rule(ContentCenterX, tableTop, ContentW));
        var headY = tableTop + 14;
        _content.AddChild(Text("NAME", SmallSize - 4, ContentLeft + 8, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("GUILD RANK", SmallSize - 4, ContentLeft + 360, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("STARS", SmallSize - 4, ContentLeft + 600, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("FAME", SmallSize - 4, ContentRight - 8, headY, UiAnchor.MiddleRight, color: InkSoft, outline: 1));

        if (g.Members.Count == 0) {
            Notice("No members.", headY + 70);
            return;
        }

        var rowsTop = headY + 16;
        var perPage = Math.Max(1, (ContentBottom - 28 - rowsTop) / ListRowH);
        var (first, count) = Pager(g.Members.Count, perPage);
        for (var i = 0; i < count; i++) {
            var m = g.Members[first + i];
            var row = BuildRowButton(ContentLeft, rowsTop + i * ListRowH, ContentW, ListRowH - 3, () => OpenPlayer(m.Name), 0.05f);
            var cy = (ListRowH - 3) / 2;
            row.AddChild(Text(m.Name, SmallSize + 1, 8, cy, UiAnchor.MiddleLeft, outline: 1));
            row.AddChild(Text(PortalData.GuildRankName(m.GuildRank), SmallSize, 360, cy, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            row.AddChild(Text($"{m.Stars}/{MaxStars()}", SmallSize, 600, cy, UiAnchor.MiddleLeft, outline: 1));
            row.AddChild(Text(N(m.Fame), SmallSize, ContentW - 8, cy, UiAnchor.MiddleRight, outline: 1));
            _content.AddChild(row);
        }
    }

    #endregion

    #region Leaderboards

    private void BuildTopPage(string kind) {
        var board = Array.Find(Boards, b => b.Kind == kind);
        if (board.Kind == null) {
            board = Boards[0];
            kind = board.Kind;
        }

        PageTitle(board.Title);
        _content.AddChild(RefreshLink(() => { Forget("board:" + kind); Render(); }));

        // Sub-tabs.
        var tabY = ContentTop + 66;
        var x = ContentLeft;
        foreach (var b in Boards) {
            var current = b.Kind == kind;
            var target = b.Kind;
            _content.AddChild(BuildTextButton(b.Tab, SmallSize, x, tabY, UiAnchor.MiddleLeft, () => Go(Page.Top, target), current ? Hover : Ink));
            var w = MeasureWidth(b.Tab, SmallSize);
            if (current) {
                _content.AddChild(new ColorRect(new ColorRectConfig { X = x, Y = tabY + 12, Width = w, Height = 2, Color = Hover, Alpha = 0.9f }));
            }

            x += w + 26;
        }

        var rows = Board(kind, out var error);
        var tableTop = tabY + 24;
        _content.AddChild(Rule(ContentCenterX, tableTop, ContentW));
        if (rows == null) {
            if (error == null) {
                Notice("Loading...", tableTop + 80);
            } else {
                Notice(error, tableTop + 80, Bad);
                _content.AddChild(BuildScrollButton("TRY AGAIN", ContentCenterX, tableTop + 140, 190, 44, () => { Forget("board:" + kind); Render(); }));
            }

            return;
        }

        if (rows.Count == 0) {
            Notice("Nobody yet.", tableTop + 80);
            return;
        }

        var isGuilds = kind == "guilds";
        var headY = tableTop + 14;
        _content.AddChild(Text("#", SmallSize - 4, ContentLeft + 8, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text(isGuilds ? "GUILD" : "PLAYER", SmallSize - 4, ContentLeft + 60, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text(isGuilds ? "MEMBERS" : kind == "fame" ? "STARS" : "CLASS", SmallSize - 4, ContentLeft + 460, headY, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text(kind == "level" ? "LEVEL" : isGuilds ? "TOTAL FAME" : "FAME", SmallSize - 4, ContentRight - 8, headY, UiAnchor.MiddleRight, color: InkSoft, outline: 1));

        var rowsTop = headY + 16;
        var perPage = Math.Max(1, (ContentBottom - 28 - rowsTop) / ListRowH);
        var (first, count) = Pager(rows.Count, perPage);
        for (var i = 0; i < count; i++) {
            var r = rows[first + i];
            var row = BuildRowButton(ContentLeft, rowsTop + i * ListRowH, ContentW, ListRowH - 3, () => { if (isGuilds) Go(Page.Guild, r.Name); else OpenPlayer(r.Name); }, 0.05f);
            var cy = (ListRowH - 3) / 2;
            row.AddChild(Text(r.Rank.ToString(), SmallSize, 8, cy, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            row.AddChild(Text(r.Name, SmallSize + 1, 60, cy, UiAnchor.MiddleLeft, outline: 1, maxWidth: 380));
            var middle = isGuilds ? Plural(r.Members, "member") : kind == "fame" ? $"{r.Stars}/{MaxStars()}" : $"{ClassName(r.Class)}" + (kind == "chars" ? $"   lvl {r.Level}" : "");
            row.AddChild(Text(middle, SmallSize, 460, cy, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            row.AddChild(Text(N(r.Value), SmallSize, ContentW - 8, cy, UiAnchor.MiddleRight, outline: 1));
            _content.AddChild(row);
        }
    }

    #endregion
}
