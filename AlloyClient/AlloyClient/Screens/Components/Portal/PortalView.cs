using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlloyClient.AppEngine;
using AlloyClient.Assets.Libraries;
using AlloyClient.Data;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace AlloyClient.Screens.Components.Portal;

// The Portal, in the client (see PortalScreen). One parchment board on the 1280x720 design canvas with a nav row (HOME, TOP PLAYERS, GUILDS,
// ITEMS, CLASSES, GRAVEYARD), a player search box with suggestions, and a content area that one Build*Page fills per route. Player and
// guild names, classes and items are links, so it browses like the website does. The live data (profiles, leaderboards, guilds, who is
// online) comes from the account server's public API through PortalRequests; the wiki (items, classes) is built from the client's own
// XML, the same source the website's data files are generated from.
//
// Answers arrive on a worker thread and are queued, then applied in the frame loop; an answer for a page that was left in the meantime
// is dropped (see _loadSerial). Layout numbers are fixed design pixels, never measured from content.
public sealed partial class PortalView : Container {

    // The board (design px).
    private const int BoardX = 40;
    private const int BoardY = 20;
    private const int BoardW = 1200;
    private const int BoardH = 680;
    private const float BoardAlpha = 0.94f;

    private const int ContentLeft = 68;
    private const int ContentRight = 1212;
    private const int ContentW = ContentRight - ContentLeft;
    private const int ContentCenterX = (ContentLeft + ContentRight) / 2;
    private const int NavY = 72;
    private const int ContentTop = 118;
    private const int ContentBottom = 664;

    // The book's palette, so it reads as one family of screens.
    private const uint Ink = 0x2A1C14;
    private const uint InkSoft = 0x6B4A33;
    private const uint Hover = 0x9C4A1A;
    private const uint Gold = 0xB8791E;
    private const uint Good = 0x2F6B2A;
    private const uint Bad = 0x8A2A1A;
    private const uint ChipColor = 0x8A6A4A;

    private const float TitleSize = 30f;
    private const float BodySize = 22f;
    private const float SmallSize = 17f;

    private const int BackIconWidth = 40;
    private const int CloseGap = 34;       // room for the X after the back arrow; the title and the nav row move right by this much
    private const int BackIconHeight = 42;
    private const float BackIconHoverScale = 1.15f;
    private static readonly ColorTransform ScrollHoverTint = new(0.86f, 0.8f, 0.7f, 1f);

    private const int SearchDebounceMs = 250;
    private const int MaxNameLength = 16;

    private enum Page { Home, Player, Guild, Top, Items, Item, Classes, Class, Graveyard, Releases, Release }

    private readonly record struct Route(Page Page, string Arg);

    private static readonly (string Label, Page Page, string Arg)[] Nav = [
        ("HOME", Page.Home, null), ("TOP PLAYERS", Page.Top, "fame"), ("GUILDS", Page.Top, "guilds"),
        ("ITEMS", Page.Items, null), ("CLASSES", Page.Classes, null), ("GRAVEYARD", Page.Graveyard, null)
    ];

    private readonly Action _onExit;
    private readonly Container _content;
    private readonly Container _navRow;
    private readonly Container _suggestions;
    private bool _suggestionsOpen;
    private readonly TextInput _search;
    private bool _searchFocused;
    private string _searchLastQuery = "";
    private double _searchTypedMs = -1;
    private int _searchSerial;

    private readonly ConcurrentQueue<Action> _uiQueue = new();

    private Route _route = new(Page.Home, null);
    private readonly Stack<Route> _history = new();

    // Caches for the life of the screen (REFRESH on a page clears its entry).
    private PortalOnline _online;
    private string _onlineError;
    private readonly Dictionary<string, PortalProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PortalGuild> _guilds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PortalRow>> _boards = new();
    private string _pageError;
    private int _pageIndex;          // paging inside long lists (leaderboards, members, item grid)

    public PortalView(Action onExit, string openPlayer = null) : base(new ContainerConfig { Width = 1280, Height = 720 }) {
        _onExit = onExit;

        AddChild(new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesParchment,
            CutX = 12,
            CutY = 12,
            X = BoardX,
            Y = BoardY,
            Width = BoardW,
            Height = BoardH,
            Alpha = BoardAlpha
        }));

        AddChild(BuildBackIconButton(ContentLeft + BackIconWidth / 2, NavY, Back));
        // X next to the back arrow (2026-09-22): leaves The Portal in one click, however many pages deep (BACK walks the history one page at a time).
        AddChild(BuildTextButton("X", TitleSize - 2, ContentLeft + BackIconWidth + CloseGap / 2 + 4, NavY, UiAnchor.Middle, _onExit));
        AddChild(Text("THE PORTAL", TitleSize, ContentLeft + BackIconWidth + CloseGap + 18, NavY, UiAnchor.MiddleLeft));

        _navRow = new Container();
        AddChild(_navRow);

        // The search box: a player name, suggestions as you type, Enter or GO opens the profile.
        _search = new TextInput(new InputConfig {
            X = ContentRight - 300,
            Y = NavY - 18,
            Width = 210,
            FontSize = 18,
            FontType = FontType.Bold,
            FontGroup = FontGroup.MyriadPro,
            Color = 0xF2E6D0,
            OutlineColor = Ink,
            OutlineThickness = 3,
            MaxCharacters = MaxNameLength,
            ClickToActivate = true,
            DefaultText = "Player name",
            BoxSlice = SliceLibrary.DarkAgesSlot,
            OnFocus = () => _searchFocused = true,
            OnUnfocus = () => { _searchFocused = false; _searchTypedMs = -1; },
            OnChange = () => _searchTypedMs = 0
        });
        AddChild(_search);
        AddChild(BuildScrollButton("GO", ContentRight - 40, NavY, 76, 36, SubmitSearch));

        AddChild(Rule(ContentCenterX, NavY + 30, ContentW));

        _content = new Container();
        AddChild(_content);

        _suggestions = new Container();
        AddChild(_suggestions);           // above the content, so the dropdown covers it

        AddEventListener(Event.AddedToStage, () => Stage.AddEventListener(KeyboardEvent.KeyDown, OnKeyDown));
        AddEventListener(Event.RemovedFromStage, () => Stage.RemoveEventListener(KeyboardEvent.KeyDown, OnKeyDown));
        AddEventListener(Event.EnterFrame, OnFrame);

        RefreshOnline();
        if (!string.IsNullOrWhiteSpace(openPlayer)) {
            _history.Push(new Route(Page.Home, null));
            _route = new Route(Page.Player, openPlayer.Trim());
        }

        Render();
    }

    #region Routing

    private void Go(Page page, string arg = null) {
        var next = new Route(page, arg);
        if (next == _route) {
            return;
        }

        _history.Push(_route);
        _route = next;
        _pageIndex = 0;
        _pageError = null;
        CloseSuggestions();
        Render();
    }

    private void Back() {
        if (_history.Count == 0) {
            _onExit();
            return;
        }

        _route = _history.Pop();
        _pageIndex = 0;
        _pageError = null;
        CloseSuggestions();
        Render();
    }

    private void OpenPlayer(string name) {
        name = (name ?? "").Trim();
        if (name.Length == 0) {
            return;
        }

        if (name.Length > MaxNameLength) {
            name = name[..MaxNameLength];
        }

        Go(Page.Player, name);
    }

    private void Render() {
        _content.RemoveChildren();
        BuildNavRow();

        switch (_route.Page) {
            case Page.Home: BuildHomePage(); break;
            case Page.Player: BuildPlayerPage(_route.Arg); break;
            case Page.Guild: BuildGuildPage(_route.Arg); break;
            case Page.Top: BuildTopPage(_route.Arg ?? "fame"); break;
            case Page.Items: BuildItemsPage(); break;
            case Page.Item: BuildItemPage(_route.Arg); break;
            case Page.Classes: BuildClassesPage(); break;
            case Page.Class: BuildClassPage(_route.Arg); break;
            case Page.Graveyard: BuildGraveyardPage(); break;
            case Page.Releases: BuildReleasesPage(); break;
            case Page.Release: BuildReleasePage(_route.Arg); break;
        }
    }

    private void BuildNavRow() {
        _navRow.RemoveChildren();
        var x = ContentLeft + BackIconWidth + CloseGap + 18 + 214;
        foreach (var (label, page, arg) in Nav) {
            var current = _route.Page == page && (page != Page.Top || (_route.Arg ?? "fame") == arg || (arg == "fame" && _route.Arg != "guilds"));
            if (page == Page.Top && arg == "guilds") {
                current = _route.Page == Page.Top && _route.Arg == "guilds" || _route.Page == Page.Guild;
            } else if (page == Page.Top) {
                current = _route.Page == Page.Top && _route.Arg != "guilds";
            } else if (page == Page.Items) {
                current = _route.Page is Page.Items or Page.Item;
            } else if (page == Page.Classes) {
                current = _route.Page is Page.Classes or Page.Class;
            }

            var button = BuildTextButton(label, SmallSize, x, NavY, UiAnchor.MiddleLeft, () => Go(page, arg), current ? Hover : Ink);
            _navRow.AddChild(button);
            var width = MeasureWidth(label, SmallSize);
            if (current) {
                _navRow.AddChild(new ColorRect(new ColorRectConfig { X = x, Y = NavY + 14, Width = width, Height = 2, Color = Hover, Alpha = 0.9f }));
            }

            x += width + 22;
        }
    }

    #endregion

    #region Loading

    private void OnFrame() {
        while (_uiQueue.TryDequeue(out var action)) {
            action();
        }

        if (_searchTypedMs >= 0) {
            _searchTypedMs += Stage.GameTime.ElapsedMs;
            if (_searchTypedMs >= SearchDebounceMs) {
                _searchTypedMs = -1;
                RunSearch();
            }
        }
    }

    // Runs a server call without blocking the game; the answer is applied in the frame loop.
    private async Task RunAsync<T>(Func<Task<PortalRequests.Result<T>>> request, Action<PortalRequests.Result<T>> done) where T : class {
        PortalRequests.Result<T> result;
        try {
            result = await request();
        } catch (Exception) {
            result = new PortalRequests.Result<T> { Error = PortalRequests.Offline };
        }

        _uiQueue.Enqueue(() => done(result));
    }

    private void RefreshOnline() {
        _ = RunAsync(PortalRequests.GetOnline, r => {
            _online = r.Data ?? _online;
            _onlineError = r.Data == null ? r.Error : null;
            if (_route.Page is Page.Home) {
                Render();
            }
        });
    }

    private int MaxStars() => (_online?.StarGoals?.Length ?? 0) * ClassCount();

    #endregion

    #region Search

    private void OnKeyDown(KeyboardEvent args) {
        if (args.Key == Key.Return && _searchFocused) {
            SubmitSearch();
        } else if (args.Key == Key.Escape) {
            if (_suggestionsOpen) {
                CloseSuggestions();
            } else if (!_searchFocused) {
                Back();
            }
        }
    }

    private void SubmitSearch() {
        if (!_search.HasText(true) || _search.Text == "Player name") {
            return;
        }

        var name = _search.Text.Trim();
        _search.UnFocus(clearText: true);
        OpenPlayer(name);
    }

    private void RunSearch() {
        var query = _search.HasText(true) && _search.Text != "Player name" ? _search.Text.Trim() : "";
        if (query == _searchLastQuery) {
            return;
        }

        _searchLastQuery = query;
        if (query.Length == 0) {
            CloseSuggestions();
            return;
        }

        var serial = ++_searchSerial;
        _ = RunAsync(() => PortalRequests.Search(query), r => {
            if (serial != _searchSerial || !_searchFocused && _search.Text != query) {
                return;
            }

            ShowSuggestions(r.Data ?? []);
        });
    }

    private void ShowSuggestions(List<string> names) {
        _suggestions.RemoveChildren();
        _suggestionsOpen = names.Count > 0;
        if (names.Count == 0) {
            return;
        }

        const int rowH = 28;
        var x = ContentRight - 300;
        var y = NavY + 20;
        var width = 210;
        _suggestions.AddChild(new NineSliceRect(new NineSliceConfig { SliceData = SliceLibrary.DarkAgesParchment, CutX = 12, CutY = 12, X = x - 6, Y = y - 4, Width = width + 12, Height = names.Count * rowH + 10 }));
        for (var i = 0; i < names.Count; i++) {
            var name = names[i];
            var row = new Container { X = x, Y = y + i * rowH };
            row.MouseEnabled = true;
            var bg = new ColorRect(new ColorRectConfig { Width = width, Height = rowH, Color = Ink, Alpha = 0f });
            row.AddChild(bg);
            row.AddChild(Text(name, 18f, 8, rowH / 2, UiAnchor.MiddleLeft, outline: 1));
            var down = false;
            row.AddEventListener(MouseEvent.MouseOver, () => bg.Alpha = 0.16f);
            row.AddEventListener(MouseEvent.MouseOut, () => bg.Alpha = 0f);
            row.AddEventListener(MouseEvent.LeftDown, () => down = true);
            row.AddEventListener(MouseEvent.LeftUp, () => {
                if (down) {
                    _search.UnFocus(clearText: true);
                    _searchLastQuery = "";
                    OpenPlayer(name);
                }

                down = false;
            });
            _suggestions.AddChild(row);
        }
    }

    private void CloseSuggestions() {
        _suggestions.RemoveChildren();
        _suggestionsOpen = false;
    }

    #endregion

    #region Game data helpers (classes and items come from the client's own XML)

    private static int ClassCount() {
        var n = 0;
        foreach (var props in ObjectLibrary.TypeToObjectProps.Values) {
            if (props.IsPlayer) {
                n++;
            }
        }

        return n;
    }

    private static string ClassName(int type) =>
        ObjectLibrary.TypeToObjectProps.TryGetValue((ushort) type, out var props) ? props.ObjectId : $"Class {type:x4}";

    private static string ItemName(int type) =>
        type > 0 && ObjectLibrary.TypeToItem.TryGetValue((ushort) type, out var item) ? item.DisplayName : $"Item {type:x4}";

    private static readonly Dictionary<int, string> SlotNames = new() {
        { 0, "Any" }, { 1, "Sword" }, { 2, "Dagger" }, { 3, "Bow" }, { 4, "Tome" }, { 5, "Shield" }, { 6, "Leather Armor" }, { 7, "Armor" }, { 8, "Wand" }, { 9, "Ring" },
        { 10, "Potion" }, { 11, "Spell" }, { 12, "Seal" }, { 13, "Cloak" }, { 14, "Robe" }, { 15, "Quiver" }, { 16, "Helmet" }, { 17, "Staff" }, { 18, "Poison" }, { 19, "Skull" },
        { 20, "Trap" }, { 21, "Orb" }, { 22, "Prism" }, { 23, "Scepter" }, { 24, "Katana" }, { 25, "Shuriken" }, { 26, "Consumable" }
    };

    private static string SlotName(int slot) => SlotNames.TryGetValue(slot, out var name) ? name : $"Slot {slot}";

    private static readonly Dictionary<int, string> StatNames = new() { { 0, "HP" }, { 3, "MP" }, { 20, "ATT" }, { 21, "DEF" }, { 22, "SPD" }, { 23, "VIT" }, { 24, "WIS" }, { 25, "DEX" } };

    private static readonly string[] StatLabels = ["HP", "MP", "ATT", "DEF", "SPD", "DEX", "VIT", "WIS"];

    // A class's max stats in StatLabels order, or null for an unknown class.
    private static int[] ClassMaxStats(int type) {
        if (!ObjectLibrary.TypeToObjectProps.TryGetValue((ushort) type, out var props) || props.PlayerProperties == null) {
            return null;
        }

        var p = props.PlayerProperties;
        return [p.MaxHp, p.MaxMp, p.MaxAttack, p.MaxDefense, p.MaxSpeed, p.MaxDexterity, p.MaxVitality, p.MaxWisdom];
    }

    private static string N(long value) => value.ToString("N0");

    #endregion

    #region Small builders (the book's look)

    private static SimpleText Text(string text, float size, int x, int y, UiAnchor anchor, uint color = Ink, int maxWidth = 0, int outline = 2) {
        var cfg = new TextConfig {
            Text = text ?? "",
            FontSize = size,
            FontType = FontType.Bold,
            FontGroup = FontGroup.MyriadPro,
            Color = color,
            OutlineColor = color,
            OutlineThickness = outline,
            X = x,
            Y = y,
            Anchor = anchor
        };
        if (maxWidth > 0) {
            cfg.MaxWidth = maxWidth;
        }

        return new SimpleText(cfg);
    }

    private static int MeasureWidth(string text, float size) => Text(text, size, 0, 0, UiAnchor.LeftTop).Width;

    private static ColorRect Rule(int centerX, int y, int width) =>
        new(new ColorRectConfig { Width = width, Height = 2, Color = InkSoft, Alpha = 0.6f }) { X = centerX - width / 2, Y = y };

    private static ColorRect Box(int x, int y, int width, int height, float alpha, uint color = Ink) =>
        new(new ColorRectConfig { X = x, Y = y, Width = width, Height = height, Color = color, Alpha = alpha });

    private static ObjectRect ItemIcon(int type, int centerX, int centerY, int size) => new(new ObjectRectConfig {
        Texture = TextureHelper.FromGameAtlas((ushort) type),
        X = centerX,
        Y = centerY,
        Width = size,
        Height = size,
        Anchor = UiAnchor.Middle,
        OutlineEnabled = false,
        GlowEnabled = false
    });

    private static ObjectRect Portrait(int classType, int centerX, int centerY, int size) {
        if (!ObjectLibrary.TypeToTextureData.TryGetValue((ushort) classType, out var textureData) || textureData.AnimatedTextures.FaceDown == null || textureData.AnimatedTextures.FaceDown.Length == 0) {
            return null;
        }

        return new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(textureData.AnimatedTextures.FaceDown[0], TextureType.GameAtlas),
            X = centerX,
            Y = centerY,
            Width = size,
            Height = size,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
    }

    private static Container BuildBackIconButton(int centerX, int centerY, Action onClicked) {
        var button = new Container { X = centerX, Y = centerY };
        button.SetAnchor(UiAnchor.Middle);
        button.MouseEnabled = true;
        button.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Icons/BookBackIcon", 0, false),
            Width = BackIconWidth,
            Height = BackIconHeight,
            OutlineEnabled = false,
            GlowEnabled = false
        }));

        var down = false;
        button.AddEventListener(MouseEvent.MouseOver, () => button.Scale = new Vector2(BackIconHoverScale));
        button.AddEventListener(MouseEvent.MouseOut, () => button.Scale = Vector2.One);
        button.AddEventListener(MouseEvent.LeftDown, () => down = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                onClicked();
            }

            down = false;
        });
        return button;
    }

    private static Container BuildScrollButton(string text, int centerX, int centerY, int width, int height, Action onClicked) {
        var button = new Container { X = centerX - width / 2, Y = centerY - height / 2 };
        button.MouseEnabled = true;
        var scroll = new NineSliceRect(new NineSliceConfig { SliceData = SliceLibrary.DarkAgesParchment, CutX = 12, CutY = 12, Width = width, Height = height });
        button.AddChild(scroll);
        button.AddChild(Text(text, 20f, width / 2, height / 2, UiAnchor.Middle, outline: 1));

        var down = false;
        button.AddEventListener(MouseEvent.MouseOver, () => scroll.ColorTransformation = ScrollHoverTint);
        button.AddEventListener(MouseEvent.MouseOut, () => scroll.ColorTransformation = new ColorTransform(1f, 1f, 1f, 1f));
        button.AddEventListener(MouseEvent.LeftDown, () => down = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                onClicked();
            }

            down = false;
        });
        return button;
    }

    // Plain text that darkens to the hover colour; the whole text area is the click target. Used for the nav row and every name / class / item link.
    private static Container BuildTextButton(string text, float size, int x, int y, UiAnchor anchor, Action onClicked, uint color = Ink, int maxWidth = 0) {
        var button = new Container { X = x, Y = y };
        button.SetAnchor(anchor);
        button.MouseEnabled = true;
        var label = Text(text, size, 0, 0, UiAnchor.LeftTop, color, maxWidth, outline: 1);
        button.AddChild(label);

        var down = false;
        button.AddEventListener(MouseEvent.MouseOver, () => label.SetColor(Hover));
        button.AddEventListener(MouseEvent.MouseOut, () => label.SetColor(color));
        button.AddEventListener(MouseEvent.LeftDown, () => down = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                onClicked();
            }

            down = false;
        });
        return button;
    }

    // A whole row (any width) that highlights on hover and opens something on click; the caller adds the texts on top.
    private static Container BuildRowButton(int x, int y, int width, int height, Action onClicked, float idleAlpha = 0.08f) {
        var row = new Container { X = x, Y = y };
        var bg = Box(0, 0, width, height, idleAlpha);
        row.AddChild(bg);
        if (onClicked == null) {
            return row;         // a static strip: its own children (links, slots) take the clicks
        }

        row.MouseEnabled = true;
        var down = false;
        row.AddEventListener(MouseEvent.MouseOver, () => bg.Alpha = 0.22f);
        row.AddEventListener(MouseEvent.MouseOut, () => bg.Alpha = idleAlpha);
        row.AddEventListener(MouseEvent.LeftDown, () => down = true);
        row.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                onClicked?.Invoke();
            }

            down = false;
        });
        return row;
    }

    private void PageTitle(string title, string subtitle = null) {
        _content.AddChild(Text(title, TitleSize, ContentLeft, ContentTop, UiAnchor.LeftTop, maxWidth: ContentW - 240));
        if (!string.IsNullOrEmpty(subtitle)) {
            _content.AddChild(Text(subtitle, SmallSize, ContentRight, ContentTop + 12, UiAnchor.RightTop, color: InkSoft, outline: 1));
        }

        _content.AddChild(Rule(ContentCenterX, ContentTop + 44, ContentW));
    }

    private void Notice(string message, int y = 0, uint color = InkSoft) {
        _content.AddChild(Text(message, BodySize, ContentCenterX, y == 0 ? ContentTop + 120 : y, UiAnchor.Middle, color: color, maxWidth: ContentW - 80, outline: 1));
    }

    // "PREV   page 2 of 7   NEXT" along the bottom edge of the content area; returns the slice of items to draw.
    private (int First, int Count) Pager(int total, int pageSize) {
        var pages = Math.Max(1, (total + pageSize - 1) / pageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, pages - 1);
        if (pages > 1) {
            var y = ContentBottom - 10;
            _content.AddChild(Text($"page {_pageIndex + 1} of {pages}", SmallSize, ContentCenterX, y, UiAnchor.Middle, color: InkSoft, outline: 1));
            if (_pageIndex > 0) {
                _content.AddChild(BuildTextButton("< PREV", SmallSize, ContentCenterX - 140, y, UiAnchor.Middle, () => { _pageIndex--; Render(); }));
            }

            if (_pageIndex < pages - 1) {
                _content.AddChild(BuildTextButton("NEXT >", SmallSize, ContentCenterX + 140, y, UiAnchor.Middle, () => { _pageIndex++; Render(); }));
            }
        }

        var first = _pageIndex * pageSize;
        return (first, Math.Min(pageSize, total - first));
    }

    private Container RefreshLink(Action onClicked) => BuildTextButton("REFRESH", SmallSize, ContentRight, ContentBottom - 10, UiAnchor.MiddleRight, onClicked, InkSoft);

    #endregion
}
