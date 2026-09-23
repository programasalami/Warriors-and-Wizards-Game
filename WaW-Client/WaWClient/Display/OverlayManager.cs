using System;
using WaWClient.Ui.Components.Panels;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaWClient.Utils;

namespace WaWClient.Display;

public sealed class OverlayManager : Sprite {

    private static readonly ColorRect Overlay = new (new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = 0x2B2B2B, Alpha = 0.8f });

    private static OverlayManager Instance;

    private Overlay _current;
    private bool _dimActive;

    // Fired only on a true none<->something transition, never when one overlay is swapped
    // directly for another (e.g. login -> register) - screens behind the overlay (like
    // TitleScreen's background/menu fade) should stay hidden throughout a swap, not flash back
    // in between the two.
    public static event Action OverlayOpened;
    public static event Action OverlayClosed;

    public OverlayManager() {
        Instance = this;
        AddEventListener(Event.AddedToStage, OnStageEnter);
        AddEventListener(Event.RemovedFromStage, OnStageExit);
    }

    public static void Set(Overlay sprite) => Instance.PrivateSet(sprite);

    public static void Clear() => Instance.PrivateClear();

    private void PrivateSet(Overlay sprite) {
        if (_current is null) {
            OverlayOpened?.Invoke();

            if (sprite.DimBackground) {
                AddChild(Overlay);
                Overlay.AddAlphaTween(0f, 0.8f, sprite.FadeInDurationMs);
                _dimActive = true;
            }

            AddChild(_current = sprite);
            PositionCurrent();
            _current.AddAlphaTween(0f, 1f, sprite.FadeInDurationMs);
        } else {
            _current.AddAlphaTween(1f, 0f, 250, onFinish: () => {
                RemoveChild(_current);
                AddChild(_current = sprite);
                PositionCurrent();
                _current.AddAlphaTween(0f, 1f, 250);
            });
        }
    }

    private void PrivateClear() {
        OverlayClosed?.Invoke();

        if (_dimActive) {
            Overlay.AddAlphaTween(0.8f, 0f, 450, onFinish: () => { RemoveChild(Overlay); });
            _dimActive = false;
        }
        _current.AddAlphaTween(1f, 0f, 300, onFinish: () => { RemoveChild(_current); _current = null; });
    }

    private void OnStageEnter() {
        Stage.AddEventListener(ResizeEvent.Resize, OnResize);
        OnResize(new ResizeEvent(ResizeEvent.Resize, Stage.StageWidth, Stage.StageHeight));
    }

    private void OnStageExit() {
        Stage.RemoveEventListener(ResizeEvent.Resize, OnResize);
    }

    private void OnResize(ResizeEvent args) {
        PositionCurrent();
        Overlay.Resize(args.Width, args.Height);
    }

    // Also called right when an overlay is set, not just on the next real resize event - the
    // overlay's own constructor no longer hardcodes a centered position (that used the "default"
    // design resolution, so it only looked centered if the actual window happened to match it).
    private void PositionCurrent() {
        if (_current is null) {
            return;
        }

        var scale = Stage.ScreenScale * _current.ScaleMultiplier;
        _current.X = (int)(Stage.StageWidth * _current.HorizontalPositionFraction);
        _current.Y = Stage.StageHeight / 2 + (int)(_current.VerticalNudge * scale.Y);

        // A fixed-size window (Overlay.FixedSize) is placed by its top-left corner so its middle is exactly the centre point above, whatever its children measure.
        if (_current.FixedSize is { } size) {
            _current.X -= (int)(size.Width / 2f * scale.X);
            _current.Y -= (int)(size.Height / 2f * scale.Y);
        }

        _current.Scale = scale;
    }
    
}