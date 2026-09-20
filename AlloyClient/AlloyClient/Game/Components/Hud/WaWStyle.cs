using System;
using AlloyClient.Game.Components.Options;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Game.Components.Hud;

// The look of the in-game GUI: the "Mini Medieval User Interface" pack (gem-cornered orange frames over dark brown). The art is small
// pixel art, so every piece is drawn at a whole-number scale to keep its pixels square: panels and slots at 2x, the bars at 3x (their
// thin ones at 2x), the portrait frame at 4x, icons at 3x.
public static class WaWStyle {
    public const int PanelScale = 2;
    public const int BarScale = 3;

    // The pack's palette (Palette/Mini-Medieval-Palette-Text-Ref.md) and the bar strips' rows (light, mid, dark).
    public const uint Text = 0xDACEA4;
    public const uint TextDim = 0xAEA47E;
    public const uint Highlight = 0xFFF1A9;
    public const uint Ink = 0x120E23;

    public static readonly uint[] Red = [0xE67146, 0xB74132, 0x7A2849];
    public static readonly uint[] Teal = [0x6DBA79, 0x2A7D75, 0x24505F];
    public static readonly uint[] Olive = [0xC9C03D, 0x7E9432, 0x56642E];
    public static readonly uint[] Orange = [0xEBB85B, 0xE67146, 0xB74132];

    // size in screen (design) pixels; the art stays 2x, so odd sizes lose a pixel
    public static NineSliceRect Panel(int width, int height) => Scaled(SliceLibrary.WaWPanel, 8, 8, width, height, PanelScale);

    public static NineSliceRect Slot(int width, int height) => Scaled(SliceLibrary.WaWSlot, 6, 6, width, height, PanelScale);

    public static NineSliceRect Button(int width, int height) => Scaled(SliceLibrary.WaWButton, 4, 4, width, height, PanelScale);

    private static readonly ColorTransform ButtonLit = new(1.18f, 1.14f, 1.08f, 1f);
    private static readonly ColorTransform ButtonNormal = new(1f, 1f, 1f, 1f);

    // The pack's orange button plate with dark text, lighter when hovered.
    public static Container TextButton(string text, int width, int height, float fontSize, Action onClick) {
        var button = new Container { MouseEnabled = true };
        var plate = Button(width, height);
        button.AddChild(plate);
        button.AddChild(OptionsStyle.Label(text, FontGroup.MyriadPro, fontSize, width / 2, height / 2 - 1, UiAnchor.Middle, Ink, 0));
        WireHover(button, plate, onClick);
        return button;
    }

    // A small square button (a slot with one of the pack's icons in it), e.g. the minimap's zoom + / -.
    public static Container IconButton(string iconLookup, int nativeW, int nativeH, int iconScale, int size, Action onClick) {
        var button = new Container { MouseEnabled = true };
        var slot = Slot(size, size);
        button.AddChild(slot);
        var icon = Icon(iconLookup, nativeW, nativeH, iconScale);
        icon.X = (size - icon.Width) / 2;
        icon.Y = (size - icon.Height) / 2;
        button.AddChild(icon);
        WireHover(button, slot, onClick);
        return button;
    }

    private static void WireHover(Container button, NineSliceRect plate, Action onClick) {
        button.AddEventListener(MouseEvent.MouseOver, () => plate.ColorTransformation = ButtonLit);
        button.AddEventListener(MouseEvent.MouseOut, () => plate.ColorTransformation = ButtonNormal);
        button.AddEventListener(MouseEvent.LeftClick, () => onClick?.Invoke());
    }

    public static NineSliceRect Scaled(string slice, int cutX, int cutY, int width, int height, int scale) {
        var rect = new NineSliceRect(new NineSliceConfig { SliceData = slice, CutX = cutX, CutY = cutY, Width = width / scale, Height = height / scale });
        rect.Scale = new Vector2(scale, scale);
        return rect;
    }

    // An icon from the pack (WaW/Coin, WaW/Star, WaW/Close), drawn at `scale`; nativeW/nativeH are the art's size.
    public static ObjectRect Icon(string lookup, int nativeW, int nativeH, int scale) {
        return new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(lookup, 0, false),
            Width = nativeW * scale,
            Height = nativeH * scale,
            OutlineEnabled = false,
            GlowEnabled = false
        });
    }

    // The close button: a slot with the pack's orange X, lighter when hovered.
    public static Container CloseButton(Action onClick) {
        const int w = 36;
        const int h = 32;
        var button = new Container { MouseEnabled = true };
        var slot = Slot(w, h);
        button.AddChild(slot);
        var x = Icon("WaW/Close", 7, 6, 3);
        x.X = (w - x.Width) / 2;
        x.Y = (h - x.Height) / 2;
        button.AddChild(x);

        var lit = new ColorTransform(1.35f, 1.3f, 1.2f, 1f);
        var normal = new ColorTransform(1f, 1f, 1f, 1f);
        button.AddEventListener(MouseEvent.MouseOver, () => slot.ColorTransformation = lit);
        button.AddEventListener(MouseEvent.MouseOut, () => slot.ColorTransformation = normal);
        button.AddEventListener(MouseEvent.LeftClick, () => onClick?.Invoke());
        return button;
    }
}
