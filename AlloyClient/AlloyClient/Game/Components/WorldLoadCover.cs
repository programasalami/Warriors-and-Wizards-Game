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

    private const double HoldFullMs = 350;
    private const double FadeOutMs = 650;

    private readonly ColorRect _background;
    private readonly Container _root;
    private readonly LoaderPanel _panel;

    private double _fullForMs;
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

    // Opaque immediately, for the first entry AND for a world switch. A switch used to fade in over the old world, but the old world is wiped the moment
    // the Reconnect arrives and the new one starts appearing within a few frames, so the fade showed an empty / half-built world flashing through the
    // see-through cover (measured: one bright frame in the middle of the black). A hard cut to black has nothing to flash.
    public void Begin(bool switching) {
        _fadingOut = false;
        _fullForMs = 0;
        Alpha = 1f;
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

        _panel.Advance(dt, WorldLoad.Progress);

        if (_fadingOut) {
            Alpha = Math.Max(0f, Alpha - (float) (dt / FadeOutMs));
            if (Alpha <= 0f) {
                Visible = false;
                WorldLoad.Finish();
            }
            return;
        }

        if (WorldLoad.IsComplete && _panel.Displayed >= 1f) {
            _fullForMs += dt;
            if (_fullForMs >= HoldFullMs) {
                _fadingOut = true;
            }
        }
    }
}
