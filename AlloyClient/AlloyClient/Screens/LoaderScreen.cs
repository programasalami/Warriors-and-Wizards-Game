using System;
using AlloyClient.Display;
using AlloyClient.Loading;
using AlloyClient.Screens.Components;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens;

// The menu-side loader: an all-black screen with the logo dead centre and a progress bar right under it, driven by a
// LoadPlan of REAL steps (see LoadPlan) - the bar shows how much of that work has genuinely finished. Used at client
// start, for PLAY -> character select, and whenever a menu screen needs data before it can appear (LoaderFlows).
//
// Nothing starts until the fade INTO this screen has completely finished. When every step is done the bar reaches
// full, holds a moment, and the screen fades into whatever the plan built (`next`).
public class LoaderScreen : TitleScreenBase {

    private const uint BackgroundColor = 0x000000;
    private const double HoldFullMs = 400;          // pause on a full bar before leaving

    private readonly ColorRect _background;
    private readonly Container _root;
    private readonly LoaderPanel _panel;

    private readonly LoadPlan _plan;
    private readonly Func<Screen> _next;
    private readonly int _fadeMs;

    private double _fullForMs;
    private bool _leaving;

    // plan: the real work; next: gives the screen to go to (called once, after everything is done - it can return a
    // screen a plan step already built); fadeMs: length of the fade into it.
    public LoaderScreen(LoadPlan plan, Func<Screen> next, int fadeMs = 1000) : base(Components.ScreenType.Loading) {
        _plan = plan;
        _next = next;
        _fadeMs = fadeMs;

        // Opaque black over TitleScreenBase's own dark grey base.
        _background = new ColorRect(new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = BackgroundColor });
        AddChild(_background);

        _root = new Container();
        _panel = new LoaderPanel();
        _root.AddChild(_panel);
        AddChild(_root);

        AddEventListener(Event.EnterFrame, OnFrame);
    }

    protected override void OnResize(ResizeEvent args) {
        base.OnResize(args);
        _background.Resize(args.Width, args.Height);

        // Same fixed-canvas scaling + centring the other screens use (see CharacterListScreen.OnResize).
        var scale = Stage.ScreenScale;
        _root.Scale = scale;
        _root.X = (int)((Stage.StageWidth - Settings.DefaultScreenWidth * scale.X) / 2f);
        _root.Y = (int)((Stage.StageHeight - Settings.DefaultScreenHeight * scale.Y) / 2f);
    }

    private void OnFrame() {
        var dt = Stage.GameTime.ElapsedMs;

        // Nothing loads until the fade into this screen has fully finished (ScreenManager tweens Alpha 0 -> 1).
        if (Alpha < 0.999f) {
            _panel.Idle(dt);
            return;
        }

        if (!_leaving) {
            _plan.Tick();
        }

        _panel.Advance(dt, _plan.Progress);

        if (_leaving || !_plan.IsComplete || _panel.Displayed < 1f) {
            return;
        }

        _fullForMs += dt;
        if (_fullForMs >= HoldFullMs) {
            _leaving = true;
            ScreenManager.FadeToScreen(_next(), Easing.SineInOut, _fadeMs, 0x0);
        }
    }
}
