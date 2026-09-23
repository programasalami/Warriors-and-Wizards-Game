using System;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Data;
using OpenTK.Mathematics;

namespace WaWClient.Screens.Components;

// The main-menu logo: the animated clip (Desktop/AnimatedTitleLogo.mp4, baked into one sprite sheet by
// Tools/BookUi/build_title_logo_anim.py - see TitleLogoAnimData) played frame by frame. Everything worth tweaking is a
// constant right below. (There used to be a breathing scale pulse and a bobbing float on top of the clip; both were
// removed - the logo stays perfectly still and only the clip's own animation plays.)
//
// The sheet holds every 2nd frame of the 24 fps clip (12 fps stored, to keep the texture small). Two copies of the logo
// are stacked: the current frame at full opacity and the NEXT frame fading in over it, which turns the 12 fps stored
// frames back into a smooth blend - including at the loop point, where the last frame fades back into the first.
public sealed class AnimatedTitleLogo : Container {

    // ---- pace / looping ----------------------------------------------------------------------------------------------

    // 1 = the clip's own speed, 0.75 = slower and calmer, 1.25 = livelier.
    private const float PlaybackSpeed = 0.8f;

    // Extra time the loop rests on its first (idle, un-glowing) frame before the animation plays again. 0 = continuous.
    private const float LoopRestMs = 0f;

    private readonly Container _holder = new();
    private readonly ObjectRect _current;
    private readonly ObjectRect _next;

    private double _startMs = -1;
    private int _shownCurrent = -1;
    private int _shownNext = -1;

    public AnimatedTitleLogo(int width, int height) {
        _current = MakeLayer(width, height, 0);
        _next = MakeLayer(width, height, 1 % TitleLogoAnimData.FrameCount);
        _next.Alpha = 0f;
        _holder.AddChild(_current);
        _holder.AddChild(_next);
        AddChild(_holder);

        AddEventListener(Event.EnterFrame, OnFrame);
    }

    private static ObjectRect MakeLayer(int width, int height, int frame) => new(new ObjectRectConfig {
        Texture = FrameTexture(frame),
        X = 0,
        Y = 0,
        Width = width,
        Height = height,
        Anchor = UiAnchor.Middle,
        OutlineEnabled = false,
        GlowEnabled = false
    });

    private static TextureInfo FrameTexture(int frame) {
        var col = frame % TitleLogoAnimData.Columns;
        var row = frame / TitleLogoAnimData.Columns;
        var w = TitleLogoAnimData.SheetWidth;
        var h = TitleLogoAnimData.SheetHeight;
        return new TextureInfo(new AtlasPosition(
            col * TitleLogoAnimData.FrameWidth / (float)w,
            row * TitleLogoAnimData.FrameHeight / (float)h,
            TitleLogoAnimData.FrameWidth / (float)w,
            TitleLogoAnimData.FrameHeight / (float)h), TextureType.TitleGraphic);
    }

    private void OnFrame() {
        var now = (double)Stage.GameTime.TotalMs;
        if (_startMs < 0) {
            _startMs = now;
        }

        var elapsed = now - _startMs;

        // Position in the loop, in stored-frame units: whole part = frame, fraction = blend towards the next one.
        var frames = TitleLogoAnimData.FrameCount;
        var loopMs = frames / TitleLogoAnimData.SourceFps * 1000.0 / PlaybackSpeed;
        var t = (elapsed % (loopMs + LoopRestMs));
        var position = t >= loopMs ? 0.0 : t / 1000.0 * TitleLogoAnimData.SourceFps * PlaybackSpeed;

        var current = (int)position % frames;
        var blend = (float)(position - Math.Floor(position));
        // During the rest, hold on frame 0 with no blend at all.
        if (t >= loopMs) {
            current = 0;
            blend = 0f;
        }

        var next = (current + 1) % frames;
        if (current != _shownCurrent) {
            _current.ChangeTexture(FrameTexture(current));
            _shownCurrent = current;
        }

        if (next != _shownNext) {
            _next.ChangeTexture(FrameTexture(next));
            _shownNext = next;
        }

        _next.Alpha = blend;
    }
}
