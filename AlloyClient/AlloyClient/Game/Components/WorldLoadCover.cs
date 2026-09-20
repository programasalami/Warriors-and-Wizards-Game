using System;
using AlloyClient.Loading;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components;

// The game screen's loading cover: opaque black + the logo loader, shown while getting into a world - the first entry
// from character select AND every world switch (portal -> Reconnect). Its bar follows WorldLoad's REAL milestones
// (connected, map info, character accepted, player spawned, first frame drawn); when they're all reached the bar
// fills, holds a moment, and the cover fades away to reveal the world.
public sealed class WorldLoadCover : Container {

    private const double SwitchFadeInMs = 300;      // fade in over the old world when switching (first entry is already black)
    private const double HoldFullMs = 350;
    private const double FadeOutMs = 650;

    private readonly ColorRect _background;
    private readonly Container _root;
    private readonly LoaderPanel _panel;

    private double _fadeInMs;
    private double _fullForMs;
    private bool _fadingIn;
    private bool _fadingOut;

    public WorldLoadCover() {
        _background = new ColorRect(new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = 0x000000 });
        AddChild(_background);

        _root = new Container();
        _panel = new LoaderPanel();
        _root.AddChild(_panel);
        AddChild(_root);

        Visible = false;
        AddEventListener(Event.EnterFrame, OnFrame);
    }

    // First entry: opaque immediately (the screen fade brings us in from black). Switch: fades in over the old world.
    public void Begin(bool switching) {
        _fadingOut = false;
        _fullForMs = 0;
        _fadingIn = switching;
        _fadeInMs = 0;
        Alpha = switching ? 0f : 1f;
        Visible = true;
    }

    public void Resize(int width, int height) {
        _background.Resize(width, height);

        var scale = Stage.ScreenScale;
        _root.Scale = scale;
        _root.X = (int) ((width - Settings.DefaultScreenWidth * scale.X) / 2f);
        _root.Y = (int) ((height - Settings.DefaultScreenHeight * scale.Y) / 2f);
    }

    private void OnFrame() {
        if (!Visible) {
            return;
        }

        var dt = Stage.GameTime.ElapsedMs;

        if (_fadingIn) {
            _fadeInMs += dt;
            Alpha = (float) Math.Clamp(_fadeInMs / SwitchFadeInMs, 0.0, 1.0);
            if (_fadeInMs >= SwitchFadeInMs) {
                _fadingIn = false;
            }
        }

        _panel.Advance(dt, WorldLoad.Progress);

        if (_fadingOut) {
            Alpha = Math.Max(0f, Alpha - (float) (dt / FadeOutMs));
            if (Alpha <= 0f) {
                Visible = false;
                WorldLoad.Finish();
            }
            return;
        }

        if (!_fadingIn && WorldLoad.IsComplete && _panel.Displayed >= 1f) {
            _fullForMs += dt;
            if (_fullForMs >= HoldFullMs) {
                _fadingOut = true;
            }
        }
    }
}
