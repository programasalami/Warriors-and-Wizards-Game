using System;
using OpenTK.Platform;
using System.Collections.Generic;
using WaWClient.AppEngine;
using WaWClient.Data;
using WaWClient.Game.Components.Hud;
using WaWClient.Game.Components.Options;
using WaWClient.Ui;
using WaWClient.Ui.Components.Panels;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.BugBoard;

// The Bug Board window: the newest reports, a box to write your own, and (for admins only) buttons to mark a report confirmed / fixed or delete it.
// Everything a player writes is plain text (the server cleans it and rate limits posting); it is drawn as literal text, never interpreted.
public sealed class BugBoardView : Overlay {

    // The panel is a FIXED size (1280x720 design canvas); nothing here is sized from its content.
    private const int PanelW = 760;
    private const int PanelH = 580;
    private const int Pad = 24;

    private const int ListX = Pad;
    private const int ListY = 88;
    private const int ListWidth = PanelW - Pad * 2;
    private const int ListHeight = 288;

    // Room kept on the right of each report for the admin's three small buttons.
    private const int ModerationWidth = 214;
    private const int RowPadding = 8;

    private const int PagerY = ListY + ListHeight + 12;
    private const int InputLabelY = PagerY + 44;
    private const int InputY = InputLabelY + 24;
    private const int FooterY = PanelH - Pad - 40;

    private const uint GoodColor = 0x6DBA79;
    private const uint BadColor = 0xE67146;
    private const uint ConfirmedColor = OptionsStyle.Gold;

    private readonly Container _list = new();
    private readonly SimpleText _pageText;
    private readonly SimpleText _statusText;
    private readonly TextInput _input;
    private readonly Container _prevButton;
    private readonly Container _nextButton;

    private BugBoardData _data;
    private string _error = "Loading...";
    private int _start;
    private int _shown;
    private readonly Stack<int> _previousStarts = [];
    private bool _posting;

    public override (int Width, int Height)? FixedSize => (PanelW, PanelH);

    // Every child below is laid out from this sprite's own top-left corner; OverlayManager centres the fixed-size panel.
    public BugBoardView() {
        AddChild(WaWStyle.Panel(PanelW, PanelH));
        AddChild(OptionsStyle.Label("Bug Board", FontGroup.MyriadPro, 30f, PanelW / 2, 34, UiAnchor.Middle, WaWStyle.Highlight, 1));

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

        AddChild(OptionsStyle.Label("Write a report (what happened, and where):", FontGroup.MyriadPro, 16f, Pad, InputLabelY, UiAnchor.LeftTop, WaWStyle.Text, 1));
        _input = new TextInput(new InputConfig {
            X = Pad,
            Y = InputY,
            Width = PanelW - Pad * 2 - 146,
            FontSize = 18,
            FontType = FontType.Bold,
            OutlineThickness = 3,
            MaxCharacters = 200,
            ClickToActivate = true,
            BoxSlice = SliceLibrary.WaWSlot
        });
        AddChild(_input);

        var post = WaWStyle.TextButton("Post", 130, 36, 18f, OnPostClicked);
        post.X = PanelW - Pad - 130;
        post.Y = InputY - 2;
        AddChild(post);

        _statusText = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 16f, Pad, InputY + 38, UiAnchor.LeftTop, WaWStyle.Text, 1);       // clear of the Refresh / Close buttons below (they start 60 px under the input)
        AddChild(_statusText);

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

    // The game stops reading the keyboard while the board is open (typing must not move the player); the caller has already switched that off.
    public override void CloseOverlay() {
        UserInput.SetManualFocus(true);
        base.CloseOverlay();
    }

    private void OnKeyDown(KeyboardEvent args) {
        if (args.Code == Settings.Options.Key || args.Code == Scancode.Escape) {      // Escape always closes; the options key is O since 2026-09-22
            CloseOverlay();
        }
    }

    // ---- loading ---------------------------------------------------------------------------------------------------------------------------------

    private void Reload() {
        _error = _data == null ? "Loading..." : null;
        AddEventListener(AppRequests.GetBugBoard(), OnLoaded);
    }

    private void OnLoaded(AppRequests.BugBoardResult result) {
        if (result.Data != null) {
            _data = result.Data;
            _error = null;
        } else {
            _data = null;
            _error = result.Error;
        }

        _start = 0;
        _previousStarts.Clear();
        Layout();
    }

    // ---- the list --------------------------------------------------------------------------------------------------------------------------------

    private void Layout() {
        while (_list.NumChildren > 0) {
            _list.RemoveChildAt(0);
        }

        _shown = 0;

        if (_data == null || _data.Posts.Count == 0) {
            var message = _data == null ? _error ?? "Loading..." : "No reports yet. Be the first to write one!";
            _list.AddChild(OptionsStyle.Label(message, FontGroup.MyriadPro, 18f, ListWidth / 2, ListHeight / 2, UiAnchor.Middle, OptionsStyle.Tan, 1));
            UpdatePager();
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var textWidth = ListWidth - RowPadding * 2 - (_data.CanModerate ? ModerationWidth : 0);
        var y = 0;
        for (var i = _start; i < _data.Posts.Count; i++) {
            var row = BuildRow(_data.Posts[i], now, textWidth, out var rowHeight);
            if (_shown > 0 && y + rowHeight > ListHeight) {
                break;      // this one goes on the next page
            }

            row.Y = y;
            _list.AddChild(row);
            y += rowHeight;
            _shown++;
        }

        UpdatePager();
    }

    private Container BuildRow(BugPost post, long now, int textWidth, out int rowHeight) {
        var row = new Container();

        row.AddChild(OptionsStyle.Label($"{post.Author}   {BugBoardData.Ago(post.CreatedAt, now)}", FontGroup.MyriadPro, 16f, RowPadding, 6, UiAnchor.LeftTop, WaWStyle.Highlight, 1));

        var tagColor = post.Status switch {
            BugBoardData.StatusConfirmed => ConfirmedColor,
            BugBoardData.StatusFixed => GoodColor,
            _ => OptionsStyle.Tan
        };
        row.AddChild(OptionsStyle.Label(BugBoardData.StatusLabel(post.Status), FontGroup.MyriadPro, 14f, RowPadding + textWidth, 7, UiAnchor.RightTop, tagColor, 1));

        var body = new SimpleText(new TextConfig {
            Text = post.Message,
            FontSize = 17,
            FontType = FontType.Normal,
            FontGroup = FontGroup.MyriadPro,
            Color = OptionsStyle.Cream,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            X = RowPadding,
            Y = 28,
            MaxWidth = textWidth,
            Anchor = UiAnchor.LeftTop
        });
        row.AddChild(body);

        rowHeight = Math.Max(28 + body.Height + 14, _data.CanModerate ? 52 : 0);
        row.AddChild(new ColorRect(new ColorRectConfig { X = RowPadding, Y = rowHeight - 5, Width = ListWidth - RowPadding * 2, Height = 1, Color = 0x000000, Alpha = 0.35f }));

        if (_data.CanModerate) {
            var x = ListWidth - ModerationWidth + 2;
            var y = (rowHeight - 26) / 2;
            AddModerationButton(row, "Confirm", x, y, () => Mark(post.Id, BugBoardData.StatusConfirmed));
            AddModerationButton(row, "Fixed", x + 70, y, () => Mark(post.Id, BugBoardData.StatusFixed));
            AddModerationButton(row, "Delete", x + 140, y, () => Delete(post.Id));
        }

        return row;
    }

    private static void AddModerationButton(Container row, string text, int x, int y, Action onClick) {
        var button = WaWStyle.TextButton(text, 64, 26, 13f, onClick);
        button.X = x;
        button.Y = y;
        row.AddChild(button);
    }

    private void UpdatePager() {
        var total = _data?.Posts.Count ?? 0;
        _prevButton.Visible = _previousStarts.Count > 0;
        _nextButton.Visible = _shown > 0 && _start + _shown < total;
        _pageText.SetText(_shown > 0 ? $"{_start + 1}-{_start + _shown} of {total}" : string.Empty);
    }

    private void OnNext() {
        _previousStarts.Push(_start);
        _start += _shown;
        Layout();
    }

    private void OnPrevious() {
        if (_previousStarts.Count == 0) {
            return;
        }

        _start = _previousStarts.Pop();
        Layout();
    }

    // ---- writing and moderating ------------------------------------------------------------------------------------------------------------------

    private void SetStatus(string text, bool good) {
        _statusText.SetText(text);
        _statusText.SetColor(good ? GoodColor : BadColor);
    }

    private void OnPostClicked() {
        if (_posting) {
            return;
        }

        var text = _input.Text?.Trim() ?? string.Empty;
        if (text.Length == 0) {
            SetStatus("Write something first.", false);
            return;
        }

        if (!Data.GlobalData.Contains<LoginData>()) {
            SetStatus("Sign in to post on the Bug Board.", false);
            return;
        }

        _posting = true;
        SetStatus("Posting...", true);
        AddEventListener(AppRequests.PostToBugBoard(text), OnPosted);
    }

    private void OnPosted(AppResponse response) {
        _posting = false;
        if (!response.Success) {
            SetStatus(response.Message ?? "Could not post that.", false);
            return;
        }

        _input.SetText(string.Empty);
        SetStatus("Posted. Thank you!", true);
        Reload();
    }

    private void Mark(int id, string status) => AddEventListener(AppRequests.SetBugPostStatus(id, status), OnModerated);

    private void Delete(int id) => AddEventListener(AppRequests.DeleteBugPost(id), OnModerated);

    private void OnModerated(AppResponse response) {
        if (!response.Success) {
            SetStatus(response.Message ?? "That did not work.", false);
        }

        Reload();
    }
}
