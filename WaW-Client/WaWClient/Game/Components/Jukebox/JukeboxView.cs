using System;
using OpenTK.Platform;
using WaWClient.AppEngine;
using WaWClient.Data;
using WaWClient.Game.Components.Hud;
using WaWClient.Game.Components.Options;
using WaWClient.Game.Music;
using WaWClient.Ui;
using WaWClient.Ui.Components.Panels;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Jukebox;

// The Jukebox window: what is playing for everyone (with a progress bar), previous / next, and the library to pick a song from. Whatever is chosen here changes the
// music for every player in the game. It is free for now; later skipping and picking will cost fame (MusicRules on the server).
public sealed class JukeboxView : Overlay {

    // Fixed sizes on the 1280x720 design canvas; nothing is sized from its content.
    private const int PanelW = 640;
    private const int PanelH = 610;
    private const int Pad = 24;

    private const int NowY = 88;
    private const int NowH = 122;
    private const int BarX = Pad + 16;
    private const int BarY = NowY + 82;
    private const int BarW = PanelW - Pad * 2 - 32;
    private const int BarH = 10;

    private const int ControlsY = NowY + NowH + 14;
    private const int LibraryHeaderY = ControlsY + 58;
    private const int ListX = Pad;
    private const int ListY = LibraryHeaderY + 26;
    private const int ListWidth = PanelW - Pad * 2;
    private const int RowH = 38;
    private const int RowsPerPage = 5;
    private const int PagerY = ListY + RowH * RowsPerPage + 8;
    private const int FooterY = PanelH - Pad - 40;

    private const uint GoodColor = 0x6DBA79;
    private const uint BadColor = 0xE67146;

    private readonly SimpleText _title;
    private readonly SimpleText _by;
    private readonly SimpleText _elapsed;
    private readonly SimpleText _length;
    private readonly SimpleText _count;
    private readonly SimpleText _pageText;
    private readonly SimpleText _status;
    private readonly ColorRect _fill;
    private readonly Container _list = new();
    private readonly Container _prevPage;
    private readonly Container _nextPage;

    private MusicNowData _shown;
    private int _shownSerial = -1;
    private int _page;
    private bool _busy;

    public override (int Width, int Height)? FixedSize => (PanelW, PanelH);

    public JukeboxView() {
        AddChild(WaWStyle.Panel(PanelW, PanelH));
        AddChild(OptionsStyle.Label("Jukebox", FontGroup.MyriadPro, 30f, PanelW / 2, 34, UiAnchor.Middle, WaWStyle.Highlight, 1));
        AddChild(OptionsStyle.Label("One playlist for everyone in the game. It shuffles all day.", FontGroup.MyriadPro, 15f, PanelW / 2, 62, UiAnchor.Middle, OptionsStyle.Tan, 1));

        // now playing
        var card = WaWStyle.Slot(PanelW - Pad * 2, NowH);
        card.X = Pad;
        card.Y = NowY;
        AddChild(card);
        AddChild(OptionsStyle.Label("NOW PLAYING", FontGroup.MyriadPro, 13f, Pad + 16, NowY + 12, UiAnchor.LeftTop, OptionsStyle.Tan, 1));
        _title = new SimpleText(new TextConfig {
            Text = "Loading...",
            FontSize = 26,
            FontType = FontType.Bold,
            FontGroup = FontGroup.MyriadPro,
            Color = OptionsStyle.Gold,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            X = Pad + 16,
            Y = NowY + 30,
            MaxWidth = PanelW - Pad * 2 - 32,
            Anchor = UiAnchor.LeftTop
        });
        AddChild(_title);
        _by = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 14f, Pad + 16, NowY + 62, UiAnchor.LeftTop, OptionsStyle.Tan, 1);
        AddChild(_by);

        AddChild(new ColorRect(new ColorRectConfig { X = BarX, Y = BarY, Width = BarW, Height = BarH, Color = 0x120E23 }));
        _fill = new ColorRect(new ColorRectConfig { X = BarX, Y = BarY, Width = 1, Height = BarH, Color = OptionsStyle.Gold });
        AddChild(_fill);
        _elapsed = OptionsStyle.Label("0:00", FontGroup.MyriadPro, 14f, BarX, BarY + 24, UiAnchor.LeftTop, OptionsStyle.Cream, 1);
        AddChild(_elapsed);
        _length = OptionsStyle.Label("0:00", FontGroup.MyriadPro, 14f, BarX + BarW, BarY + 24, UiAnchor.RightTop, OptionsStyle.Cream, 1);
        AddChild(_length);

        // back / forward
        var back = WaWStyle.TextButton("<  Previous", 190, 44, 19f, () => Skip(false));
        back.X = Pad;
        back.Y = ControlsY;
        AddChild(back);
        var forward = WaWStyle.TextButton("Next  >", 190, 44, 19f, () => Skip(true));
        forward.X = PanelW - Pad - 190;
        forward.Y = ControlsY;
        AddChild(forward);
        AddChild(OptionsStyle.Label("Everyone hears what you pick", FontGroup.MyriadPro, 14f, PanelW / 2, ControlsY + 22, UiAnchor.Middle, OptionsStyle.Tan, 1));

        // the library
        AddChild(OptionsStyle.Label("Library", FontGroup.MyriadPro, 19f, Pad, LibraryHeaderY, UiAnchor.LeftTop, WaWStyle.Highlight, 1));
        _count = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 14f, PanelW - Pad, LibraryHeaderY + 4, UiAnchor.RightTop, OptionsStyle.Tan, 1);
        AddChild(_count);
        _list.X = ListX;
        _list.Y = ListY;
        AddChild(_list);

        _prevPage = WaWStyle.TextButton("< Prev", 100, 28, 15f, () => ShowPage(_page - 1));
        _prevPage.X = Pad;
        _prevPage.Y = PagerY;
        AddChild(_prevPage);
        _nextPage = WaWStyle.TextButton("More >", 100, 28, 15f, () => ShowPage(_page + 1));
        _nextPage.X = PanelW - Pad - 100;
        _nextPage.Y = PagerY;
        AddChild(_nextPage);
        _pageText = OptionsStyle.Label(string.Empty, FontGroup.MyriadPro, 15f, PanelW / 2, PagerY + 14, UiAnchor.Middle, OptionsStyle.Tan, 1);
        AddChild(_pageText);
        _prevPage.Visible = false;
        _nextPage.Visible = false;

        _status = new SimpleText(new TextConfig {
            Text = string.Empty,
            FontSize = 16,
            FontType = FontType.Normal,
            FontGroup = FontGroup.MyriadPro,
            Color = WaWStyle.Text,
            OutlineColor = OptionsStyle.OutlineDark,
            OutlineThickness = 1,
            X = Pad,
            Y = FooterY + 20,
            MaxWidth = PanelW - Pad * 2 - 140,
            Anchor = UiAnchor.MiddleLeft
        });
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

        InGameMusic.Refresh();
    }

    // The game stops reading the keyboard while the window is open; the caller already switched that off.
    public override void CloseOverlay() {
        UserInput.SetManualFocus(true);
        base.CloseOverlay();
    }

    private void OnKeyDown(KeyboardEvent args) {
        if (args.Code == Settings.Options.Key || args.Code == Scancode.Escape) {      // Escape always closes; the options key is O since 2026-09-22
            CloseOverlay();
        }
    }

    // ---- showing the state -----------------------------------------------------------------------------------------------------------------------

    private void OnFrame() {
        var state = InGameMusic.State;
        if (state == null) {
            return;
        }

        if (state.Serial != _shownSerial || _shown == null || state.Tracks.Count != _shown.Tracks.Count) {
            var first = _shown == null;
            _shown = state;
            _shownSerial = state.Serial;
            var current = state.Find(state.CurrentId);
            _title.SetText(current.Title);
            _by.SetText(state.ChangedBy == null ? "Shuffling" : $"Picked by {state.ChangedBy}");
            _length.SetText(MusicNowData.FormatTime(current.LengthMs));
            _count.SetText($"{state.Tracks.Count} song{(state.Tracks.Count == 1 ? "" : "s")}");
            if (first) {
                _page = Math.Max(0, IndexOf(state, state.CurrentId)) / RowsPerPage;
            }

            ShowPage(_page);
        }

        var length = _shown.Find(_shown.CurrentId).LengthMs;
        var position = Math.Clamp(InGameMusic.PositionMs, 0, length);
        _fill.Resize(Math.Max(1, (int)(BarW * position / length)), BarH);
        _elapsed.SetText(MusicNowData.FormatTime(position));
    }

    private static int IndexOf(MusicNowData state, string id) {
        for (var i = 0; i < state.Tracks.Count; i++) {
            if (state.Tracks[i].Id == id) {
                return i;
            }
        }

        return -1;
    }

    private void ShowPage(int page) {
        if (_shown == null) {
            return;
        }

        var pages = Math.Max(1, (_shown.Tracks.Count + RowsPerPage - 1) / RowsPerPage);
        _page = Math.Clamp(page, 0, pages - 1);

        while (_list.NumChildren > 0) {
            _list.RemoveChildAt(0);
        }

        for (var i = _page * RowsPerPage; i < Math.Min(_shown.Tracks.Count, (_page + 1) * RowsPerPage); i++) {
            var row = BuildRow(_shown.Tracks[i], i + 1, _shown.Tracks[i].Id == _shown.CurrentId);
            row.Y = (i - _page * RowsPerPage) * RowH;
            _list.AddChild(row);
        }

        _prevPage.Visible = pages > 1 && _page > 0;
        _nextPage.Visible = pages > 1 && _page < pages - 1;
        _pageText.SetText(pages > 1 ? $"Page {_page + 1} of {pages}" : string.Empty);
    }

    private Container BuildRow(MusicTrackInfo track, int number, bool playing) {
        var row = new Container { MouseEnabled = true };
        var background = new ColorRect(new ColorRectConfig { Width = ListWidth, Height = RowH - 4, Color = playing ? OptionsStyle.Gold : 0xFFFFFF, Alpha = playing ? 0.22f : 0f });
        row.AddChild(background);
        row.AddChild(OptionsStyle.Label($"{number}.", FontGroup.MyriadPro, 16f, 14, (RowH - 4) / 2, UiAnchor.MiddleLeft, OptionsStyle.Tan, 1));
        row.AddChild(OptionsStyle.Label(track.Title, FontGroup.MyriadPro, 18f, 48, (RowH - 4) / 2, UiAnchor.MiddleLeft, playing ? OptionsStyle.Gold : OptionsStyle.Cream, 1));
        row.AddChild(OptionsStyle.Label(MusicNowData.FormatTime(track.LengthMs), FontGroup.MyriadPro, 15f, ListWidth - 14, (RowH - 4) / 2, UiAnchor.MiddleRight, OptionsStyle.Tan, 1));
        if (playing) {
            row.AddChild(OptionsStyle.Label("PLAYING", FontGroup.MyriadPro, 13f, ListWidth - 96, (RowH - 4) / 2, UiAnchor.MiddleRight, OptionsStyle.Gold, 1));
        }

        if (!playing) {
            row.AddEventListener(MouseEvent.MouseOver, () => background.Alpha = 0.12f);
            row.AddEventListener(MouseEvent.MouseOut, () => background.Alpha = 0f);
        }

        var leftDown = false;
        row.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        row.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                Pick(track);
            }

            leftDown = false;
        });
        return row;
    }

    // ---- changing the music ----------------------------------------------------------------------------------------------------------------------

    private void SetStatus(string text, bool good) {
        _status.SetText(text);
        _status.SetColor(good ? GoodColor : BadColor);
    }

    private void Skip(bool forward) {
        if (_busy) {
            return;
        }

        _busy = true;
        AddEventListener(AppRequests.SkipMusic(forward), OnChanged);
    }

    private void Pick(MusicTrackInfo track) {
        if (_busy) {
            return;
        }

        if (_shown != null && track.Id == _shown.CurrentId) {
            SetStatus("That song is already playing.", false);
            return;
        }

        _busy = true;
        AddEventListener(AppRequests.SetMusicTrack(track.Id), OnChanged);
    }

    private void OnChanged(AppResponse response) {
        _busy = false;
        if (response.Success) {
            SetStatus(string.Empty, true);       // no confirmation: the new song simply starts (only a refusal is worth saying)
            InGameMusic.Refresh();
        } else {
            SetStatus(response.Message ?? "That did not work.", false);
        }
    }
}
