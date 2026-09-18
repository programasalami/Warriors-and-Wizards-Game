using AlloyClient.Display;
using AlloyClient.Ui.Components.Graphics;
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

    // Only populated for ScreenType.Title - TitleScreenGraphic (the scroll/text/buttons art) sits
    // on top of TitleScreenBackground (the same wallpaper minus the scroll, see
    // Content/TitleScreen/TitleScreenBackground.png) and fades out/in as overlays (login,
    // register, and future settings/servers/account frames) open/close, revealing the plain
    // wallpaper underneath instead of the scroll fading to nothing.
    private readonly ScreenGraphic _splashGraphic;

    protected TitleScreenBase(ScreenType type = ScreenType.Other) {
        // Dark grey base for every screen type - on the title screen it shows through the
        // letterbox bars left by ScreenGraphic's "contain" scaling (see ScreenBaseGraphic).
        _background = new ColorRect(new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = BackgroundColor });
        AddChild(_background);

        if (type == ScreenType.Title) {
            AddChild(new ScreenGraphic(false));
            AddChild(_splashGraphic = new ScreenGraphic(true));
        }

        //Todo guild/stars

        AddEventListener(Event.AddedToStage, OnStageEnter);
        AddEventListener(Event.RemovedFromStage, OnStageExit);
    }

    private void OnStageEnter() {
        Stage.AddEventListener(ResizeEvent.Resize, OnResize);
        OnResize(new ResizeEvent(ResizeEvent.Resize, Stage.StageWidth, Stage.StageHeight));

        if (_splashGraphic != null) {
            OverlayManager.OverlayOpened += OnOverlayOpened;
            OverlayManager.OverlayClosed += OnOverlayClosed;
        }
    }

    private void OnStageExit() {
        Stage.RemoveEventListener(ResizeEvent.Resize, OnResize);

        if (_splashGraphic != null) {
            OverlayManager.OverlayOpened -= OnOverlayOpened;
            OverlayManager.OverlayClosed -= OnOverlayClosed;
        }
    }

    protected override void OnResize(ResizeEvent args) {
        _background?.Resize(args.Width, args.Height);
    }

    private void OnOverlayOpened() {
        _splashGraphic.AddAlphaTween(1f, 0f, ContentFadeDuration);
        OnTitleContentVisibilityChanged(false);
    }

    private void OnOverlayClosed() {
        _splashGraphic.AddAlphaTween(0f, 1f, ContentFadeDuration);
        OnTitleContentVisibilityChanged(true);
    }

    // Only invoked for ScreenType.Title. Lets TitleScreen fade its own row list/header alongside
    // the splash graphic above, without TitleScreenBase needing to know that content exists.
    protected virtual void OnTitleContentVisibilityChanged(bool visible) {}
}
