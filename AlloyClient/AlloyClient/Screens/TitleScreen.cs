using System;
using AlloyClient.AppEngine;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Loading;
using AlloyClient.Screens.Components;
using AlloyClient.Screens.Components.Containers;
using AlloyClient.Ui.Components.Buttons;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Graphics;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Screens;

public class TitleScreen : TitleScreenBase {

    public const int FontSize = 24;

    // PLAY/PORTAL/SETTINGS all render at this same size, so they line up in a
    // neat, even list. Icon is sized a bit bigger than the word's cap height on purpose, so it
    // still reads as the dominant part of each row.
    private const float RowFontSize = 30f;
    private const int RowHeight = 32;

    // The guest header ("Click PLAY to begin your adventure") is ONE line laid out piece by piece (BuildGuestHeader), so
    // it must fit the scroll's writable width (InteriorWidth - padding = 284 design px) at this size. 24 was right for
    // the thin cursive font used before every font went back to MyriadPro (2026-09-21), whose wider glyphs pushed the
    // line off the scroll; 16 fits with a little room. The welcome-back line keeps its own size below.
    private const int HeaderFontSize = 16;

    // The pulsing PLAY word doesn't need to read as bigger than the rest of the sentence - it
    // already stands out via font + motion - so it renders a touch smaller than the surrounding
    // text, which also tightens up the spacing around it.
    private const float PlayWordFontSize = 15f;

    // BitPotion is a thin pixel face and any outline on it comes out blurry, so the word has none: it is drawn twice, the
    // second copy one design px to the right, which thickens every stroke while keeping the pixel edges crisp.
    private const float PlayWordOutlineThickness = 0f;
    private const int PlayWordBoldOffset = 1;

    // The logged-in "Grete thee wel, {username}" message only - it's a single short line (no
    // multi-segment layout to balance against, unlike the guest header), so it can run bigger
    // than HeaderFontSize without needing to match it.
    private const float WelcomeBackFontSize = 26f;

    private const uint HeaderColor = 0x000000;
    private const uint HeaderOutlineColor = 0x000000;
    private const float HeaderOutlineThickness = 3f;

    // Layout (1280x720 design canvas, scaled with the window): one parchment scroll, centred on the middle of the client, with
    // the animated game logo at the top of it and the header and rows underneath. The whole block scales together, and fades
    // out while a popup (the sign-in book) is open.
    private const float BlockScaleMultiplier = 0.85f;

    // The animated logo (see AnimatedTitleLogo, built from Desktop/AnimatedTitleLogo.mp4 by
    // Tools/BookUi/build_title_logo_anim.py), drawn on the scroll. Width/height in the scroll's own units, matching the art's
    // portrait shape (352:440); it sits just under the scroll's top ornament, and the header starts right below it.
    private const int LogoWidth = 232;
    private const int LogoHeight = 290;
    private const int LogoTopPadding = 4;

    // The writable area of the scroll (design px, relative to the scroll's own top / centre): the parchment between the
    // ribbon's ornament line and the bottom ornament. Header and rows live in here.
    private const int InteriorTop = 96;
    private const int InteriorCenterXOffset = 0;
    private const int InteriorWidth = 292;

    private const int FramePaddingX = 4;

    private const int FrameTopPadding = 8;
    private const int HeaderGap = 30;

    // Extra vertical space (design px) under the guest header's line, before the rows. Fixed, not measured.
    private const int GuestHeaderReserve = 28;
    private const int RowGap = 10;

    // The guest header ("Click PLAY to begin your adventure") is split into these three pieces so
    // the middle word can pulse on its own as a "this is the clickable part" hint, independent of
    // the surrounding plain text. Kept as flat constants rather than a template/placeholder scheme
    // since there's only the one guest message right now.
    private const string GuestHeaderPrefix = "Click";
    private const string GuestHeaderWord = "PLAY";
    private const string GuestHeaderSuffix = "to begin your adventure";

    // Deliberately much subtler than MenuBarButton's old button pulse - this is a small word
    // inside a sentence, not a button, so it only needs to read as "this is the clickable part"
    // at a glance, not visibly throb. One full swell (grow + shrink) takes HeaderWordPulsePeriodMs; the size only ever
    // changes through a smooth sine, and the word is scaled around a fixed centre point (see BuildGuestHeader), so
    // nothing steps or snaps between frames.
    private const float HeaderWordPulseBase = 1.0f;
    private const float HeaderWordPulseAmplitude = 0.045f;
    private const float HeaderWordPulsePeriodMs = 1900f;

    // The pulsing PLAY word is a different font from the words around it, and each piece's measured width includes its own
    // glyph padding, so the plain "one space" gap on either side of it does not look like one space. Measured from a
    // screenshot (ink to ink: Click-PLAY 14 px, PLAY-to 5 px, a normal word space 8 px) - these nudge each side (design
    // px, + wider / - tighter) so both gaps match the rest of the sentence.
    private const float PlayWordGapBeforeAdjust = 1.0f;
    private const float PlayWordGapAfterAdjust = 1.5f;

    // Faster, small pulse still used by the "Switch accounts" link (unchanged from before).
    private const float SwitchAccountsPulseAmplitude = 0.02f;
    private const float SwitchAccountsPulseSpeedMs = 200f;

    // Row / link hover colour: a dark bronze that stays clearly visible on the parchment (the old stock light gold
    // blended into it).
    private const uint HoverColor = 0x9C5F12;

    // LOCKED layout bounds, by explicit user decision - do NOT derive this from row buttons, header text, or anything
    // else, ever again, including when adding/removing/resizing rows in the future. These two numbers are the menu
    // frame's size (Content/Ui/Frames/MenuScroll.png scaled to it; that art's plain middle band was shortened to match the
    // removal of the LEGENDS row and then lengthened again to make room for the logo, so nothing is stretched). Change
    // them only to match new frame art.
    private const int FrameWidth = 370;
    private const int FrameHeight = 724;

    // How see-through the scroll picture is (the text on it stays fully solid): the map behind it shows through.
    private const float FrameAlpha = 0.72f;

    // Logged-in header message - just one for now, but picked at random so more can be dropped in
    // later. The guest message is built from GuestHeaderPrefix/Word/Suffix above instead, since it
    // needs to isolate the pulsing "PLAY" word.
    private static readonly string[] WelcomeBackMessages = {
        "Grete thee wel, {0}!"
    };

    // A little smaller than the header itself, per usual game convention for a "not you?" style
    // link tucked under a welcome message.
    private const float SwitchAccountsFontSize = 10f;

    // Thin, to suit the small text (the header's heavy outline would clog it up).
    private const float SwitchAccountsOutlineThickness = 1f;
    private const int SwitchAccountsGap = 3;

    private readonly Container _root;
    private readonly Container _logoRoot;

    // Null when logged in (the welcome-back message has no pulsing word). A holder sitting at the word's centre: the
    // word is a child of it, so the pulse just scales the holder around that fixed point.
    private readonly Container _headerPlayWord;

    // Null when logged out - only the welcome-back header gets this.
    private readonly TextButton _switchAccountsButton;

    private readonly TitleMenuButton _play;
    private readonly TitleMenuButton _portal;
    private readonly TitleMenuButton _settings;
    private readonly TitleMenuButton[] _rows;

    public TitleScreen() : base(Components.ScreenType.Title) {
        // PLAY and PORTAL are wired up; SETTINGS is a placeholder until it gets built. SERVERS was removed (2026-09-21): with one server
        // there is nothing to pick on the title screen - the server list and where PLAY spawns you live on the Character Book's FAST TRAVEL
        // page instead. LEGENDS was removed earlier.
        _play = new TitleMenuButton("PLAY", OnPlay, RowFontSize, RowHeight, HoverColor);
        // PORTAL opens the in-client Portal (PortalScreen): the same player profiles, leaderboards, guilds and wiki as portal.<domain>.
        _portal = new TitleMenuButton("PORTAL", OnPortal, RowFontSize, RowHeight, HoverColor);
        _settings = new TitleMenuButton("SETTINGS", null, RowFontSize, RowHeight, HoverColor);
        _rows = [_play, _portal, _settings];

        var isLoggedIn = TryGetWelcomeBackMessage(out var welcomeMessage);
        Sprite[] headerSegments = isLoggedIn
            ? [MakeHeaderText(welcomeMessage, UiAnchor.Middle, fontSize: WelcomeBackFontSize)]
            : BuildGuestHeader(out _headerPlayWord);

        if (isLoggedIn) {
            _switchAccountsButton = new TextButton(new TextButtonConfig {
                Text = "Switch accounts",
                FontSize = SwitchAccountsFontSize,
                FontType = FontType.Normal,
                FontGroup = FontGroup.MyriadPro,
                ActiveColor = HeaderColor,
                HoverColor = HoverColor,
                OutlineColor = HeaderOutlineColor,
                OutlineThickness = SwitchAccountsOutlineThickness,
                OnClicked = OnSwitchAccounts,
                Anchor = UiAnchor.Middle
            });
        }

        // Stacked top-to-bottom starting just under the frame's top padding - header first
        // (leaving room for it was the whole point of the new frame art), then the row list. The header and
        // every row are centred on the middle of the scroll.
        // The logo takes the top of the writable area; the header and rows start right under it.
        var top = -FrameHeight / 2f + InteriorTop + LogoTopPadding + LogoHeight;

        // All header segments share one font/size, so they're all the same height regardless of
        // which piece(s) got built above - any one of them is a valid reference for it.
        var headerHeight = headerSegments[0].Height;
        var headerY = (int)(top + FrameTopPadding + headerHeight / 2f);
        foreach (var segment in headerSegments) {
            segment.Y = headerY;
        }

        // The guest header is a single line, but it keeps the space the old two-line version took: the line sits in the
        // middle of that space and the rows stay where they were tuned to be.
        var extraHeaderHeight = 0f;
        if (!isLoggedIn) {
            extraHeaderHeight = GuestHeaderReserve;
            foreach (var segment in headerSegments) {
                segment.Y = (int)(headerY + GuestHeaderReserve / 2f);
            }
        }

        // Positioned in the same gap the rows already start after (HeaderGap) rather than adding
        // its own extra space - the rows stay exactly where they were before this button existed.
        if (_switchAccountsButton != null) {
            _switchAccountsButton.Y = (int)(headerY + headerHeight / 2f + SwitchAccountsGap + _switchAccountsButton.Height / 2f);
        }

        var rowY = headerY + headerHeight / 2f + extraHeaderHeight + HeaderGap;
        foreach (var row in _rows) {
            row.X = 0;   // each word is centred on the scroll's middle
            rowY += row.ContentHeight / 2f;
            row.Y = (int)rowY;
            rowY += row.ContentHeight / 2f + RowGap;
        }

        _root = new Container();
        _root.AddChild(BuildFrame());

        // Everything on the scroll is laid out around the writable area's centre, in its own (optionally offset) container.
        var content = new Container { X = InteriorCenterXOffset };
        foreach (var segment in headerSegments) {
            content.AddChild(segment);
        }
        if (_switchAccountsButton != null) {
            content.AddChild(_switchAccountsButton);
        }
        foreach (var row in _rows) {
            content.AddChild(row);
        }
        _root.AddChild(content);

        // The logo sits on the scroll (a child of the same scaled block), centred under the top ornament.
        _logoRoot = new Container { Y = (int)(-FrameHeight / 2f + InteriorTop + LogoTopPadding + LogoHeight / 2f) };
        _logoRoot.AddChild(new AnimatedTitleLogo(LogoWidth, LogoHeight));
        _root.AddChild(_logoRoot);

        AddChild(_root);

        _headerPlayWord?.AddEventListener(Event.EnterFrame, OnHeaderPlayWordPulse);
        _switchAccountsButton?.AddEventListener(Event.EnterFrame, OnSwitchAccountsPulse);

        CheckForAppFailure();

        // An out-of-date build can't get into the game, so say so right away (PLAY also re-shows this - see OnPlay).
        VersionCheck.ShowDialogIfOutdated();
    }

    // "Click" + a separately-anchored, pulsing "PLAY" + "to begin your adventure" - centered as a
    // group around local X = 0, the same spot the single centered header text used to sit.
    private static Sprite[] BuildGuestHeader(out Container playWordHolder) {
        // SimpleText.Width is a bounding box over its actual rendered glyph quads, so it silently
        // drops any leading/trailing whitespace that isn't flanked by another glyph in the same
        // node - not usable directly for the gap around a word split out into its own node like
        // this. Measuring "A B" against "AB" sidesteps that: the space becomes interior to that
        // throwaway text, so its true advance (with correct kerning) shows up in the difference.
        var gapBeforeWord = MeasureGap(GuestHeaderPrefix, GuestHeaderWord) + PlayWordGapBeforeAdjust;
        var gapAfterWord = MeasureGap(GuestHeaderWord, GuestHeaderSuffix) + PlayWordGapAfterAdjust;

        var prefix = MakeHeaderText(GuestHeaderPrefix, UiAnchor.MiddleLeft);
        // The word lives inside a holder placed at the word's centre, with the word offset by half its own width
        // (a whole number, set once). The pulse only ever changes the holder's Scale, so the word grows and shrinks
        // smoothly around its centre - no per-frame integer positions to round, which is what used to make it step.
        var word = MakeHeaderText(GuestHeaderWord, UiAnchor.MiddleLeft, FontGroup.MyriadPro, PlayWordFontSize, PlayWordOutlineThickness);
        var suffix = MakeHeaderText(GuestHeaderSuffix, UiAnchor.MiddleLeft);

        // All three pieces on one line, centred as a group.
        var groupLeft = -(prefix.Width + gapBeforeWord + word.Width + gapAfterWord + suffix.Width) / 2f;
        prefix.X = (int)groupLeft;
        var holder = new Container { X = (int)(groupLeft + prefix.Width + gapBeforeWord + word.Width / 2f) };
        word.X = -word.Width / 2;
        holder.AddChild(word);
        var wordBold = MakeHeaderText(GuestHeaderWord, UiAnchor.MiddleLeft, FontGroup.MyriadPro, PlayWordFontSize, PlayWordOutlineThickness);
        wordBold.X = word.X + PlayWordBoldOffset;
        holder.AddChild(wordBold);
        suffix.X = (int)(groupLeft + prefix.Width + gapBeforeWord + word.Width + gapAfterWord);

        playWordHolder = holder;
        return [prefix, holder, suffix];
    }

    // Width("before after") - Width("beforeafter") = the true advance of the space between them
    // (kerning and all), by making it interior to a throwaway, never-displayed text node instead
    // of a leading/trailing edge that SimpleText.Width can't see (see BuildGuestHeader above).
    private static float MeasureGap(string before, string after) =>
        MakeHeaderText($"{before} {after}", UiAnchor.LeftTop).Width - MakeHeaderText($"{before}{after}", UiAnchor.LeftTop).Width;

    private static SimpleText MakeHeaderText(string text, UiAnchor anchor, FontGroup fontGroup = FontGroup.MyriadPro, float fontSize = HeaderFontSize, float outline = HeaderOutlineThickness) => new(new TextConfig {
        Text = text,
        FontSize = fontSize,
        FontType = FontType.Normal,
        FontGroup = fontGroup,
        Color = HeaderColor,
        OutlineColor = HeaderOutlineColor,
        OutlineThickness = outline,
        MaxWidth = InteriorWidth - FramePaddingX * 2,
        Anchor = anchor
    });

    private void OnHeaderPlayWordPulse() {
        var pulse = HeaderWordPulseBase + HeaderWordPulseAmplitude * (float)Math.Sin(Stage.GameTime.TotalMs / HeaderWordPulsePeriodMs * Math.Tau);
        _headerPlayWord.Scale = new Vector2(pulse);
    }

    // The scroll, centred on this block's origin at its exact (locked) FrameWidth x FrameHeight.
    private static ObjectRect BuildFrame() => new(new ObjectRectConfig {
        Texture = TextureHelper.FromUiAtlasLinear("Frames/MenuScroll", 0, false),
        X = 0,
        Y = 0,
        Width = FrameWidth,
        Height = FrameHeight,
        Alpha = FrameAlpha,
        Anchor = UiAnchor.Middle,
        OutlineEnabled = false,
        GlowEnabled = false
    });

    protected override void OnResize(ResizeEvent args) {
        // Each block is scaled once here - nothing nested inside sets its own Scale, so it all inherits it uniformly.
        var scale = Stage.ScreenScale * BlockScaleMultiplier;

        _root.Scale = scale;
        _root.X = Stage.StageWidth / 2;
        _root.Y = Stage.StageHeight / 2;

        base.OnResize(args);
    }

    private static bool TryGetWelcomeBackMessage(out string message) {
        // AccountData alone isn't a reliable "is logged in" signal - the account server's
        // /char/list falls back to a "Guest" account whenever it's called without valid
        // credentials (which happens on every app startup via AppRequests.GetCharList), so
        // AccountData is always present even when nobody's actually logged in. LoginData is the
        // same signal OnPlay() uses to decide whether to prompt a login.
        if (GlobalData.Contains<LoginData>()) {
            var account = GlobalData.Get<AccountData>();
            if (account != null) {
                var template = WelcomeBackMessages[Random.Shared.Next(WelcomeBackMessages.Length)];
                message = string.Format(template, account.Name);
                return true;
            }
        }

        message = null;
        return false;
    }

    // ALLOY_PERFTEST only (Game/DevPerfTest.cs): press PLAY by itself once the saved sign-in is loaded.
    private double _perfTestWaitMs;
    private bool _perfTestPressed;

    public override void Update(Alloy.Engine.GameTime gameTime) {
        base.Update(gameTime);
        if (!AlloyClient.Dev.DevPerfTest.Enabled || _perfTestPressed || !GlobalData.Contains<LoginData>())
            return;
        _perfTestWaitMs += gameTime.ElapsedMs;
        if (_perfTestWaitMs < 3000)
            return;
        _perfTestPressed = true;
        OnPlay();
    }

    private void OnPlay() {
        // The server would turn this build away at connect time anyway: stop here, with the reason and where to get the current one.
        if (VersionCheck.ShowDialogIfOutdated()) {
            return;
        }

        if (GlobalData.Contains<LoginData>()) {
            // Straight to the book - the character list was fetched with the sign-in. Falls back to the logo loader
            // only if that fetch didn't happen or failed - see LoaderFlows.
            LoaderFlows.FromTitleToCharacterList();
        } else {
            // On success the book closes back to this same title screen instance (see
            // BookOverlay's own close-before-dispatch in OnSignInResponse/OnRegisterResponse)
            // rather than jumping straight into character select - but this instance was built
            // back when GlobalData had no LoginData yet, so its header/row list are still frozen
            // on the guest text. Rebuilding the screen from scratch here (same as
            // OnSwitchAccounts) picks up the now-logged-in state instead of leaving it stale
            // until some unrelated resize/navigation happens to reconstruct it.
            var book = new BookOverlay();
            book.AddEventListener(BookOverlay.LoginEvent, () => ScreenManager.FadeTo(new TitleScreen()));
            OverlayManager.Set(book);
        }
    }

    private static void OnPortal() {
        ScreenManager.FadeTo(new PortalScreen());
    }

    private void OnSwitchAccounts() {
        GlobalData.Logout();
        ScreenManager.FadeTo(new TitleScreen());
    }

    // Same subtle pulse math as OnHeaderPlayWordPulse, but this button is a standalone
    // Middle-anchored sprite (not a segment inside a wider centered sentence), so a plain Scale
    // assignment recenters it correctly on its own - no need for the manual X-recentering that
    // word needs.
    private void OnSwitchAccountsPulse() {
        var pulse = HeaderWordPulseBase + SwitchAccountsPulseAmplitude * (float)Math.Sin(Stage.GameTime.TotalMs / SwitchAccountsPulseSpeedMs);
        _switchAccountsButton.Scale = new Vector2(pulse);
    }

    // Fades the header/row list out alongside TitleScreenBase's splash graphic when an overlay
    // (login, register, and later settings/servers/account) opens, and back in when it closes -
    // see OverlayManager.OverlayOpened/Closed and TitleScreenBase.OnOverlayOpened/Closed.
    protected override void OnTitleContentVisibilityChanged(bool visible) {
        _root.AddAlphaTween(visible ? 0f : 1f, visible ? 1f : 0f, ContentFadeDuration);
    }

    private void CheckForAppFailure() {
        if (!GlobalData.TryRemove<AppRequestFailedFlag>(out var data)) {
            return;
        }

        AddChild(new ScreenDarkenOverlay());

        DialogManager.Enqueue(new RetryLoadDialog(data.Message));
    }
}
