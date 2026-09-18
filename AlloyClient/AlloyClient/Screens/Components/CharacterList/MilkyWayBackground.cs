using System;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens.Components.CharacterList;

// "The Muster Hall" background (2026-09-17, sixth pass) - the fifth pass built this as a single
// dominant galaxy spiral plus a modest star scatter, matching one LockVenture reference gif. Per
// a follow-up request pointing at a *different* reference (a dense, uniform field of twinkling
// stars with no galaxy shape at all, same @HumblePixel pack family as the Console), the spiral
// (and its bright "core" accent star) were dropped entirely in favor of just a much denser star
// scatter - reusing the same star sprites already extracted from the Milky Way pack rather than
// sourcing a whole new asset pack for what's fundamentally the same kind of content, just without
// the spiral centerpiece.
//
// Lives outside CharacterListScreen's _root and exposes Resize(width, height) for that screen's
// own OnResize to call directly (same pattern TitleScreenBase already uses for its own background
// ColorRect) - a full-screen backdrop needs to actually stretch-fill the real window on every
// resize, unlike the rest of that screen's content which shares a single uniform
// Stage.ScreenScale and is fine leaving letterboxing slack (buttons/text must never stretch
// non-uniformly, a backdrop can). Stars end up very slightly non-circular on an off-16:9 window
// as a result - an acceptable trade-off for guaranteed full coverage.
public sealed class MilkyWayBackground : Container {

    private const uint SpaceBlack = 0x040209;

    private const int StarFrameCount = 7;
    private const int StarFrameMs = 140;
    private const int NumTwinkleStars = 55;
    private const int TwinkleStarMinSize = 10;
    private const int TwinkleStarMaxSize = 18;

    private const int NumStaticStars = 220;
    private const int StaticStarVariantCount = 8;
    private const int StaticStarMinSize = 4;
    private const int StaticStarMaxSize = 12;

    // Fixed seed - a deliberately composed starfield that looks the same every visit, not a
    // reshuffle each time the screen loads.
    private const int LayoutSeed = 1337;

    private readonly ObjectRect[] _twinkleStars;
    private readonly int[] _twinkleFrameOffsetMs;

    public MilkyWayBackground() : base(new ContainerConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight }) {
        var rng = new Random(LayoutSeed);

        var backdrop = new ColorRect(new ColorRectConfig {
            Width = Settings.DefaultScreenWidth,
            Height = Settings.DefaultScreenHeight,
            Color = SpaceBlack
        });
        AddChild(backdrop);

        // Densest layer added first (bottom), so twinkling stars/core sit visually on top of it.
        for (var i = 0; i < NumStaticStars; i++) {
            var variant = rng.Next(0, StaticStarVariantCount);
            var size = rng.Next(StaticStarMinSize, StaticStarMaxSize + 1);
            var star = new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromUiAtlas($"MilkyWay/StaticStar{variant}", 0, false),
                Width = size,
                Height = size,
                X = rng.Next(0, Settings.DefaultScreenWidth),
                Y = rng.Next(0, Settings.DefaultScreenHeight),
                Alpha = 0.55f + (float)rng.NextDouble() * 0.4f,
                OutlineEnabled = false,
                GlowEnabled = false
            });
            AddChild(star);
        }

        _twinkleStars = new ObjectRect[NumTwinkleStars];
        _twinkleFrameOffsetMs = new int[NumTwinkleStars];
        for (var i = 0; i < NumTwinkleStars; i++) {
            var size = rng.Next(TwinkleStarMinSize, TwinkleStarMaxSize + 1);
            var star = new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromUiAtlas("MilkyWay/Star0", 0, false),
                Width = size,
                Height = size,
                X = rng.Next(0, Settings.DefaultScreenWidth),
                Y = rng.Next(0, Settings.DefaultScreenHeight),
                OutlineEnabled = false,
                GlowEnabled = false
            });
            AddChild(star);
            _twinkleStars[i] = star;
            _twinkleFrameOffsetMs[i] = rng.Next(0, StarFrameCount * StarFrameMs);
        }

        AddEventListener(Event.EnterFrame, OnFrameEnter);
    }

    // Stretches this element's fixed 1280x720 internal layout to exactly fill the real window -
    // see the class comment above for why this can't just be Stage.ScreenScale like the rest of
    // the screen's content.
    public void Resize(int width, int height) {
        ScaleX = width / (float)Settings.DefaultScreenWidth;
        ScaleY = height / (float)Settings.DefaultScreenHeight;
    }

    private void OnFrameEnter() {
        var totalMs = (int)Stage.GameTime.TotalMs;

        for (var i = 0; i < _twinkleStars.Length; i++) {
            var frame = (totalMs + _twinkleFrameOffsetMs[i]) / StarFrameMs % StarFrameCount;
            _twinkleStars[i].ChangeTexture(TextureHelper.FromUiAtlas($"MilkyWay/Star{frame}", 0, false));
        }
    }
}
