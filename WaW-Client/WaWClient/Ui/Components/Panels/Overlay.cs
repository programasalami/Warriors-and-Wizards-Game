using WaWClient.Display;
using WaW.UiLib.Core;

namespace WaWClient.Ui.Components.Panels;

public class Overlay : Sprite {

    public virtual bool InputBlocker => true;

    // OverlayManager's dim ColorRect behind every overlay - most overlays (login/register/class
    // select) want it for contrast against the title screen art behind them, but an overlay that
    // wants the background art to stay fully visible (e.g. BookOverlay) can opt out.
    public virtual bool DimBackground => true;

    // Where OverlayManager centers this overlay on the stage, as a fraction of stage width/height
    // (0.5/0.5 = dead center) plus a fixed post-scale pixel nudge - lets an overlay spawn somewhere
    // other than dead-center (e.g. BookOverlay spawning exactly where TitleScreen's scroll sits).
    // The design-pixel size of the overlay's panel, for a window whose panel has a FIXED size (the in-game windows). OverlayManager then centres the panel on
    // exactly that size. Do NOT centre such a window with SetAnchor(Middle): a centre anchor follows the size of the sprite's CHILDREN, so whenever a page's
    // contents grow, shrink or are rebuilt (a tab click, a list that loads, a button that refreshes the menu) the whole window jumped and flickered.
    public virtual (int Width, int Height)? FixedSize => null;

    public virtual float HorizontalPositionFraction => 0.5f;
    public virtual int VerticalNudge => 0;

    // Extra scale on top of Stage.ScreenScale - lets an individual overlay render larger/smaller
    // than the game's other overlays without needing higher-resolution source art.
    public virtual float ScaleMultiplier => 1f;

    // How long OverlayManager's alpha-fade-in takes for this overlay the first time it opens
    // (none -> something transition) - lets an individual overlay fade in slower/faster than the
    // rest without changing the shared default.
    public virtual int FadeInDurationMs => 450;

    public virtual void CloseOverlay() => OverlayManager.Clear();
}