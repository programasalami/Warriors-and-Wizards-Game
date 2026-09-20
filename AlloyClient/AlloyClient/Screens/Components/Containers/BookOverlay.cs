using System;
using AlloyClient.AppEngine;
using AlloyClient.Display;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Panels;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Screens.Components.Containers;

/// <summary>
/// Replaces LoginContainer/RegisterContainer as the entry point from the title screen's PLAY
/// button when logged out - an animated book (the "2 Brown Book" set from the Pocket Inventory
/// Series #5 pack - was "1 Green Book" briefly, swapped per request; every other book color in
/// that pack shares the same Style 1/Cover 1 frame layout, so swapping again later is just a
/// re-export from a different source folder, no layout constants need to change) that fades in,
/// plays its open animation, shows a hub page (big Register/Sign In cards, left/right page each), and
/// page-flips to Register or Sign In content on the same book frame. Register/Sign In content
/// lives entirely on the right page - the left page is intentionally left blank for future
/// registration-related features (the flip only reveals the right page's content changing, same
/// as a real book). AccountOverlay's own inline login/register links still go straight to
/// LoginContainer/RegisterContainer - this class only changes the PLAY-button path.
/// </summary>
public class BookOverlay : Overlay {

    public static readonly EventType<Event> LoginEvent = "loginSuccess";

    // The source frames are rasterized at 650x523 (see the resize step noted in
    // Content/Ui/Book) - rendered 1:1 at that same size so the texture is never stretched.
    // Was 750x603 briefly, but that overflowed the 4096x4096 Ui atlas once combined with
    // everything else already in it - 5 of PageFlipLeft's 9 frames silently failed to pack
    // (AtlasBuilder catches per-image pack failures and just logs+skips them), so the backward
    // flip was rendering whatever garbage/overlapping texture happened to be at those missing
    // lookups - that's what looked like the book randomly closing and reopening on Back.
    private const int FrameWidth = 650;
    private const int FrameHeight = 523;

    // Milliseconds per Open/Close frame - cut from an original 90 per feedback that the book took
    // too long to actually open once the cover-hold finished; the fade-in and cover-hold below
    // both got longer this same pass, so this is where that time gets clawed back. Shaved a hair
    // further four times since (50 -> 45 -> 40 -> 35 -> 30) per repeated feedback that it still
    // opened just a touch slow.
    private const int OpenCloseFrameMs = 30;

    // Milliseconds per page-flip frame - unchanged frame count (9) from before.
    private const int PageFlipFrameMs = 40;

    // Fed into OverlayManager via the FadeInDurationMs override below, so OverlayManager's own
    // alpha-fade-in for this overlay takes exactly this long - the cover-hold delay further down
    // is timed to start right as that fade finishes, so the book visibly "fades in, then sits
    // closed, then opens" instead of animating open while still translucent. Bumped up again
    // (450 -> 900 -> 1200), past every other overlay's default (Overlay.FadeInDurationMs, 450) -
    // per repeated feedback that the book still read as "slapping in" instantly at 900.
    private const int FadeInMs = 1600;

    // How long the closed book sits on-screen, fully faded in, before the Open animation starts -
    // gives the cover art (see _coverArt/BookCoverTexture below) an actual moment to be shown off
    // rather than flashing straight into the open animation. Cut back down from 1500 per feedback
    // that it lingered there too long once the entrance spin/grow (see EntranceSpinRotations
    // below) finishes.
    private const int CoverHoldMs = 900;

    // Total full rotations the book spins through while growing from a single point up to full
    // size during the fade-in (see OnEntranceFrame) - purely a decorative flourish on top of the
    // existing alpha fade, no functional meaning to the exact count.
    private const float EntranceSpinRotations = 2f;

    // Content/Ui/Book/BookCover.png - the "WARRIORS & WIZARDS" logo, resized down from its
    // 1536x1024 source to 198x132 (matching its own 1.5 aspect ratio) so it's stored at
    // roughly its actual on-screen size instead of being resampled from something huge every
    // frame. Centered on the closed cover's face - measured directly from Book/Open/1.png's
    // pixel data (the cover face, inside its decorative border and above the bottom binding bar,
    // spans roughly x=212..433, y=129..398 at this frame's 650x523), not eyeballed.
    private const string BookCoverTexture = "Book/BookCover";
    private const int BookCoverWidth = 198;
    private const int BookCoverHeight = 132;
    private const int BookCoverCenterX = (212 + 433) / 2;
    private const int BookCoverCenterY = (129 + 398) / 2;

    // Measured directly from Content/Ui/Book/Static/1.png's pixel data at its native 650x523
    // resolution - not eyeballed. This asset's page area sits well inside the outer book
    // silhouette (decorative corner border, curved page top, and a thick bottom binding bar
    // that isn't part of the writable area). Local coordinates, since _bookSprite sits at (0,0)
    // within this overlay.
    private const int PageAreaLeft = 93;
    private const int PageAreaRight = 557;
    private const int PageAreaTop = 152;
    private const int PageAreaBottom = 410;
    private const int SpineX = 326;

    // Per-page gutter margin so content never sits right against the spine - everything on the
    // right page (Register/Sign In forms) is confined to RightPageLeft..PageAreaRight, and
    // everything on the left page (the hub's Register card) to PageAreaLeft..LeftPageRight.
    // Nothing spans across the spine anymore - that was reading as sloppy/unfinished.
    private const int PageGutter = 14;
    private const int LeftPageRight = SpineX - PageGutter;
    private const int RightPageLeft = SpineX + PageGutter;
    private const int LeftPageCenterX = (PageAreaLeft + LeftPageRight) / 2;
    private const int RightPageCenterX = (RightPageLeft + PageAreaRight) / 2;
    private const int RightPageContentWidth = PageAreaRight - RightPageLeft - 24;

    // TextButton/TextInput both default to white text (meant for this app's usual dark
    // backdrops) - the book's pages are cream/white, so everything on them needs dark colors
    // instead, same fix as the title screen header's readability pass.
    private const uint PageTextColor = 0x000000;

    // Matches the home screen row buttons' hover color (TextButtonConfig's own default
    // HoverColor, 0xFFDC85 - TitleMenuButton/MenuBarButton never override it) so the hub's
    // Register/Sign In text highlights the same way PLAY/SERVERS/etc. do. The hub cards used to
    // have a visible PageButtonFrame nine-slice background that tinted on hover - removed per
    // request in favor of just the text itself highlighting, same as the home screen.
    private const uint HomeButtonHoverColor = 0xFFDC85;
    private const int HubCardWidth = LeftPageRight - PageAreaLeft - 30;

    // Cut to roughly half its original 130 - the frame's width was already right, but at 130
    // tall it read as an oversized plate around the label. Still centered on the same cardY.
    private const int HubCardHeight = 66;

    // The close icon (Content/Ui/Icons/BookCloseIcon.png, a hand-drawn X) replacing the old bare
    // "X" TextButton - that button's own box (centered on the page-area corner, ~50px across)
    // stuck out past the book's actual silhouette on two sides at once, which read as "floating"
    // rather than sitting in the book's corner. Sized/positioned here from the book's own opaque
    // pixel bounds (Book/Static/1.png, measured directly: silhouette spans roughly x=86..564,
    // y=132..432 at this frame's 650x523) so the icon sits fully inside the top-right corner
    // with a small margin, instead of overhanging the transparent edge.
    private const int CloseIconWidth = 34;
    private const int CloseIconHeight = 40;
    private const int CloseIconCenterX = 564 - CloseIconWidth / 2 - 2;
    private const int CloseIconCenterY = 132 + CloseIconHeight / 2 + 2;
    private const float CloseIconHoverScale = 1.15f;

    // Hub-page-only Back button (Content/Ui/Icons/BookBackIcon.png, same icon the Register/Sign
    // In forms use to return to the hub) in the book's top-left corner, mirroring the close (X)
    // button's top-right placement - lets someone back out of the book without picking Register
    // or Sign In first, same intent as the X close button but a second, more expected place to
    // look for "go back". Lives on _hubPage itself (not the overlay root, like the close button
    // does) so it only exists while the hub page is actually showing and disappears automatically
    // the moment a page-flip removes it, same as everything else on that page.
    private const int HubBackIconCenterX = 86 + CloseIconWidth / 2 + 2;
    private const int HubBackIconCenterY = CloseIconCenterY;

    // The close icon used to be added (and visible) the instant the overlay was constructed -
    // it sat there floating in empty space through the whole fade-in, then through the open
    // animation, well before the book art it's supposed to be sitting on top of had actually
    // finished appearing. Kept hidden until this long after the hub page actually appears (i.e.
    // measured from after fade-in + the cover-hold + the open animation itself, not from fade
    // start), so it only shows up once the book is visually "there" to hold it.
    private const int OpenAnimationDurationMs = 4 * OpenCloseFrameMs; // 5-frame Open animation = 4 frame transitions
    private const int CloseButtonAppearDelayMs = 800;

    private const float PageTitleFontSize = 28f;
    private const float LabelFontSize = 22f;
    private const float InputFontSize = 26f;
    private const float HubCardFontSize = 32f;

    // Back/Proceed on the Register/Sign In forms - replaced the old separate text buttons (a
    // "Proceed" row above a "Back" row) with a single row of two icons side by side, Back on the
    // left and Proceed on the right, per request. Same bbox on both source PNGs (~150x156 inside
    // a 256x256 canvas), so one shared size works for both.
    private const int FormIconButtonWidth = 40;
    private const int FormIconButtonHeight = 42;
    private const int FormIconButtonGap = 30;
    private const float FormIconButtonHoverScale = 1.15f;

    // Multiply-tint applied over the whole book texture (there's no way to recolor just the page
    // area of a single rasterized asset without a second texture/mask) so the pages read close to
    // the same warm parchment tone as the title screen's scroll. Derived by sampling both source
    // images directly, not eyeballed: the book's page color at rest is #D8D3AF (216,211,175), the
    // scroll's parchment is #C68C48 (198,140,72) - this is componentwise target/source
    // (0.9167, 0.6635, 0.4114) packed as an RGB tint. Also shifts the cover/binding leather the
    // same way since it's one texture, which happens to land in the same warm brown family.
    private const uint PageTintColor = 0xE9A969;

    // Overlay spawns bigger than 1:1 screen scale, same idea as TitleScreen's own
    // BlockScaleMultiplier for the scroll it's replacing.
    private const float BookScaleMultiplier = 1.3f;

    private readonly ObjectRect _bookSprite;
    private readonly ObjectRect _coverArt;
    private readonly Sprite _closeButton;
    private readonly Container _pageContent;

    private readonly Container _hubPage;
    private readonly Container _registerPage;
    private readonly Container _signInPage;

    private Timer _frameTimer;
    private string _animationName;
    private int _animationFrame;
    private int _animationFrameCount;
    private Action _onAnimationComplete;

    private bool _closing;

    // Captured lazily on the first entrance frame rather than at construction time, since
    // OverlayManager doesn't set this overlay's real target Scale (via PositionCurrent) until
    // after the constructor already returns - see OnEntranceFrame.
    private Vector2 _entranceTargetScale;
    private bool _entranceScaleCaptured;
    private double _entranceElapsedMs;
    private double _exitElapsedMs;
    private bool _exitHoldDone;

    // No dim backdrop and a bigger overlay scale (see Overlay's virtuals). It opens dead centre of the screen like every
    // other popup - it used to sit flush left only because the old title wallpaper had the logo baked into its right
    // side, and the position/scale of the drawn-in scroll had to be matched.
    public override bool DimBackground => false;
    public override float ScaleMultiplier => BookScaleMultiplier;
    public override int FadeInDurationMs => FadeInMs;

    public BookOverlay() {
        SetAnchor(UiAnchor.Middle);
        AddEventListener(Event.EnterFrame, OnEntranceFrame);

        // Anchored on its own center (matching _coverArt below), not the default LeftTop, purely
        // so the entrance spin (OnEntranceFrame) rotates this around the same point as the logo -
        // a sprite's rotation always pivots around its OWN local anchor (see Ui.vert: pos =
        // Position + VertexAnchor, rotated, then -VertexAnchor), which for LeftTop is the raw
        // (0,0) corner. With that default, the cascaded rotation from this overlay spun the book
        // in a huge arc around its top-left corner while the Middle-anchored logo spun tightly in
        // place around its own center right on top of it - two completely different motions even
        // though both were receiving the identical rotation value. X/Y are set to the frame's own
        // center (FrameWidth/2, FrameHeight/2) so the sprite still renders at the exact same spot
        // at rest (Rotation=0) as it did at local (0,0) with the old LeftTop anchor - only the
        // pivot used while actively rotating changes.
        _bookSprite = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Book/Open/1", 0, false),
            X = FrameWidth / 2,
            Y = FrameHeight / 2,
            Width = FrameWidth,
            Height = FrameHeight,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        _bookSprite.SetColor(PageTintColor);
        AddChild(_bookSprite);

        // Sits on top of _bookSprite only while the closed cover is what's showing - hidden the
        // moment the Open animation is kicked off, below, so it doesn't linger through the pages
        // flipping open. No tint applied - the logo keeps its own colors, unlike the book itself.
        _coverArt = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(BookCoverTexture, 0, false),
            X = BookCoverCenterX,
            Y = BookCoverCenterY,
            Width = BookCoverWidth,
            Height = BookCoverHeight,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_coverArt);

        _closeButton = BuildCloseButton();
        _closeButton.Visible = false;
        AddChild(_closeButton);

        _hubPage = BuildHubPage();
        _registerPage = BuildRegisterPage();
        _signInPage = BuildSignInPage();

        _pageContent = new Container();
        AddChild(_pageContent);

        // CoverHoldMs folded in here (not a separate timer) - the closed cover (already the
        // texture _bookSprite was constructed with, above) just sits on screen this much longer
        // after the fade-in finishes before the Open animation starts.
        var openDelay = new Timer(FadeInMs + CoverHoldMs, 1);
        openDelay.AddEventListener(TimerEvent.Timer, () => {
            _coverArt.Visible = false;
            PlayAnimation("Open", 5, OpenCloseFrameMs, ShowHubPage);
        });
        openDelay.Start();

        // A sibling top-level timer (started here, alongside openDelay), not one created from
        // inside openDelay's own callback - a Timer constructed and Start()-ed from within
        // another Timer's TimerEvent.Timer callback never actually renders its target visible
        // (reproduced directly: identical setup minus the nesting works fine). Total delay folds
        // FadeInMs + CoverHoldMs + OpenAnimationDurationMs in directly so the close icon still
        // only appears once the hub page is visually "there" to hold it, same intent as before.
        var closeButtonDelay = new Timer(FadeInMs + CoverHoldMs + OpenAnimationDurationMs + CloseButtonAppearDelayMs, 1);
        closeButtonDelay.AddEventListener(TimerEvent.Timer, () => _closeButton.Visible = true);
        closeButtonDelay.Start();
    }

    // Tucked into the book's actual top-right corner. Built on top of the shared BuildIconButton
    // helper below (also used for the Back/Proceed icons on the Register/Sign In forms).
    private Sprite BuildCloseButton() =>
        BuildIconButton("Icons/BookCloseIcon", CloseIconCenterX, CloseIconCenterY, CloseIconWidth, CloseIconHeight, CloseIconHoverScale, OnCloseClicked);

    // A Container wrapping a plain icon Sprite, self-contained click handling and a small
    // scale-up on hover (rather than a color tint - this codebase's icon art here is solid
    // near-black, so a multiply tint wouldn't read against it).
    private Sprite BuildIconButton(string iconLookup, int centerX, int centerY, int width, int height, float hoverScale, Action onClicked) {
        var button = new Container();
        button.X = centerX;
        button.Y = centerY;
        button.SetAnchor(UiAnchor.Middle);
        button.MouseEnabled = true;

        var icon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(iconLookup, 0, false),
            Width = width,
            Height = height,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        button.AddChild(icon);

        var leftDown = false;
        button.AddEventListener(MouseEvent.MouseOver, () => button.Scale = new Vector2(hoverScale));
        button.AddEventListener(MouseEvent.MouseOut, () => button.Scale = Vector2.One);
        button.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                onClicked();
            }
            leftDown = false;
        });

        return button;
    }

    // Just the two cards - no header text. Register lives on the left page, Sign In on the
    // right, matching where each flips off to (see FlipForward calls below).
    private Container BuildHubPage() {
        var container = new Container();

        var cardY = (PageAreaTop + PageAreaBottom) / 2;
        container.AddChild(BuildHubCard(LeftPageCenterX, cardY, "Register", () => FlipForward(_registerPage)));
        container.AddChild(BuildHubCard(RightPageCenterX, cardY, "Sign In", () => FlipForward(_signInPage)));
        container.AddChild(BuildIconButton("Icons/BookBackIcon", HubBackIconCenterX, HubBackIconCenterY, CloseIconWidth, CloseIconHeight, CloseIconHoverScale, OnCloseClicked));

        return container;
    }

    // No visible background anymore (the PageButtonFrame nine-slice was removed per request) -
    // just the label, highlighting the same way the home screen row buttons do on hover. The
    // card is still a big invisible ColorRect (Alpha=0) sized HubCardWidth x HubCardHeight
    // behind the label so the whole "giant button" area stays clickable, not just the glyphs.
    //
    // IMPORTANT: card itself is Middle-anchored and its children are LeftTop-anchored filling
    // its local (0,0)..(width,height) box - NOT the other way around. Sprite's mouse hit-test
    // (SquareHitbox in Sprite.Bounds.cs) always treats a sprite's own _trueX/_trueY as the
    // hitbox's top-left corner and extends by Width/Height - it does NOT know that this sprite's
    // children might be independently centered. Anchoring the CHILDREN to Middle instead (as a
    // first pass here did) left card's own _trueX at the unshifted centerX while the children
    // rendered centered around it - so the hitbox and the visible card were offset from each
    // other by half the card's size, which read as "hovering beside it highlights, hovering on
    // it doesn't."
    private Sprite BuildHubCard(int centerX, int centerY, string text, Action onClicked) {
        var card = new Container();
        card.X = centerX;
        card.Y = centerY;
        card.SetAnchor(UiAnchor.Middle);
        card.MouseEnabled = true;

        var hitArea = new ColorRect(new ColorRectConfig {
            Width = HubCardWidth,
            Height = HubCardHeight,
            Alpha = 0f
        });
        card.AddChild(hitArea);

        var label = new SimpleText(new TextConfig { Text = text, FontSize = HubCardFontSize, FontType = FontType.Normal, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, OutlineColor = 0x000000, OutlineThickness = 3, X = HubCardWidth / 2, Y = HubCardHeight / 2, Anchor = UiAnchor.Middle });
        card.AddChild(label);

        var leftDown = false;
        card.AddEventListener(MouseEvent.MouseOver, () => label.SetColor(HomeButtonHoverColor));
        card.AddEventListener(MouseEvent.MouseOut, () => label.SetColor(PageTextColor));
        card.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        card.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                onClicked();
            }
            leftDown = false;
        });

        return card;
    }

    // Everything lives on the right page, one item per line: title, "Username:", the input,
    // a gap, "Password:", the input, then the submit/back buttons. The left page is left
    // completely blank - reserved for other registration-related features later.
    private Container BuildRegisterPage() {
        var container = new Container();
        BuildRightPageForm(container, "Register", OnRegister);
        return container;
    }

    private Container BuildSignInPage() {
        var container = new Container();
        BuildRightPageForm(container, "Sign In", OnSignIn);
        return container;
    }

    private void BuildRightPageForm(Container container, string titleText, Action<TextInput, TextInput> onSubmit) {
        var title = new SimpleText(new TextConfig { Text = titleText, FontSize = PageTitleFontSize, FontType = FontType.Bold, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, OutlineColor = 0x000000, X = RightPageCenterX, Y = PageAreaTop + 24, Anchor = UiAnchor.Middle });
        container.AddChild(title);

        var usernameLabel = new SimpleText(new TextConfig { Text = "Username:", FontSize = LabelFontSize, FontType = FontType.Bold, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, OutlineColor = 0x000000, X = RightPageCenterX, Y = PageAreaTop + 61, Anchor = UiAnchor.Middle });
        container.AddChild(usernameLabel);

        // Label-to-input gap bumped from 26 to 32 (below) - at 26 the label's descenders were
        // touching the box's top edge.
        var usernameInput = new TextInput(new InputConfig { X = RightPageCenterX, Y = PageAreaTop + 93, FontSize = InputFontSize, FontType = FontType.Bold, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, BoxColor = 0x000000, Width = RightPageContentWidth, DefaultText = "", Anchor = UiAnchor.Middle });
        container.AddChild(usernameInput);

        var passwordLabel = new SimpleText(new TextConfig { Text = "Password:", FontSize = LabelFontSize, FontType = FontType.Bold, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, OutlineColor = 0x000000, X = RightPageCenterX, Y = PageAreaTop + 136, Anchor = UiAnchor.Middle });
        container.AddChild(passwordLabel);

        var passwordInput = new TextInput(new InputConfig { X = RightPageCenterX, Y = PageAreaTop + 168, FontSize = InputFontSize, FontType = FontType.Bold, FontGroup = FontGroup.MyriadPro, Color = PageTextColor, BoxColor = 0x000000, Width = RightPageContentWidth, DefaultText = "", Password = true, Anchor = UiAnchor.Middle });
        container.AddChild(passwordInput);

        // One row, Back on the left and Proceed on the right of the form's own center line -
        // replaced the old two separate text-button rows.
        var iconRowY = PageAreaTop + 226;
        var backX = RightPageCenterX - FormIconButtonGap / 2 - FormIconButtonWidth / 2;
        var proceedX = RightPageCenterX + FormIconButtonGap / 2 + FormIconButtonWidth / 2;

        container.AddChild(BuildIconButton("Icons/BookBackIcon", backX, iconRowY, FormIconButtonWidth, FormIconButtonHeight, FormIconButtonHoverScale, () => FlipBackward(_hubPage)));
        container.AddChild(BuildIconButton("Icons/BookProceedIcon", proceedX, iconRowY, FormIconButtonWidth, FormIconButtonHeight, FormIconButtonHoverScale, () => onSubmit(usernameInput, passwordInput)));
    }

    // Grows the whole book from a single point up to full size while spinning down to upright,
    // timed to land exactly when OverlayManager's own alpha fade-in finishes (FadeInMs). Scale and
    // Rotation both cascade to every child (_bookSprite, _coverArt, etc.) the same way Alpha does
    // (see Sprite's InternalUpdate), so animating them here on the overlay root spins/grows the
    // whole book as one piece rather than needing this on every child individually.
    private void OnEntranceFrame() {
        if (!_entranceScaleCaptured) {
            _entranceTargetScale = Scale;
            _entranceScaleCaptured = true;
        }

        _entranceElapsedMs += Stage.GameTime.ElapsedMs;
        var t = Math.Clamp(_entranceElapsedMs / FadeInMs, 0.0, 1.0);
        var eased = 1.0 - Math.Pow(1.0 - t, 3.0);

        Scale = _entranceTargetScale * (float)eased;
        Rotation = (float)((1.0 - eased) * EntranceSpinRotations * MathHelper.TwoPi);

        if (t >= 1.0) {
            Scale = _entranceTargetScale;
            Rotation = 0f;
            RemoveEventListener(Event.EnterFrame, OnEntranceFrame);
        }
    }

    private void ShowHubPage() {
        _bookSprite.ChangeTexture(TextureHelper.FromUiAtlas("Book/Static", 0, false));
        _pageContent.AddChild(_hubPage);
    }

    // Named for the direction of travel (hub -> Register/Sign In is "forward", Back -> hub is
    // "backward"), not for which asset folder happens to produce the right look - confirmed
    // with the user that "PageFlipLeft" is the animation that reads as forward and
    // "PageFlipRight" as backward for this asset, the opposite of what the folder names suggest.
    private void FlipForward(Container nextPage) => Flip(nextPage, "PageFlipLeft");

    private void FlipBackward(Container nextPage) => Flip(nextPage, "PageFlipRight");

    // Hides the current page's content, plays the page-flip animation (the actual content swap
    // happens once it settles, not mid-flip, so it doesn't need to line up with a specific curl
    // frame), then shows the new page.
    private void Flip(Container nextPage, string animationName) {
        _pageContent.RemoveChildAt(0);

        PlayAnimation(animationName, 9, PageFlipFrameMs, () => {
            _bookSprite.ChangeTexture(TextureHelper.FromUiAtlas("Book/Static", 0, false));
            _pageContent.AddChild(nextPage);
        });
    }

    private void OnCloseClicked() {
        if (_closing) {
            return;
        }

        _closing = true;
        _closeButton.Visible = false;

        if (_pageContent.NumChildren > 0) {
            _pageContent.RemoveChildAt(0);
        }

        PlayAnimation("Close", 5, OpenCloseFrameMs, OnClosedToCover);
    }

    // Mirrors the entrance (see OnEntranceFrame) exactly in reverse: the Close animation above
    // already lands _bookSprite back on its fully-shut texture, same closed look as the very
    // first frame it opened from - showing the cover art on top of it again here is what makes
    // that "closed book" moment read the same coming in as going out. Sits there closed for
    // CoverHoldMs (same hold the entrance uses before opening) before shrinking back down to a
    // point while spinning and fading out over FadeInMs (same duration/rotation count as growing
    // in) - skipping this hold and going straight into the spin made the whole close read as
    // rushed compared to the entrance's pacing, per feedback. The hold is timed via the same
    // elapsed-accumulation EnterFrame loop as the spin below, NOT a separate Timer object - this
    // callback runs from inside PlayAnimation's own frame Timer, and a Timer constructed and
    // Start()-ed from within another Timer's TimerEvent.Timer callback never actually renders its
    // target visible (see the openDelay/closeButtonDelay comment above for the same gotcha).
    // Only once the whole hold+spin sequence finishes does this hand off to CloseOverlay (which
    // actually removes this overlay and lets the title screen behind it fade back in).
    private void OnClosedToCover() {
        _coverArt.Visible = true;
        _exitElapsedMs = 0;
        _exitHoldDone = false;
        AddEventListener(Event.EnterFrame, OnExitFrame);
    }

    private void OnExitFrame() {
        _exitElapsedMs += Stage.GameTime.ElapsedMs;

        if (!_exitHoldDone) {
            if (_exitElapsedMs < CoverHoldMs) {
                return;
            }

            _exitHoldDone = true;
            _exitElapsedMs -= CoverHoldMs;
        }

        var t = Math.Clamp(_exitElapsedMs / FadeInMs, 0.0, 1.0);
        var eased = 1.0 - Math.Pow(1.0 - t, 3.0);

        Scale = _entranceTargetScale * (float)(1.0 - eased);
        Rotation = (float)(eased * EntranceSpinRotations * MathHelper.TwoPi);
        Alpha = (float)(1.0 - eased);

        if (t >= 1.0) {
            Scale = Vector2.Zero;
            Rotation = 0f;
            Alpha = 0f;
            RemoveEventListener(Event.EnterFrame, OnExitFrame);
            CloseOverlay();
        }
    }

    private void PlayAnimation(string name, int frameCount, int frameMs, Action onComplete) {
        _frameTimer?.Stop();

        _animationName = name;
        _animationFrameCount = frameCount;
        _animationFrame = 1;
        _onAnimationComplete = onComplete;

        _bookSprite.ChangeTexture(TextureHelper.FromUiAtlas($"Book/{name}/{_animationFrame}", 0, false));

        _frameTimer = new Timer(frameMs, frameCount - 1);
        _frameTimer.AddEventListener(TimerEvent.Timer, AdvanceAnimationFrame);
        _frameTimer.Start();
    }

    private void AdvanceAnimationFrame() {
        _animationFrame++;
        _bookSprite.ChangeTexture(TextureHelper.FromUiAtlas($"Book/{_animationName}/{_animationFrame}", 0, false));

        if (_animationFrame >= _animationFrameCount) {
            _onAnimationComplete?.Invoke();
        }
    }

    private void OnRegister(TextInput usernameInput, TextInput passwordInput) {
        AddEventListener(AppRequests.Register(usernameInput.Text, passwordInput.Text), OnRegisterResponse);
    }

    private void OnRegisterResponse(AppResponse response) {
        if (!response.Success) {
            DialogManager.Enqueue(new Dialog("Register Error", response.Message, new DialogOption("Ok")));
            return;
        }

        // AppRequests.Register already logs the new account in (it calls VerifyAsync itself on
        // success) - dispatch the same event OnSignInResponse does so the title screen behind
        // this overlay refreshes to the logged-in header instead of sitting stale as a guest.
        CloseOverlay();
        DispatchEvent(new Event(LoginEvent));
    }

    private void OnSignIn(TextInput usernameInput, TextInput passwordInput) {
        AddEventListener(AppRequests.VerifyAsync(usernameInput.Text, passwordInput.Text, true), OnSignInResponse);
    }

    private void OnSignInResponse(AppResponse response) {
        if (!response.Success) {
            DialogManager.Enqueue(new Dialog("Login Error", response.Message, new DialogOption("Ok")));
            return;
        }

        CloseOverlay();
        DispatchEvent(new Event(LoginEvent));
    }
}
