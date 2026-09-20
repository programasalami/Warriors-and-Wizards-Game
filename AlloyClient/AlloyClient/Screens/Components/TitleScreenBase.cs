using AlloyClient.Display;
using AlloyClient.Screens.Components.CharacterList;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using AlloyClient.Utils;

namespace AlloyClient.Screens.Components;

public enum ScreenType {
    Loading,
    Title,
    Other
}

public abstract class TitleScreenBase : Screen {

    // Same dark grey ScreenDarkenOverlay already used to dim a background - here it's the
    // whole background (opaque) instead of a translucent dimming layer over something else.
    private const uint BackgroundColor = 0x2B2B2B;

    // How long the wallpaper crossfade + any subclass content fade (see
    // OnTitleContentVisibilityChanged) takes, both ways. Bumped up twice now (250 -> 450 -> 700)
    // per direct feedback that the scroll-fades-out/book-fades-in transition still read as too
    // quick a flash. Protected so subclasses (e.g. TitleScreen fading its row list) can match it
    // exactly.
    protected const int ContentFadeDuration = 700;

    private readonly ColorRect _background;

    // Only for ScreenType.Title: a dark cave map (CaveBackdrop). It replaced the two wallpaper images the title screen
    // used to swap between (one with a frame drawn INTO the picture, one without, switched whenever a popup opened) -
    // overlays now just open over the map, and the title screen's own logo and menu block fade (see
    // OnTitleContentVisibilityChanged). The character-select screen keeps the light forest map (ForestBackdrop).
    private readonly CaveBackdrop _backdrop;
    private readonly bool _isTitle;

    protected TitleScreenBase(ScreenType type = ScreenType.Other) {
        // Dark grey base for every screen type - on the title screen it shows through the
        // letterbox bars left by ScreenGraphic's "contain" scaling (see ScreenBaseGraphic).
        _background = new ColorRect(new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = BackgroundColor });
        AddChild(_background);

        if (type == ScreenType.Title) {
            _isTitle = true;
            AddChild(_backdrop = new CaveBackdrop());
        }

        //Todo guild/stars

        AddEventListener(Event.AddedToStage, OnStageEnter);
        AddEventListener(Event.RemovedFromStage, OnStageExit);
    }

    private void OnStageEnter() {
        Stage.AddEventListener(ResizeEvent.Resize, OnResize);
        OnResize(new ResizeEvent(ResizeEvent.Resize, Stage.StageWidth, Stage.StageHeight));

        if (_isTitle) {
            OverlayManager.OverlayOpened += OnOverlayOpened;
            OverlayManager.OverlayClosed += OnOverlayClosed;
        }
    }

    private void OnStageExit() {
        Stage.RemoveEventListener(ResizeEvent.Resize, OnResize);

        if (_isTitle) {
            OverlayManager.OverlayOpened -= OnOverlayOpened;
            OverlayManager.OverlayClosed -= OnOverlayClosed;
        }
    }

    protected override void OnResize(ResizeEvent args) {
        _background?.Resize(args.Width, args.Height);
        _backdrop?.Resize(args.Width, args.Height);
    }

    private void OnOverlayOpened() => OnTitleContentVisibilityChanged(false);

    private void OnOverlayClosed() => OnTitleContentVisibilityChanged(true);

    // Only invoked for ScreenType.Title. Lets TitleScreen fade its own row list/header alongside
    // the splash graphic above, without TitleScreenBase needing to know that content exists.
    protected virtual void OnTitleContentVisibilityChanged(bool visible) {}
}
