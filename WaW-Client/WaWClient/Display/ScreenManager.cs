using System;
using WaWClient.Game;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Extra;
using WaW.Engine;
using WaWClient.Ui.Components;
using OpenTK.Platform;

namespace WaWClient.Display;

public sealed class ScreenManager : Sprite {
    private static ScreenManager _instance;
    public static readonly FadeScreen FadeScreen = new(0);

    private static Screen _prevScreen;
    private static Screen _currScreen = FadeScreen;

    public ScreenManager() {
        _instance = this;
        FadeScreen.Visible = false;
        _instance.AddChild(FadeScreen);
        
        AddEventListener(Event.AddedToStage, AddedToStage);
    }

    private void AddedToStage() {
        Stage.AddEventListener(KeyboardEvent.KeyUp, OnKeyUp);
    }

    /// <summary>
    /// Calls the current screens virtual update call, used for drawing the actual game
    /// </summary>
    public static void Update(GameTime gameTime) => _currScreen?.Update(gameTime);
    /// <summary>
    /// Calls the current screens virtual draw call, used for drawing the actual game
    /// </summary>
    public static void Draw(GameTime gameTime) => _currScreen?.Draw(gameTime);

    public static void SetScreen(Screen screen) {
        RemovePrevious();
        _currScreen = screen;
        _instance.AddChild(_currScreen);
    }

    public static void SetPrevious() {
        SetScreen(_prevScreen);
    }

    public static void FadeToScreen(Screen screen, Easing ease, int durationMs, uint color, Action onFinish = null) {
        Main.OnScreenChange.Dispatch(screen is GameScreen ? ScreenType.Game : ScreenType.Menu);

        FadeScreen.Visible = true;
        FadeScreen.SetFadeColor(color);

        // The game screen brings its own opaque black loading cover (WorldLoadCover), and the world under it is drawn with GL code that
        // ignores sprite alpha. Fading the screen in from alpha 0 therefore let the world and HUD show through a mostly transparent
        // cover for the whole second half of the fade (the server has usually answered by then) before the loader appeared. So it goes
        // on screen fully opaque instead: the old screen has already faded to black and the cover keeps it black until the world is ready.
        var opaqueEntry = screen is GameScreen;
        screen.Alpha = opaqueEntry ? 1f : 0f;
        GTween.Add(Tween.New(_currScreen, ease, durationMs / 2, 0f, EaseType.Alpha, 0, () => {
            onFinish?.Invoke();
            SetScreen(screen);
            if (opaqueEntry) {
                FadeScreen.Visible = false;
            }
        }));
        if (!opaqueEntry) {
            GTween.Add(Tween.New(screen, ease, durationMs / 2, 1f, EaseType.Alpha, durationMs / 2, () => { FadeScreen.Visible = false; }));
        }
    }

    public static void FadeTo(Screen screen, Action callback = null) => FadeToScreen(screen, Easing.SineInOut, 1000, 0x0, callback);

    public static void FadeToPrevious(Easing ease, int durationMs, uint color) {
        FadeToScreen(_prevScreen, ease, durationMs, color);
    }

    private static void RemovePrevious() {
        _prevScreen = _currScreen;
        _instance.RemoveChild(_currScreen);
    }

    private void OnKeyUp(KeyboardEvent args) {
        if ((args.Key == Key.Return && args.Alt) || Settings.FullscreenKey.Equals(args.Code)) {
            Settings.FullscreenState.Set(!Settings.FullscreenState);
            Main.OnFullscreenToggle.Dispatch();
        }
    }
}

public abstract class Screen : UiElement {
    public virtual void Update(GameTime gameTime) { }
    public virtual void Draw(GameTime gameTime) { }
}

public class FadeScreen : Screen {
    
    private readonly ColorRect _rect;

    public FadeScreen(uint color) {
        var config = new ColorRectConfig { X = 0, Y = 0, Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = color};
        _rect = new ColorRect(config);
        AddChild(_rect);
    }

    public void SetFadeColor(uint color) => _rect.SetColor(color);
}