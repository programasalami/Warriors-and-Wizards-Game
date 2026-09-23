using System;
using WaWClient.Game.Components.Options;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Extra;

namespace WaWClient.Game.Components.Hud;

// A walnut frame with a gold title (and the line under it) and a close button that fades in over the game when its side tab is clicked. A popup made with an
// empty title has no header at all: no title text and no line.
public abstract class HudPopup : Sprite {
    private const int FadeMs = 170;

    private readonly SimpleText _title;
    private string _titleText;

    private NineSliceRect _panel;
    private ColorRect _divider;
    private Container _close;

    public int PopupWidth { get; private set; }
    public int PopupHeight { get; private set; }

    public bool IsOpen { get; private set; }

    // Called when the popup closes itself (the close button) so the tab can un-highlight.
    public Action OnClosed;

    protected HudPopup(string title, int width, int height) {
        PopupWidth = width;
        PopupHeight = height;

        _panel = WaWStyle.Panel(width, height);
        AddChild(_panel);
        _title = OptionsStyle.Label(title, FontGroup.MyriadPro, 24f, width / 2, 26, UiAnchor.Middle, OptionsStyle.Gold, 2);
        AddChild(_title);
        _divider = new ColorRect(new ColorRectConfig { X = 16, Y = 48, Width = width - 32, Height = 2, Color = OptionsStyle.Tan, Alpha = 0.5f });
        _divider.Visible = !string.IsNullOrEmpty(title);
        AddChild(_divider);
        _close = WaWStyle.CloseButton(Close);
        _close.X = width - _close.Width - 12;
        _close.Y = 10;
        AddChild(_close);

        Alpha = 0f;
        Visible = false;
    }

    // Gives the popup a new size (its frame, title, divider and close button follow).
    public void Resize(int width, int height) {
        if (width == PopupWidth && height == PopupHeight) {
            return;
        }

        PopupWidth = width;
        PopupHeight = height;
        RemoveChild(_panel);
        _panel = WaWStyle.Panel(width, height);
        AddChildAt(_panel, 0);
        _title.X = width / 2;
        _divider.Resize(width - 32, 2);
        _close.X = width - _close.Width - 12;
        OnResized();
    }

    protected virtual void OnResized() { }

    public void Toggle() {
        if (IsOpen) {
            Close();
        } else {
            Open();
        }
    }

    public void Open() {
        if (IsOpen) {
            return;
        }

        IsOpen = true;
        Visible = true;
        OnOpened();
        GTween.Add(Tween.New(this, Easing.SineInOut, FadeMs, 1f, EaseType.Alpha));
    }

    public void Close() {
        if (!IsOpen) {
            return;
        }

        IsOpen = false;
        OnClosed?.Invoke();
        GTween.Add(Tween.New(this, Easing.SineInOut, FadeMs, 0f, EaseType.Alpha, 0, () => {
            if (!IsOpen) {
                Visible = false;
            }
        }));
    }

    // Changes the header (only touches the text when it really changed).
    protected void SetTitle(string text) {
        if (text == _titleText) {
            return;
        }

        _titleText = text;
        _title.SetText(text);
    }

    protected virtual void OnOpened() { }

    // Called every frame while open.
    public virtual void Refresh() { }
}
