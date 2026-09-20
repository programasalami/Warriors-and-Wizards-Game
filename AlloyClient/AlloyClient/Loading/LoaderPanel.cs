using System;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using OpenTK.Mathematics;

namespace AlloyClient.Loading;

// The loader's picture: the logo (Desktop/LoaderLogo.png) dead centre of the 1280x720 design canvas and a progress bar
// right under it. It only DISPLAYS a progress value it is given (Advance) - what that value means (a LoadPlan, the
// world-load milestones) is up to whoever owns it. The bar follows the real value smoothly but never fills faster
// than MinFillMs from empty to full, so a load that happens to finish instantly still reads as a bar filling
// rather than teleporting.
public sealed class LoaderPanel : Container {

    private const int CanvasWidth = 1280;
    private const int LogoWidth = 420;
    private const int LogoHeight = 336;
    private const int LogoCenterY = 340;

    private const int BarWidth = 420;
    private const int BarHeight = 14;
    private const int BarTop = LogoCenterY + LogoHeight / 2 + 26;

    private const uint BarBorderColor = 0x3A2452;
    private const uint BarTrackColor = 0x0C0714;
    private const uint BarFillColor = 0x9A3FE8;
    private const uint BarHighlightColor = 0xC98BFF;
    private const uint BarShineColor = 0xFFFFFF;

    private const float ShineWidth = 54f;
    private const double MinFillMs = 700;       // fastest the bar may go from empty to full
    private const double CatchUpMs = 110;       // smoothing time constant toward the real value
    private const double MaxFrameMs = 50;       // a single long frame must not leap the bar

    private readonly ObjectRect _logo;
    private readonly ColorRect _fill;
    private readonly ColorRect _fillHighlight;
    private readonly ColorRect _shine;
    private readonly ColorRect _cap;

    private double _animMs;
    private double _shineMs;

    public float Displayed { get; private set; }

    public LoaderPanel() {
        _logo = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Loader/Logo", 0, false),
            X = CanvasWidth / 2,
            Y = LogoCenterY,
            Width = LogoWidth,
            Height = LogoHeight,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_logo);

        var barLeft = (CanvasWidth - BarWidth) / 2;
        AddChild(new ColorRect(new ColorRectConfig { Width = BarWidth + 4, Height = BarHeight + 4, Color = BarBorderColor }) { X = barLeft - 2, Y = BarTop - 2 });
        AddChild(new ColorRect(new ColorRectConfig { Width = BarWidth, Height = BarHeight, Color = BarTrackColor }) { X = barLeft, Y = BarTop });

        _fill = new ColorRect(new ColorRectConfig { Width = 1, Height = BarHeight, Color = BarFillColor }) { X = barLeft, Y = BarTop };
        AddChild(_fill);
        _fillHighlight = new ColorRect(new ColorRectConfig { Width = 1, Height = 4, Color = BarHighlightColor, Alpha = 0.55f }) { X = barLeft, Y = BarTop };
        AddChild(_fillHighlight);
        _shine = new ColorRect(new ColorRectConfig { Width = 1, Height = BarHeight, Color = BarShineColor, Alpha = 0.22f }) { X = barLeft, Y = BarTop };
        AddChild(_shine);
        _cap = new ColorRect(new ColorRectConfig { Width = 3, Height = BarHeight, Color = 0xF3E2FF }) { X = barLeft, Y = BarTop };
        AddChild(_cap);

        Render(0f);
    }

    // Idle animation only (logo breathing) - use before the real work has started.
    public void Idle(double dtMs) {
        _animMs += Math.Min(dtMs, MaxFrameMs);
        _logo.Scale = new Vector2(1f + 0.012f * MathF.Sin((float) (_animMs * 0.0018)));
    }

    // Moves the displayed bar toward `target` (0..1, real progress) and redraws it.
    public void Advance(double dtMs, float target) {
        var dt = Math.Min(dtMs, MaxFrameMs);
        Idle(dt);
        _shineMs += dt;

        target = Math.Clamp(target, 0f, 1f);
        var delta = (target - Displayed) * (1f - MathF.Exp((float) (-dt / CatchUpMs)));
        delta = Math.Min(delta, (float) (dt / MinFillMs));
        Displayed += Math.Max(0f, delta);

        if (target >= 1f && 1f - Displayed < 0.004f) {
            Displayed = 1f;
        }

        Render(Displayed);
    }

    private void Render(float progress) {
        var barLeft = (CanvasWidth - BarWidth) / 2;
        var fillW = Math.Max(1, (int) (BarWidth * progress));
        _fill.Resize(fillW, BarHeight);
        _fillHighlight.Resize(fillW, 4);

        // A soft highlight sweeping along the filled part, clipped by hand to it, so a waiting bar never looks frozen.
        var sweep = (float) ((_shineMs * 0.32) % (BarWidth + ShineWidth)) - ShineWidth;
        var left = Math.Max(0f, sweep);
        var right = Math.Min(fillW, sweep + ShineWidth);
        _shine.Visible = right > left;
        if (_shine.Visible) {
            _shine.X = barLeft + (int) left;
            _shine.Resize(Math.Max(1, (int) (right - left)), BarHeight);
        }

        _cap.X = barLeft + Math.Max(0, fillW - 3);
    }
}
