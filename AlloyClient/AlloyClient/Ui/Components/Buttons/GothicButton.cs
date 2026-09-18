using System;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using OpenTK.Mathematics;

namespace AlloyClient.Ui.Components.Buttons;

// Themed nine-sliced button for the "Dark Dwellers" character select / class creation redesign -
// plain TextButton has no background of its own (see MenuBarButton/TitleMenuButton, which rely on
// sitting over existing art instead), so this pairs a NineSliceRect plate with the same
// hover/press handling TextButton uses.
public struct GothicButtonConfig {
    public string Text = "";
    public float FontSize = 22f;
    public Action OnClicked = null;
    public FontGroup FontGroup = FontGroup.CrunchyFont;
    public string SliceData = SliceLibrary.DarkDwellersButton;
    public int CutX = 16;
    public int CutY = 8;
    public int Width = 140;
    public int Height = 36;
    public int X = 0;
    public int Y = 0;
    public UiAnchor Anchor = UiAnchor.LeftTop;
    public uint TextColor = 0xFFFFFF;
    public uint HoverColor = 0xFFDC85;
    public uint OutlineColor = 0x000000;
    public float OutlineThickness = 3f;
    public float HoverScale = 1.05f;

    public GothicButtonConfig() { }
}

public sealed class GothicButton : Container {

    private readonly SimpleText _text;
    private readonly Action _onClicked;
    private readonly uint _textColor;
    private readonly uint _hoverColor;
    private readonly float _hoverScale;

    private bool _leftDown;

    public GothicButton(GothicButtonConfig config) : base(new ContainerConfig {
        X = config.X, Y = config.Y, Width = config.Width, Height = config.Height, Anchor = config.Anchor
    }) {
        _onClicked = config.OnClicked;
        _textColor = config.TextColor;
        _hoverColor = config.HoverColor;
        _hoverScale = config.HoverScale;

        var background = new NineSliceRect(new NineSliceConfig {
            SliceData = config.SliceData,
            CutX = config.CutX,
            CutY = config.CutY,
            Width = config.Width,
            Height = config.Height
        });
        AddChild(background);

        _text = new SimpleText(new TextConfig {
            Text = config.Text,
            FontSize = config.FontSize,
            FontType = FontType.Normal,
            FontGroup = config.FontGroup,
            Color = _textColor,
            OutlineColor = config.OutlineColor,
            OutlineThickness = config.OutlineThickness,
            X = config.Width / 2,
            Y = config.Height / 2,
            Anchor = UiAnchor.Middle
        });
        AddChild(_text);

        MouseEnabled = true;
        AddEventListener(MouseEvent.MouseOver, OnMouseOver);
        AddEventListener(MouseEvent.MouseOut, OnMouseOut);
        AddEventListener(MouseEvent.LeftDown, () => _leftDown = true);
        AddEventListener(MouseEvent.LeftUp, OnLeftUp);
    }

    private void OnMouseOver() {
        _text.SetColor(_hoverColor);
        Scale = new Vector2(_hoverScale);
    }

    private void OnMouseOut() {
        _text.SetColor(_textColor);
        Scale = Vector2.One;
    }

    private void OnLeftUp() {
        if (_leftDown) {
            _onClicked?.Invoke();
        }
        _leftDown = false;
    }
}
