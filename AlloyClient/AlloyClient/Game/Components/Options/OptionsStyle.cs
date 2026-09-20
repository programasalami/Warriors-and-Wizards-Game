using System;
using AlloyClient.Ui;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Game.Components.Options;

// The look of the in-game options menu: the same walnut panels, slots and parchment scrolls as the character creation
// popup (ClassContainer), with the same two fonts - BitPotion for labels and values, NotJamSignature21 for the title and
// the scroll buttons.
public static class OptionsStyle {
    // The pack's palette (Mini Medieval): gold-sand, the UI tan and a dimmer tan, on near-black outlines.
    public const uint Gold = 0xEBB85B;
    public const uint Cream = 0xDACEA4;
    public const uint Tan = 0xAEA47E;
    public const uint ParchmentInk = 0x2A1C14;
    public const uint OutlineDark = 0x120E23;

    public static readonly ColorTransform NormalTint = new(1f, 1f, 1f, 1f);
    public static readonly ColorTransform SlotHoverTint = new(1.4f, 1.34f, 1.22f, 1f);
    public static readonly ColorTransform ParchmentHoverTint = new(0.86f, 0.8f, 0.7f, 1f);
    public static readonly ColorTransform ParchmentDimTint = new(0.62f, 0.55f, 0.47f, 1f);

    public const int SlotCut = 8;
    public const int ScrollCut = 12;

    public static SimpleText Label(string text, FontGroup font, float size, int x, int y, UiAnchor anchor, uint color = Cream, int outline = 1) {
        return new SimpleText(new TextConfig {
            Text = text,
            FontSize = size,
            FontType = FontType.Bold,
            FontGroup = font,
            Color = color,
            OutlineColor = OutlineDark,
            OutlineThickness = outline,
            X = x,
            Y = y,
            Anchor = anchor
        });
    }

    // The in-game GUI's box and slot: the "Mini Medieval" pack's gem-cornered frame over dark brown, and its inventory slot (see WaWStyle).
    public static NineSliceRect Panel(int width, int height) => Hud.WaWStyle.Panel(width, height);

    public static NineSliceRect Slot(int width, int height) => Hud.WaWStyle.Slot(width, height);
}

// A parchment scroll with dark-ink text (the creation popup's CANCEL / CREATE look). Used for the options menu's tabs (which
// stay dimmed until selected) and its bottom buttons.
public sealed class ParchmentButton : Container {
    private readonly NineSliceRect _plate;
    private bool _selected;
    private bool _hovered;
    private bool _down;

    public readonly string Key;

    public ParchmentButton(string text, string key, int width, int height, float fontSize, Action onClicked, bool startSelected = true) {
        Key = key;
        MouseEnabled = true;

        _plate = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesParchment,
            CutX = OptionsStyle.ScrollCut,
            CutY = OptionsStyle.ScrollCut,
            Width = width,
            Height = height
        });
        AddChild(_plate);
        AddChild(OptionsStyle.Label(text, FontGroup.MyriadPro, fontSize, width / 2, height / 2, UiAnchor.Middle, OptionsStyle.ParchmentInk));

        _selected = startSelected;
        UpdateTint();

        AddEventListener(MouseEvent.MouseOver, () => { _hovered = true; UpdateTint(); });
        AddEventListener(MouseEvent.MouseOut, () => { _hovered = false; _down = false; UpdateTint(); });
        AddEventListener(MouseEvent.LeftDown, () => _down = true);
        AddEventListener(MouseEvent.LeftUp, () => {
            if (_down) {
                onClicked?.Invoke();
            }
            _down = false;
        });
    }

    public void SetSelected(bool selected) {
        _selected = selected;
        UpdateTint();
    }

    private void UpdateTint() {
        if (_selected) {
            _plate.ColorTransformation = _hovered ? OptionsStyle.ParchmentHoverTint : OptionsStyle.NormalTint;
        } else {
            // An unselected tab lights up to the full scroll colour while hovered.
            _plate.ColorTransformation = _hovered ? OptionsStyle.NormalTint : OptionsStyle.ParchmentDimTint;
        }
    }
}
