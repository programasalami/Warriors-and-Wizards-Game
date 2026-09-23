using System;
using OpenTK.Platform;
using System.Collections.Generic;
using System.Linq;
using AlloyClient.Assets.Libraries;
using AlloyClient.Assets.XmlStructs;
using AlloyClient.Data;
using AlloyClient.Game.Components.Hud;
using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Objects;
using AlloyClient.Networking;
using AlloyClient.Networking.Packets.Outgoing;
using AlloyClient.Ui;
using AlloyClient.Ui.Components.Panels;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Data;
using Alloy.UiLib.Extra;

namespace AlloyClient.Game.Components.Admin;

// The admin dashboard (the ADMIN tab, moderators and owners only): three pages -
//   PLAYERS   who is in this world, and kick / mute / ban / unmute / unban / find with a duration and a reason
//   LIBRARY   every item and every entity the game knows, searchable, marked NEW art or ORIGINAL art; owners can give an item or spawn an entity from here
//   SERVER    your rank, the world, and a cheat sheet of what each rank may type
// Every button just types the matching chat command for you (see AdminRules), so the server's own rank checks apply exactly as if you typed it: showing a
// button never grants anything. What the server answers is shown at the bottom (AdminReplies).
public sealed class AdminDashboardView : Overlay {

    // The panel is a FIXED size (1280x720 design canvas); nothing here is sized from its content.
    private const int PanelW = 940;
    private const int PanelH = 680;
    private const int Pad = 24;

    private const int TabsY = 68;
    private const int BodyY = 118;
    private const int BodyH = 424;
    private const int RepliesY = BodyY + BodyH + 8;
    private const int FooterY = PanelH - Pad - 40;

    private const int RowH = 40;

    private const uint Good = 0x6DBA79;
    private const uint Bad = 0xE67146;

    private enum Page { Players, Library, Server }

    private readonly int _rank = GlobalData.Get<AccountData>()?.Rank ?? 0;
    private readonly Container _body = new();
    private readonly Container _repliesBox = new();
    private readonly SimpleText _status;
    private readonly List<Container> _tabButtons = [];
    private readonly ColorRect _tabUnderline;
    private int _shownRepliesVersion = -1;

    private Page _page = Page.Players;

    // players page
    private TextInput _target;
    private TextInput _reason;
    private string _duration = "30m";
    private SimpleText _durationLabel;

    // library page
    // Id = the XML id (what ArtPlaceholders lists), Name = what the row shows (DisplayId when there is one - the dummies, loot bags and guild hall
    // upgrades have one, and until 2026-09-22 the placeholder check was made with it and called them NEW).
    private sealed record Entry(ushort Type, string Id, string Name, string Sub, string Sheet, int SheetIndex, bool IsItem, string Description);
    private List<Entry> _items;
    private List<Entry> _entities;
    private bool _showItems = true;
    private string _entityFilter = "All";
    private string _query = string.Empty;
    private TextInput _search;
    private int _libStart;
    private Entry _selected;

    public override (int Width, int Height)? FixedSize => (PanelW, PanelH);

    public AdminDashboardView() {
        AddChild(WaWStyle.Panel(PanelW, PanelH));
        AddChild(OptionsStyle.Label("Admin Dashboard", FontGroup.MyriadPro, 30f, PanelW / 2, 34, UiAnchor.Middle, WaWStyle.Highlight, 1));
        AddChild(OptionsStyle.Label($"You are {AdminRules.RankName(_rank)}. Every button types a chat command; the server decides what you may do.", FontGroup.MyriadPro, 15f, PanelW / 2, 58, UiAnchor.Middle, OptionsStyle.Tan, 1));

        var x = Pad;
        foreach (var (page, label) in new[] { (Page.Players, "Players"), (Page.Library, "Library"), (Page.Server, "Server") }) {
            var target = page;
            var button = WaWStyle.TextButton(label, 150, 36, 18f, () => ShowPage(target));
            button.X = x;
            button.Y = TabsY;
            AddChild(button);
            _tabButtons.Add(button);
            x += 158;
        }

        _tabUnderline = new ColorRect(new ColorRectConfig { Width = 150, Height = 3, Color = WaWStyle.Highlight, Alpha = 1f, Y = TabsY + 40 });
        AddChild(_tabUnderline);

        _body.X = Pad;
        _body.Y = BodyY;
        AddChild(_body);

        _repliesBox.X = Pad;
        _repliesBox.Y = RepliesY;
        AddChild(_repliesBox);

        _status = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 16f, Pad, FooterY + 20, UiAnchor.MiddleLeft, WaWStyle.Text, 1);
        AddChild(_status);

        var close = WaWStyle.TextButton("Close", 120, 40, 18f, CloseOverlay);
        close.X = PanelW - Pad - 120;
        close.Y = FooterY;
        AddChild(close);

        AddEventListener(Event.AddedToStage, () => {
            Stage.AddEventListener(KeyboardEvent.KeyDown, OnKeyDown);
            AddEventListener(Event.EnterFrame, OnFrame);
        });
        AddEventListener(Event.RemovedFromStage, () => {
            Stage.RemoveEventListener(KeyboardEvent.KeyDown, OnKeyDown);
            RemoveEventListener(Event.EnterFrame, OnFrame);
        });

        ShowPage(Page.Players);
    }

    // The game stops reading the keyboard while the dashboard is open (typing must not move the player); the caller has already switched that off.
    public override void CloseOverlay() {
        UserInput.SetManualFocus(true);
        base.CloseOverlay();
    }

    private void OnKeyDown(KeyboardEvent args) {
        if (args.Code == Settings.Options.Key || args.Code == Scancode.Escape) {      // Escape always closes; the options key is O since 2026-09-22
            CloseOverlay();
        }
    }

    private void OnFrame() {
        if (_shownRepliesVersion != AdminReplies.Version) {
            _shownRepliesVersion = AdminReplies.Version;
            DrawReplies();
        }
    }

    private void DrawReplies() {
        _repliesBox.RemoveChildren();
        _repliesBox.AddChild(OptionsStyle.Label("Server says:", FontGroup.MyriadPro, 14f, 0, 0, UiAnchor.LeftTop, OptionsStyle.Tan, 1));
        var lines = AdminReplies.Recent();
        var y = 18;
        foreach (var line in lines.Skip(Math.Max(0, lines.Length - 4))) {
            var bad = line.StartsWith("! ", StringComparison.Ordinal);
            _repliesBox.AddChild(OptionsStyle.Label(Shorten(bad ? line[2..] : line, 110), FontGroup.MyriadPro, 15f, 8, y, UiAnchor.LeftTop, bad ? Bad : OptionsStyle.Cream, 1));
            y += 17;
        }
    }

    // ---- sending -------------------------------------------------------------------------------------------------------------------------------------

    // Types the command into chat for the player. `command` is null when the buttons' inputs do not make a valid one.
    private void Send(string command, string problem) {
        if (command == null) {
            SetStatus(problem, false);
            return;
        }

        var packet = PlayerText.CreatePacket();
        packet.Text = command;
        Client.QueuePacket(packet);
        SetStatus("Sent: " + command, true);
    }

    private void SetStatus(string text, bool good) {
        _status.SetText(Shorten(text, 100));
        _status.SetColor(good ? Good : Bad);
    }

    private static string Shorten(string text, int max) => string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";

    // ---- pages ---------------------------------------------------------------------------------------------------------------------------------------

    private void ShowPage(Page page) {
        _page = page;
        _tabUnderline.X = Pad + (int) page * 158;
        _body.RemoveChildren();
        SetStatus(string.Empty, true);

        switch (page) {
            case Page.Players: BuildPlayers(); break;
            case Page.Library: BuildLibrary(); break;
            default: BuildServer(); break;
        }
    }

    private static Sprite Slot(int x, int y, int w, int h) {
        var slot = WaWStyle.Slot(w, h);
        slot.X = x;
        slot.Y = y;
        return slot;
    }

    private static Container Button(string text, int x, int y, int w, int h, float size, Action onClick) {
        var button = WaWStyle.TextButton(text, w, h, size, onClick);
        button.X = x;
        button.Y = y;
        return button;
    }

    private TextInput Input(int x, int y, int width, int maxChars) {
        var input = new TextInput(new InputConfig {
            X = x,
            Y = y,
            Width = width,
            FontSize = 18,
            FontType = FontType.Bold,
            OutlineThickness = 3,
            MaxCharacters = (byte) maxChars,
            ClickToActivate = true,
            BoxSlice = SliceLibrary.WaWSlot
        });
        _body.AddChild(input);
        return input;
    }

    private void Label(string text, int x, int y, float size = 16f, uint color = 0) =>
        _body.AddChild(OptionsStyle.Label(text, FontGroup.MyriadPro, size, x, y, UiAnchor.LeftTop, color == 0 ? WaWStyle.Text : color, 1));

    // ---- PLAYERS -------------------------------------------------------------------------------------------------------------------------------------

    private void BuildPlayers() {
        const int listW = 320;
        Label("In this world (click a name)", 0, 0, 16f, OptionsStyle.Tan);
        _body.AddChild(Slot(0, 24, listW, BodyH - 24));

        var names = Map.Entities.Values.OfType<Player>().Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) {
            _body.AddChild(OptionsStyle.Label("Nobody else here.", FontGroup.MyriadPro, 16f, listW / 2, 60, UiAnchor.Middle, OptionsStyle.Tan, 1));
        }

        var rows = (BodyH - 24 - 12) / 30;
        for (var i = 0; i < Math.Min(rows, names.Count); i++) {
            var name = names[i];
            var row = new Container { X = 8, Y = 30 + i * 30 };
            row.MouseEnabled = true;
            row.AddChild(new ColorRect(new ColorRectConfig { Width = listW - 16, Height = 27, Color = 0x000000, Alpha = 0.25f }));
            row.AddChild(OptionsStyle.Label(name, FontGroup.MyriadPro, 17f, 10, 14, UiAnchor.MiddleLeft, OptionsStyle.Cream, 1));
            row.AddEventListener(MouseEvent.LeftClick, () => _target.SetText(name));
            _body.AddChild(row);
        }

        if (names.Count > rows) {
            _body.AddChild(OptionsStyle.Label($"+{names.Count - rows} more (type the name)", FontGroup.MyriadPro, 14f, listW / 2, BodyH - 12, UiAnchor.Middle, OptionsStyle.Tan, 1));
        }

        const int fx = listW + 32;
        var fw = PanelW - Pad * 2 - fx;

        Label("Player name (letters only, up to 10)", fx, 0, 16f, OptionsStyle.Tan);
        _target = Input(fx, 24, fw, 10);

        Label("Reason (optional, shown to the player)", fx, 84, 16f, OptionsStyle.Tan);
        _reason = Input(fx, 108, fw, 100);

        _durationLabel = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 17f, fx + 210, 176, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        _body.AddChild(_durationLabel);
        RefreshDuration();
        _body.AddChild(Button("Change length", fx, 158, 190, 38, 16f, () => { _duration = AdminRules.NextDuration(_duration); RefreshDuration(); }));

        const int bw = 150;
        const int gap = 12;
        var y = 226;
        _body.AddChild(Button("Kick", fx, y, bw, 42, 18f, () => Send(AdminRules.Kick(_target.Text.Trim()), NeedName)));
        _body.AddChild(Button("Mute", fx + bw + gap, y, bw, 42, 18f, () => Send(AdminRules.Mute(_target.Text.Trim(), _duration, _reason.Text), NeedName)));
        _body.AddChild(Button("Ban", fx + (bw + gap) * 2, y, bw, 42, 18f, () => Send(AdminRules.Ban(_target.Text.Trim(), _duration, _reason.Text), NeedName)));
        y += 54;
        _body.AddChild(Button("Find", fx, y, bw, 42, 18f, () => Send(AdminRules.Find(_target.Text.Trim()), NeedName)));
        _body.AddChild(Button("Unmute", fx + bw + gap, y, bw, 42, 18f, () => Send(AdminRules.Unmute(_target.Text.Trim()), NeedName)));
        _body.AddChild(Button("Unban", fx + (bw + gap) * 2, y, bw, 42, 18f, () => Send(AdminRules.Unban(_target.Text.Trim()), NeedName)));

        Label("Mute and Ban use the length above ('perm' = no end). Moderators cannot act on other moderators or owners.", fx, y + 58, 14f, OptionsStyle.Tan);
    }

    private const string NeedName = "Type a player name first (letters only, up to 10).";

    private void RefreshDuration() => _durationLabel?.SetText("Length: " + (_duration == "perm" ? "permanent" : _duration));

    // ---- LIBRARY -------------------------------------------------------------------------------------------------------------------------------------

    private void EnsureLibrary() {
        if (_items != null) {
            return;
        }

        _items = ObjectLibrary.TypeToItem.Values
            .Select(i => new Entry(i.ObjectType, i.ObjectId, i.DisplayId ?? i.ObjectId, i.Tier >= 0 ? $"Tier {i.Tier}" : "Item", SheetOf(i.ObjectType), IndexOf(i.ObjectType), true, i.Description))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _entities = ObjectLibrary.TypeToObjectProps.Values
            .Where(p => !ObjectLibrary.TypeToItem.ContainsKey(p.ObjectType) && !string.IsNullOrWhiteSpace(p.ObjectId))
            .Select(p => new Entry(p.ObjectType, p.ObjectId, p.DisplayId ?? p.ObjectId, KindOf(p), SheetOf(p.ObjectType), IndexOf(p.ObjectType), false, p.Description))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string KindOf(ObjectProperties p) => p.IsPlayer || p.PlayerClassType != 0 ? "Class" : p.IsEnemy ? "Enemy" : p.Class == "Character" || p.IsAlly ? "NPC" : "Object";

    private static string SheetOf(ushort type) => ObjectLibrary.TypeToTextureData.TryGetValue(type, out var data) ? data.SheetName : null;

    private static int IndexOf(ushort type) => ObjectLibrary.TypeToTextureData.TryGetValue(type, out var data) ? data.SheetIndex : -1;

    private static ObjectRect Icon(Entry entry, int size) {
        TextureInfo texture;
        try {
            if (entry.IsItem) {
                texture = TextureHelper.FromGameAtlas(entry.Type);
            } else {
                var data = ObjectLibrary.TypeToTextureData[entry.Type];
                texture = TextureHelper.Create(data.HasAnimationData ? data.AnimatedTextures.FaceDown[0] : data.GetTexture(), TextureType.GameAtlas);
            }
        } catch (Exception) {
            texture = TextureHelper.FromGameAtlas(0x0096);
        }

        return new ObjectRect(new ObjectRectConfig { Texture = texture, Width = size, Height = size, OutlineEnabled = false, GlowEnabled = false });
    }

    private List<Entry> Filtered() {
        var source = _showItems ? _items : _entities;
        var q = _query.Trim();
        return source.Where(e => (_showItems || _entityFilter == "All" || e.Sub == _entityFilter) &&
                                 (q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Type.ToString("x").Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void BuildLibrary() {
        EnsureLibrary();
        _body.RemoveChildren();

        var itemsNew = _items.Count(e => ArtRules.IsNew(e.Sheet, e.SheetIndex, e.Id));
        var entNew = _entities.Count(e => ArtRules.IsNew(e.Sheet, e.SheetIndex, e.Id));
        Label($"Items: {_items.Count} ({itemsNew} new art, {_items.Count - itemsNew} original)     Entities: {_entities.Count} ({entNew} new art, {_entities.Count - entNew} original)", 0, 0, 15f, OptionsStyle.Tan);

        _body.AddChild(Button("Items", 0, 24, 100, 32, 16f, () => { _showItems = true; _libStart = 0; _selected = null; BuildLibrary(); }));
        _body.AddChild(Button("Entities", 108, 24, 110, 32, 16f, () => { _showItems = false; _libStart = 0; _selected = null; BuildLibrary(); }));
        _body.AddChild(new ColorRect(new ColorRectConfig { X = _showItems ? 0 : 108, Y = 58, Width = _showItems ? 100 : 110, Height = 3, Color = WaWStyle.Highlight, Alpha = 1f }));

        if (!_showItems) {
            var fx = 232;
            foreach (var filter in new[] { "All", "Enemy", "NPC", "Object", "Class" }) {
                var f = filter;
                _body.AddChild(Button(f, fx, 24, f == "Object" ? 84 : 72, 32, 15f, () => { _entityFilter = f; _libStart = 0; BuildLibrary(); }));
                if (_entityFilter == f) {
                    _body.AddChild(new ColorRect(new ColorRectConfig { X = fx, Y = 58, Width = f == "Object" ? 84 : 72, Height = 3, Color = WaWStyle.Highlight, Alpha = 1f }));
                }

                fx += f == "Object" ? 92 : 80;
            }
        }

        const int listW = 540;
        _search = new TextInput(new InputConfig { X = 0, Y = 68, Width = listW - 190, FontSize = 17, FontType = FontType.Bold, OutlineThickness = 3, MaxCharacters = 30, ClickToActivate = true, BoxSlice = SliceLibrary.WaWSlot });
        _search.SetText(_query);
        _body.AddChild(_search);
        _body.AddChild(Button("Search", listW - 182, 66, 88, 36, 15f, () => { _query = _search.Text; _libStart = 0; BuildLibrary(); }));
        _body.AddChild(Button("Clear", listW - 88, 66, 88, 36, 15f, () => { _query = string.Empty; _libStart = 0; BuildLibrary(); }));

        var list = Filtered();
        const int listTop = 110;
        var rows = (BodyH - listTop - 34) / RowH;
        _libStart = Math.Clamp(_libStart, 0, Math.Max(0, list.Count - 1));
        _libStart -= _libStart % rows;

        _body.AddChild(Slot(0, listTop, listW, rows * RowH + 8));
        if (list.Count == 0) {
            _body.AddChild(OptionsStyle.Label("Nothing matches.", FontGroup.MyriadPro, 17f, listW / 2, listTop + 40, UiAnchor.Middle, OptionsStyle.Tan, 1));
        }

        for (var i = 0; i < rows && _libStart + i < list.Count; i++) {
            _body.AddChild(BuildRow(list[_libStart + i], 4, listTop + 4 + i * RowH, listW - 8));
        }

        var pagerY = listTop + rows * RowH + 16;
        if (_libStart > 0) {
            _body.AddChild(Button("< Prev", 0, pagerY, 100, 28, 14f, () => { _libStart = Math.Max(0, _libStart - rows); BuildLibrary(); }));
        }

        if (_libStart + rows < list.Count) {
            _body.AddChild(Button("Next >", listW - 100, pagerY, 100, 28, 14f, () => { _libStart += rows; BuildLibrary(); }));
        }

        _body.AddChild(OptionsStyle.Label(list.Count == 0 ? string.Empty : $"{_libStart + 1}-{Math.Min(list.Count, _libStart + rows)} of {list.Count}", FontGroup.MyriadPro, 14f, listW / 2, pagerY + 14, UiAnchor.Middle, OptionsStyle.Tan, 1));

        BuildDetail(listW + 20);
    }

    private Sprite BuildRow(Entry entry, int x, int y, int width) {
        var row = new Container { X = x, Y = y };
        row.MouseEnabled = true;
        var picked = _selected != null && _selected.Type == entry.Type && _selected.IsItem == entry.IsItem;
        row.AddChild(new ColorRect(new ColorRectConfig { Width = width, Height = RowH - 3, Color = picked ? 0x8A6A2Au : 0x000000u, Alpha = picked ? 0.6f : 0.22f }));

        var icon = Icon(entry, 30);
        icon.X = 6;
        icon.Y = 3;
        row.AddChild(icon);

        row.AddChild(OptionsStyle.Label(Shorten(entry.Name, 26), FontGroup.MyriadPro, 17f, 46, 12, UiAnchor.MiddleLeft, OptionsStyle.Cream, 1));
        row.AddChild(OptionsStyle.Label($"{entry.Sub}   0x{entry.Type:x}", FontGroup.MyriadPro, 13f, 46, 28, UiAnchor.MiddleLeft, OptionsStyle.Tan, 1));
        var isNew = ArtRules.IsNew(entry.Sheet, entry.SheetIndex, entry.Id);
        row.AddChild(OptionsStyle.Label(ArtRules.Label(entry.Sheet, entry.SheetIndex, entry.Id), FontGroup.MyriadPro, 14f, width - 10, RowH / 2 - 2, UiAnchor.MiddleRight, isNew ? Good : OptionsStyle.Tan, 1));

        row.AddEventListener(MouseEvent.LeftClick, () => {
            _selected = entry;
            BuildLibrary();
        });
        return row;
    }

    private void BuildDetail(int x) {
        var w = PanelW - Pad * 2 - x;
        _body.AddChild(Slot(x, 110, w, BodyH - 110));

        if (_selected == null) {
            _body.AddChild(OptionsStyle.Label("Pick something on the left.", FontGroup.MyriadPro, 16f, x + w / 2, 200, UiAnchor.Middle, OptionsStyle.Tan, 1));
            return;
        }

        var e = _selected;
        var icon = Icon(e, 64);
        icon.X = x + 14;
        icon.Y = 124;
        _body.AddChild(icon);

        _body.AddChild(OptionsStyle.Label(Shorten(e.Name, 22), FontGroup.MyriadPro, 22f, x + 90, 128, UiAnchor.LeftTop, WaWStyle.Highlight, 1));
        _body.AddChild(OptionsStyle.Label($"{e.Sub}   type 0x{e.Type:x}", FontGroup.MyriadPro, 14f, x + 90, 156, UiAnchor.LeftTop, OptionsStyle.Tan, 1));
        var isNew = ArtRules.IsNew(e.Sheet, e.SheetIndex, e.Id);
        _body.AddChild(OptionsStyle.Label($"Art: {ArtRules.Label(e.Sheet, e.SheetIndex, e.Id)}  ({e.Sheet ?? "none"})", FontGroup.MyriadPro, 14f, x + 90, 176, UiAnchor.LeftTop, isNew ? Good : OptionsStyle.Tan, 1));

        var desc = string.IsNullOrWhiteSpace(e.Description) ? "No description." : e.Description.Trim();
        _body.AddChild(new SimpleText(new TextConfig {
            Text = Shorten(desc, 220),
            FontSize = 15,
            FontType = FontType.Normal,
            FontGroup = FontGroup.MyriadPro,
            Color = OptionsStyle.Cream,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            X = x + 14,
            Y = 214,
            MaxWidth = w - 28,
            Anchor = UiAnchor.LeftTop
        }));

        var by = BodyH - 56;
        if (!AdminRules.IsOwner(_rank)) {
            _body.AddChild(OptionsStyle.Label("Owners can give / spawn from here.", FontGroup.MyriadPro, 14f, x + w / 2, by + 16, UiAnchor.Middle, OptionsStyle.Tan, 1));
        } else if (e.IsItem) {
            _body.AddChild(Button("Give me one", x + 14, by, w - 28, 42, 18f, () => Send(AdminRules.Give(e.Name), "That item has no usable name.")));
        } else if (e.Sub == "Class") {
            _body.AddChild(OptionsStyle.Label("Player classes cannot be spawned.", FontGroup.MyriadPro, 14f, x + w / 2, by + 16, UiAnchor.Middle, OptionsStyle.Tan, 1));
        } else {
            var half = (w - 28 - 10) / 2;
            _body.AddChild(Button("Spawn 1", x + 14, by, half, 42, 18f, () => Send(AdminRules.Spawn(1, e.Name), "That entity has no usable name.")));
            _body.AddChild(Button("Spawn 5", x + 14 + half + 10, by, half, 42, 18f, () => Send(AdminRules.Spawn(5, e.Name), "That entity has no usable name.")));
        }
    }

    // The map / item editor is a developer tool that lives in the source folder (Tools/Editor); it opens in your browser (use Chrome or Edge so it can save files).
    private void OpenEditor() {
        var path = EditorLocator.Find(AppContext.BaseDirectory);
        if (path == null) {
            SetStatus("The editor is in the source folder (Tools/Editor/editor.html); this client is not running from there.", false);
        } else if (ClientPlatform.OpenLocalPage(path)) {
            SetStatus("Opened the editor in your browser.", true);
        } else {
            SetStatus("Could not open " + path, false);
        }
    }

    // ---- SERVER --------------------------------------------------------------------------------------------------------------------------------------

    private void BuildServer() {
        var players = Map.Entities.Values.OfType<Player>().Count();
        Label($"Your rank: {AdminRules.RankName(_rank)}", 0, 0, 20f, WaWStyle.Highlight);
        Label($"World: {Map.DisplayName ?? Map.Name ?? "?"}      Players in this world: {players}      Game version: {Settings.BuildVersion}", 0, 32, 16f);

        Label("Quick answers (the reply appears below):", 0, 76, 16f, OptionsStyle.Tan);
        _body.AddChild(Button("Who is online", 0, 100, 190, 40, 17f, () => Send("/online", string.Empty)));
        _body.AddChild(Button("My commands", 202, 100, 190, 40, 17f, () => Send("/commands", string.Empty)));
        _body.AddChild(Button("My rank", 404, 100, 190, 40, 17f, () => Send("/rank", string.Empty)));

        if (AdminRules.IsOwner(_rank)) {
            _body.AddChild(Button("Open the editor", 606, 100, 250, 40, 17f, OpenEditor));
        }

        Label("What each rank can type", 0, 170, 18f, WaWStyle.Highlight);
        var lines = new List<(string Rank, string Text)> {
            ("Player", "/online   /commands   /rank"),
            ("Moderator", "/find <player>   /kick <player>   /mute <player> <30m|2h|7d|perm> [reason]   /unmute   /ban <player> <length> [reason]   /unban"),
            ("Owner", "everything above, plus  /give <item>   /spawn <count> <entity>   /setrank <player> <player|moderator|owner>   /mail <player> <gold> <fame> <message>")
        };
        var y = 200;
        foreach (var (rank, text) in lines) {
            Label(rank, 0, y, 16f, OptionsStyle.Tan);
            _body.AddChild(new SimpleText(new TextConfig {
                Text = text,
                FontSize = 15,
                FontType = FontType.Normal,
                FontGroup = FontGroup.MyriadPro,
                Color = OptionsStyle.Cream,
                OutlineColor = OptionsStyle.OutlineDark,
                OutlineThickness = 1,
                X = 110,
                Y = y + 1,
                MaxWidth = PanelW - Pad * 2 - 110,
                Anchor = UiAnchor.LeftTop
            }));
            y += 66;
        }

        Label("Owners are set by hand in the database (an owner cannot be changed with a command). Moderators cannot act on other moderators or owners.", 0, y + 4, 14f, OptionsStyle.Tan);
    }
}
