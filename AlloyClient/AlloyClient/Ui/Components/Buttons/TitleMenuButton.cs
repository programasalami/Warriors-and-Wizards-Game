using System;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Utils;

namespace AlloyClient.Ui.Components.Buttons;

/// <summary>
/// A title screen menu entry: an icon (the visually prominent part) followed by its word, no
/// background of its own - meant to sit inside a shared frame (see TitleScreen). Scale is
/// expected to come from an already-scaled parent container, not set directly on this.
/// </summary>
public sealed class TitleMenuButton : Sprite {

    private const int IconGap = 10;

    private readonly MenuBarButton _text;
    private readonly ObjectRect _icon;

    public int ContentWidth { get; }
    public int ContentHeight { get; }

    public TitleMenuButton(string text, string iconLookup, Action onClicked, float fontSize, int iconSize) {
        _text = new MenuBarButton(new TextButtonConfig {
            Text = text,
            FontSize = fontSize,
            OnClicked = onClicked,
            OutlineThickness = 4,
            FontGroup = FontGroup.CrunchyFont,
            FontType = FontType.Normal,
            ActiveColor = 0x000000
        });
        _icon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(iconLookup, 0, false),
            Width = iconSize,
            Height = iconSize,
            OutlineEnabled = false,
            GlowEnabled = false,
            Anchor = UiAnchor.Middle
        });

        ContentWidth = iconSize + IconGap + _text.Width;
        ContentHeight = Math.Max(_text.Height, iconSize);

        // Content starts at this element's own local X = 0 (the icon's left edge) rather than
        // being centered around it, so the owning screen can line several of these up on a
        // shared left edge instead of each one centering independently at a different width.
        _icon.X = iconSize / 2;
        _icon.Y = 0;

        _text.SetAnchor(UiAnchor.Middle);
        _text.X = iconSize + IconGap + _text.Width / 2;
        _text.Y = 0;

        AddChild(_icon);
        AddChild(_text);
    }
}
