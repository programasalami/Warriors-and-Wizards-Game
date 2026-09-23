using System.Collections.Generic;
using OpenTK.Platform;
using WaWClient.AppEngine;
using WaWClient.Data;
using WaWClient.Game.Components.Hud;
using WaWClient.Game.Components.Options;
using WaWClient.Ui.Components.Panels;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.News;

// The News Board window (2026-09-21): the patch notes the server ships (Resources/News/PatchNotes.txt), newest first, one entry after another with
// Newer / Older paging - the same walnut window as the Bug Board, minus the writing. Nothing here is editable in the game: the file is written
// before a deploy and every player reads the same text.
// 2026-09-22: pages are cut by LINE, not by whole entry. An entry longer than the list (a big session's notes) used to be placed whole and ran
// off the bottom of the window; now it stops at the last line that fits and carries on at the top of the next page as "<title> (continued)".
// The list is also a clip container, so nothing can spill past its box whatever happens.
public sealed class NewsBoardView : Overlay {
    // The panel is a FIXED size (1280x720 design canvas); nothing here is sized from its content.
    private const int PanelW = 760;
    private const int PanelH = 580;
    private const int Pad = 24;
    private const int ListX = Pad;
    private const int ListY = 88;
    private const int ListWidth = PanelW - Pad * 2;
    private const int ListHeight = 380;
    private const int RowPadding = 10;
    private const int PagerY = ListY + ListHeight + 12;
    private const int FooterY = PanelH - Pad - 40;

    private readonly Container _list = new(new ContainerConfig { EnableClip = true, Width = ListWidth, Height = ListHeight }) { MouseEnabled = false };
    private readonly SimpleText _pageText;
    private readonly Container _prevButton;
    private readonly Container _nextButton;

    private NewsBoardData _data;
    private string _error = "Loading...";
    private int _start;                      // first entry on this page
    private int _startLine;                  // and the first of its lines (0 = its title too)
    private int _lastEntry;                  // last entry with anything on this page
    private (int Entry, int Line)? _next;    // where the next page starts (null = this is the last page)
    private readonly Stack<(int Entry, int Line)> _previousStarts = [];

    public override (int Width, int Height)? FixedSize => (PanelW, PanelH);

    public NewsBoardView() {
        AddChild(WaWStyle.Panel(PanelW, PanelH));
        AddChild(OptionsStyle.Label("News Board", FontGroup.MyriadPro, 30f, PanelW / 2, 34, UiAnchor.Middle, WaWStyle.Highlight, 1));

        var backdrop = WaWStyle.Slot(ListWidth + 8, ListHeight + 8);
        backdrop.X = ListX - 4;
        backdrop.Y = ListY - 4;
        AddChild(backdrop);
        _list.X = ListX;
        _list.Y = ListY;
        AddChild(_list);

        _prevButton = WaWStyle.TextButton("< Newer", 110, 32, 16f, OnPrevious);
        _prevButton.X = Pad;
        _prevButton.Y = PagerY;
        AddChild(_prevButton);
        _nextButton = WaWStyle.TextButton("Older >", 110, 32, 16f, OnNext);
        _nextButton.X = PanelW - Pad - 110;
        _nextButton.Y = PagerY;
        AddChild(_nextButton);
        _pageText = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 15f, PanelW / 2, PagerY + 16, UiAnchor.Middle, OptionsStyle.Tan, 1);
        AddChild(_pageText);

        var refresh = WaWStyle.TextButton("Refresh", 120, 40, 18f, Reload);
        refresh.X = Pad;
        refresh.Y = FooterY;
        AddChild(refresh);
        var close = WaWStyle.TextButton("Close", 120, 40, 18f, CloseOverlay);
        close.X = PanelW - Pad - 120;
        close.Y = FooterY;
        AddChild(close);

        AddEventListener(Event.AddedToStage, () => Stage.AddEventListener(KeyboardEvent.KeyDown, OnKeyDown));
        AddEventListener(Event.RemovedFromStage, () => Stage.RemoveEventListener(KeyboardEvent.KeyDown, OnKeyDown));

        Layout();
        Reload();
    }

    // The game stops reading the keyboard while the board is open; the caller has already switched that off.
    public override void CloseOverlay() {
        UserInput.SetManualFocus(true);
        base.CloseOverlay();
    }

    private void OnKeyDown(KeyboardEvent args) {
        if (args.Code == Settings.Options.Key || args.Code == Scancode.Escape) {      // Escape always closes; the options key is O since 2026-09-22
            CloseOverlay();
        }
    }

    private void Reload() {
        _error = _data == null ? "Loading..." : null;
        AddEventListener(AppRequests.GetNewsBoard(), OnLoaded);
    }

    private void OnLoaded(AppRequests.NewsBoardResult result) {
        if (result.Data != null) {
            _data = result.Data;
            _error = null;
        } else {
            _data = null;
            _error = result.Error;
        }

        _start = 0;
        _startLine = 0;
        _previousStarts.Clear();
        Layout();
    }

    private void Layout() {
        while (_list.NumChildren > 0) {
            _list.RemoveChildAt(0);
        }

        _lastEntry = _start;
        _next = null;
        if (_data == null || _data.Entries.Count == 0) {
            var message = _data == null ? _error ?? "Loading..." : "Nothing on the board yet.";
            _list.AddChild(OptionsStyle.Label(message, FontGroup.MyriadPro, 18f, ListWidth / 2, ListHeight / 2, UiAnchor.Middle, OptionsStyle.Tan, 1));
            UpdatePager();
            return;
        }

        var y = 0;
        var line = _startLine;
        for (var i = _start; i < _data.Entries.Count; i++) {
            var entry = _data.Entries[i];
            var row = BuildEntry(entry, ListWidth - RowPadding * 2, line, ListHeight - y, y == 0, out var rowHeight, out var nextLine);
            if (row == null) {
                _next = (i, line);          // nothing of it fits under what is already on the page: it opens the next one
                break;
            }

            row.Y = y;
            _list.AddChild(row);
            y += rowHeight;
            _lastEntry = i;

            if (nextLine < entry.Lines.Count) {
                _next = (i, nextLine);      // the rest of this entry continues on the next page
                break;
            }

            line = 0;
            _next = i + 1 < _data.Entries.Count ? (i + 1, 0) : null;
        }

        UpdatePager();
    }

    // Title (gold) with the date on the right, then one line per change from `startLine` on, wrapped to the list's width, as many as fit in
    // `maxHeight`. `nextLine` is the first line that did not fit (= Lines.Count when the entry is complete, which is when it gets its divider).
    // Returns null when not even the title and one line fit - unless this is the first thing on the page, which always shows at least a line.
    private static Container BuildEntry(NewsEntry entry, int textWidth, int startLine, int maxHeight, bool firstOnPage, out int rowHeight, out int nextLine) {
        var row = new Container();
        var titleText = startLine > 0 ? entry.Title + " (continued)" : entry.Title;
        var title = OptionsStyle.Label(titleText, FontGroup.MyriadPro, 20f, RowPadding, 8, UiAnchor.LeftTop, WaWStyle.Highlight, 1);
        row.AddChild(title);
        if (startLine == 0 && (!string.IsNullOrEmpty(entry.Date) || !string.IsNullOrEmpty(entry.Version))) {
            var stamp = string.IsNullOrEmpty(entry.Version) ? entry.Date : $"v{entry.Version}   {entry.Date}";
            row.AddChild(OptionsStyle.Label(stamp, FontGroup.MyriadPro, 14f, RowPadding + textWidth, 11, UiAnchor.RightTop, OptionsStyle.Tan, 1));
        }

        var y = 8 + title.Height + 6;
        nextLine = startLine;
        while (nextLine < entry.Lines.Count) {
            var body = new SimpleText(new TextConfig {
                Text = "- " + entry.Lines[nextLine],
                FontSize = 16,
                FontType = FontType.Normal,
                FontGroup = FontGroup.MyriadPro,
                Color = WaWStyle.Text,
                OutlineColor = OptionsStyle.OutlineDark,
                OutlineThickness = 1,
                X = RowPadding + 8,
                Y = y,
                Anchor = UiAnchor.LeftTop,
                MaxWidth = textWidth - 8
            });
            var lineHeight = (int) body.Height + 3;
            var isLast = nextLine == entry.Lines.Count - 1;
            var needed = y + lineHeight + (isLast ? 14 : 4);
            if (needed > maxHeight && !(firstOnPage && nextLine == startLine)) {
                break;          // no room: this line opens the next page
            }

            row.AddChild(body);
            y += lineHeight;
            nextLine++;
        }

        if (nextLine == startLine && entry.Lines.Count > 0) {
            rowHeight = 0;
            return null;        // not one line fitted (and the page already has something on it)
        }

        if (nextLine < entry.Lines.Count) {
            rowHeight = y + 4;  // to be continued: no divider
            return row;
        }

        rowHeight = y + 14;
        var divider = new ColorRect(new ColorRectConfig { X = RowPadding, Y = rowHeight - 4, Width = textWidth, Height = 1, Color = OptionsStyle.Tan, Alpha = 0.35f });
        row.AddChild(divider);
        return row;
    }

    private void UpdatePager() {
        var total = _data?.Entries.Count ?? 0;
        _pageText.SetText(total == 0 ? string.Empty : _start == _lastEntry ? $"{_start + 1} of {total}" : $"{_start + 1}-{_lastEntry + 1} of {total}");
        _prevButton.Visible = _previousStarts.Count > 0;
        _nextButton.Visible = _next != null;
    }

    private void OnPrevious() {
        if (_previousStarts.Count == 0) {
            return;
        }

        (_start, _startLine) = _previousStarts.Pop();
        Layout();
    }

    private void OnNext() {
        if (_data == null || _next == null) {
            return;
        }

        _previousStarts.Push((_start, _startLine));
        (_start, _startLine) = _next.Value;
        Layout();
    }
}
