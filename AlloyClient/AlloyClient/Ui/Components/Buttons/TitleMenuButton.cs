using System;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Utils;

namespace AlloyClient.Ui.Components.Buttons;

/// <summary>
/// A title screen menu entry: just its word, no background of its own - meant to sit inside a shared frame (see
/// TitleScreen), centred on its own local X = 0. While the mouse is over it, a small sword swings in on each side of the
/// word, both with their tips pointing at it (a hover indicator / cursor). Scale is expected to come from an already-scaled
/// parent container, not set directly on this.
/// </summary>
public sealed class TitleMenuButton : Sprite {

    // The sword pointer (Icons/TitleSwordPointer, the desktop sword icon turned a quarter turn so its tip points left).
    // Design px; the art is 64x40, so this keeps each art pixel about the same size on screen. The one on the word's left is
    // the same art mirrored, so its tip points right.
    private const int PointerWidth = 36;
    private const int PointerHeight = 23;
    private const int PointerGap = 10;

    // Swing-in time, how far (design px) each sword slides in from outside while appearing, and how far it turns (radians)
    // before settling: it starts flipped over (half a turn), swings round and overshoots a touch before coming to rest.
    private const float PointerSwingMs = 300f;
    private const int PointerSlidePx = 16;
    private const float PointerStartTurn = MathF.PI;

    private readonly MenuBarButton _text;
    private readonly ObjectRect _rightPointer;
    private readonly ObjectRect _leftPointer;

    private bool _hover;
    private float _pointerT;
    private double _lastMs = -1;

    public int ContentWidth { get; }
    public int ContentHeight { get; }

    public TitleMenuButton(string text, Action onClicked, float fontSize, int rowHeight, uint hoverColor) {
        _text = new MenuBarButton(new TextButtonConfig {
            Text = text,
            FontSize = fontSize,
            OnClicked = onClicked,
            OutlineThickness = 0,
            FontGroup = FontGroup.MyriadPro,
            FontType = FontType.Normal,
            ActiveColor = 0x000000,
            HoverColor = hoverColor
        });

        ContentWidth = _text.Width;
        ContentHeight = Math.Max(_text.Height, rowHeight);

        // The word is centred on this element's own local X = 0, so the owning screen can put a whole column of them on the
        // middle of the scroll.
        _text.SetAnchor(UiAnchor.Middle);
        _text.X = 0;
        _text.Y = 0;

        _rightPointer = BuildPointer(false);
        _leftPointer = BuildPointer(true);
        PlacePointers(0f);

        AddChild(_text);
        AddChild(_leftPointer);
        AddChild(_rightPointer);

        _text.AddEventListener(MouseEvent.MouseOver, () => _hover = true);
        _text.AddEventListener(MouseEvent.MouseOut, () => _hover = false);
        AddEventListener(Event.EnterFrame, OnFrame);
    }

    private static ObjectRect BuildPointer(bool mirrored) => new(new ObjectRectConfig {
        Texture = TextureHelper.FromUiAtlas("Icons/TitleSwordPointer", 0, false),
        Width = PointerWidth,
        Height = PointerHeight,
        Alpha = 0f,
        OutlineEnabled = false,
        GlowEnabled = false,
        Anchor = UiAnchor.Middle
    }) { FlipX = mirrored };

    // t runs 0 (hidden) to 1 (in place). The slide and fade ease with a smoothstep; the turn uses an ease-out-back curve, so
    // it whips round past straight and settles.
    private void PlacePointers(float t) {
        var eased = t * t * (3f - 2f * t);
        var reach = ContentWidth / 2 + PointerGap + PointerWidth / 2 + (int)Math.Round((1f - eased) * PointerSlidePx);

        var turn = (1f - EaseOutBack(t)) * PointerStartTurn;

        _rightPointer.X = reach;
        _rightPointer.Rotation = turn;
        _rightPointer.Alpha = eased;

        _leftPointer.X = -reach;
        _leftPointer.Rotation = -turn;
        _leftPointer.Alpha = eased;
    }

    private static float EaseOutBack(float t) {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private void OnFrame() {
        var now = (double)Stage.GameTime.TotalMs;
        var dt = _lastMs < 0 ? 0f : (float)Math.Min(now - _lastMs, 100.0);
        _lastMs = now;

        var target = _hover ? 1f : 0f;
        if (_pointerT == target) {
            return;
        }

        var step = dt / PointerSwingMs;
        _pointerT = _hover ? Math.Min(1f, _pointerT + step) : Math.Max(0f, _pointerT - step);
        PlacePointers(_pointerT);
    }
}
