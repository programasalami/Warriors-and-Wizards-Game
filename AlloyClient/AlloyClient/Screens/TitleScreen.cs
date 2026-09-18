using System;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Screens.Components;
using AlloyClient.Screens.Components.Containers;
using AlloyClient.Ui.Components.Buttons;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Ui.Components.Graphics;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using OpenTK.Mathematics;

namespace AlloyClient.Screens;

public class TitleScreen : TitleScreenBase {

    // Used elsewhere (e.g. ServersTitleScreen's back button) - unrelated to the row buttons below.
    public const int FontSize = 24;

    // PLAY/SERVERS/LEGENDS/ACCOUNT/SETTINGS all render at this same size, so they line up in a
    // neat, even list. Icon is sized a bit bigger than the word's cap height on purpose, so it
    // still reads as the dominant part of each row.
    private const float RowFontSize = 36f;
    private const int RowIconSize = 38;

    // Bumped up from the original 14, then again from 23, then again from 26, per repeated
    // feedback that it should read bigger still - NotJamSignature21's thin cursive strokes read
    // as barely legible at small sizes against the busy parchment texture, even with an outline.
    // MaxWidth below is a wrap safety net if a future message doesn't fit at this size.
    private const int HeaderFontSize = 30;

    // The pulsing PLAY word doesn't need to read as bigger than the rest of the sentence - it
    // already stands out via font + motion - so it renders a touch smaller than the surrounding
    // text, which also tightens up the spacing around it.
    private const float PlayWordFontSize = 19f;

    // The logged-in "Grete thee wel, {username}" message only - it's a single short line (no
    // multi-segment layout to balance against, unlike the guest header), so it can run bigger
    // than HeaderFontSize without needing to match it.
    private const float WelcomeBackFontSize = 34f;

    private const uint HeaderColor = 0x000000;
    private const uint HeaderOutlineColor = 0x000000;
    private const float HeaderOutlineThickness = 4f;

    // The whole block (frame + everything on it) rendered this much bigger than 1:1 screen
    // scale.
    private const float BlockScaleMultiplier = 1.2f;

    // Horizontal position of the block's center as a fraction of the screen's width - 0.5 would
    // be dead center. Measured directly from TitleScreenGraphic.png's drawn-in scroll: its
    // parchment area spans roughly x=163 to x=668 of the 1451px-wide source image, centering it
    // at ~28.6% across. Internal (not private) so BookOverlay can spawn at this exact same spot
    // when it replaces the scroll - re-measure and update both if the background art changes.
    internal const float HorizontalPositionFraction = 0.286f;

    // Small downward nudge off dead-center, fine-tuned by eye against the drawn-in scroll.
    internal const int VerticalNudge = 20;

    private const int FramePaddingX = 34;

    // Extra breathing room beyond the frame's base left padding, so the row icons don't sit
    // right at the frame's edge.
    private const int RowsExtraLeftMargin = 30;

    private const int FrameTopPadding = 110;
    private const int HeaderGap = 32;
    private const int RowGap = 12;

    // The guest header ("Click PLAY to begin your adventure") is split into these three pieces so
    // the middle word can pulse on its own as a "this is the clickable part" hint, independent of
    // the surrounding plain text. Kept as flat constants rather than a template/placeholder scheme
    // since there's only the one guest message right now.
    private const string GuestHeaderPrefix = "Click";
    private const string GuestHeaderWord = "PLAY";
    private const string GuestHeaderSuffix = "to begin your adventure";

    // Deliberately much subtler than MenuBarButton's old button pulse - this is a small word
    // inside a sentence, not a button, so it only needs to read as "this is the clickable part"
    // at a glance, not visibly throb.
    private const float HeaderWordPulseBase = 1.0f;
    private const float HeaderWordPulseAmplitude = 0.02f;
    private const float HeaderWordPulseSpeedMs = 200f;

    // LOCKED layout bounds, by explicit user decision - do NOT derive this from row buttons,
    // header text, or anything else, ever again, including when adding/removing/resizing rows in
    // the future. The scroll background image itself has been removed (per user request), but
    // these two numbers still define the same invisible layout area the header/rows are
    // positioned within, so removing the image didn't move or resize anything else. If this
    // needs to be a different size, that's a direct, explicit edit to these two constants and
    // nothing else - never reintroduce measuring text/buttons to compute them.
    private const int FrameWidth = 420;
    private const int FrameHeight = 540;

    // Logged-in header message - just one for now, but picked at random so more can be dropped in
    // later. The guest message is built from GuestHeaderPrefix/Word/Suffix above instead, since it
    // needs to isolate the pulsing "PLAY" word.
    private static readonly string[] WelcomeBackMessages = {
        "Grete thee wel, {0}!"
    };

    // A little smaller than the header itself, per usual game convention for a "not you?" style
    // link tucked under a welcome message.
    private const float SwitchAccountsFontSize = 20f;
    private const int SwitchAccountsGap = -4;

    private readonly Container _root;

    // Null when logged in (the welcome-back message has no pulsing word).
    private readonly SimpleText _headerPlayWord;

    // Null when logged out - only the welcome-back header gets this.
    private readonly TextButton _switchAccountsButton;

    // The pulsing word's fixed center X and its width at rest (Scale = 1) - see
    // OnHeaderPlayWordPulse for why these are needed instead of just letting a Middle anchor
    // handle centering automatically.
    private readonly float _headerPlayWordCenterX;
    private readonly int _headerPlayWordBaseWidth;

    private readonly TitleMenuButton _play;
    private readonly TitleMenuButton _servers;
    private readonly TitleMenuButton _legends;
    private readonly TitleMenuButton _account;
    private readonly TitleMenuButton _settings;
    private readonly TitleMenuButton[] _rows;

    public TitleScreen() : base(Components.ScreenType.Title) {
        // Only PLAY is wired up for now. SERVERS/LEGENDS technically navigate somewhere already,
        // but neither is in a working state (server list doesn't render right, legends screen was
        // never finished), so they're placeholders like ACCOUNT/SETTINGS until those get properly
        // built (planned after the login frame and character select/creation revamps).
        _play = new TitleMenuButton("PLAY", "Icons/TitlePlayButtonIcon", OnPlay, RowFontSize, RowIconSize);
        _servers = new TitleMenuButton("SERVERS", "Icons/TitleServersButtonIcon", null, RowFontSize, RowIconSize);
        _legends = new TitleMenuButton("LEGENDS", "Icons/TitleLegendsButtonIcon", null, RowFontSize, RowIconSize);
        _account = new TitleMenuButton("ACCOUNT", "Icons/TitleAccountButtonIcon", null, RowFontSize, RowIconSize);
        _settings = new TitleMenuButton("SETTINGS", "Icons/TitleSettingsButtonIcon", null, RowFontSize, RowIconSize);
        _rows = [_play, _servers, _legends, _account, _settings];

        var isLoggedIn = TryGetWelcomeBackMessage(out var welcomeMessage);
        var headerSegments = isLoggedIn
            ? [MakeHeaderText(welcomeMessage, UiAnchor.Middle, fontSize: WelcomeBackFontSize)]
            : BuildGuestHeader(out _headerPlayWord, out _headerPlayWordCenterX, out _headerPlayWordBaseWidth);

        if (isLoggedIn) {
            _switchAccountsButton = new TextButton(new TextButtonConfig {
                Text = "Switch accounts",
                FontSize = SwitchAccountsFontSize,
                FontType = FontType.Normal,
                FontGroup = FontGroup.NotJamSignature21,
                ActiveColor = HeaderColor,
                OutlineColor = HeaderOutlineColor,
                OutlineThickness = HeaderOutlineThickness,
                OnClicked = OnSwitchAccounts,
                Anchor = UiAnchor.Middle
            });
        }

        // Stacked top-to-bottom starting just under the frame's top padding - header first
        // (leaving room for it was the whole point of the new frame art), then the row list. The
        // header stays centered on the frame, but the rows are flush against the frame's left
        // padding edge instead of centered - all of them share that same left edge so they stay
        // lined up with each other.
        var top = -FrameHeight / 2f;
        var rowsLeft = -FrameWidth / 2f + FramePaddingX + RowsExtraLeftMargin;

        // All header segments share one font/size, so they're all the same height regardless of
        // which piece(s) got built above - any one of them is a valid reference for it.
        var headerHeight = headerSegments[0].Height;
        var headerY = (int)(top + FrameTopPadding + headerHeight / 2f);
        foreach (var segment in headerSegments) {
            segment.Y = headerY;
        }

        // Positioned in the same gap the rows already start after (HeaderGap) rather than adding
        // its own extra space - the rows stay exactly where they were before this button existed.
        if (_switchAccountsButton != null) {
            _switchAccountsButton.Y = (int)(headerY + headerHeight / 2f + SwitchAccountsGap + _switchAccountsButton.Height / 2f);
        }

        var rowY = headerY + headerHeight / 2f + HeaderGap;
        foreach (var row in _rows) {
            row.X = (int)rowsLeft;
            rowY += row.ContentHeight / 2f;
            row.Y = (int)rowY;
            rowY += row.ContentHeight / 2f + RowGap;
        }

        _root = new Container();
        foreach (var segment in headerSegments) {
            _root.AddChild(segment);
        }
        if (_switchAccountsButton != null) {
            _root.AddChild(_switchAccountsButton);
        }
        foreach (var row in _rows) {
            _root.AddChild(row);
        }
        AddChild(_root);

        _headerPlayWord?.AddEventListener(Event.EnterFrame, OnHeaderPlayWordPulse);
        _switchAccountsButton?.AddEventListener(Event.EnterFrame, OnSwitchAccountsPulse);

        CheckForAppFailure();
    }

    // "Click" + a separately-anchored, pulsing "PLAY" + "to begin your adventure" - centered as a
    // group around local X = 0, the same spot the single centered header text used to sit.
    private static SimpleText[] BuildGuestHeader(out SimpleText playWord, out float playWordCenterX, out int playWordBaseWidth) {
        // SimpleText.Width is a bounding box over its actual rendered glyph quads, so it silently
        // drops any leading/trailing whitespace that isn't flanked by another glyph in the same
        // node - not usable directly for the gap around a word split out into its own node like
        // this. Measuring "A B" against "AB" sidesteps that: the space becomes interior to that
        // throwaway text, so its true advance (with correct kerning) shows up in the difference.
        var gapBeforeWord = MeasureGap(GuestHeaderPrefix, GuestHeaderWord);
        var gapAfterWord = MeasureGap(GuestHeaderWord, GuestHeaderSuffix);

        var prefix = MakeHeaderText(GuestHeaderPrefix, UiAnchor.MiddleLeft);
        // MiddleLeft, not Middle - a Middle anchor recenters itself via (ContentWidth/2 * ScaleX),
        // cast to int every frame; animating Scale for the pulse made that recompute unevenly and
        // visibly snap sideways instead of breathing in place. MiddleLeft's anchor offset is a
        // constant 0 regardless of scale, so OnHeaderPlayWordPulse repositions X by hand instead
        // (using playWordCenterX/playWordBaseWidth below) for a properly centered pulse.
        var word = MakeHeaderText(GuestHeaderWord, UiAnchor.MiddleLeft, FontGroup.CrunchyFont, PlayWordFontSize);
        var suffix = MakeHeaderText(GuestHeaderSuffix, UiAnchor.MiddleLeft);

        var groupLeft = -(prefix.Width + gapBeforeWord + word.Width + gapAfterWord + suffix.Width) / 2f;
        prefix.X = (int)groupLeft;
        playWordBaseWidth = word.Width;
        playWordCenterX = groupLeft + prefix.Width + gapBeforeWord + word.Width / 2f;
        word.X = (int)(playWordCenterX - word.Width / 2f);
        suffix.X = (int)(groupLeft + prefix.Width + gapBeforeWord + word.Width + gapAfterWord);

        playWord = word;
        return [prefix, word, suffix];
    }

    // Width("before after") - Width("beforeafter") = the true advance of the space between them
    // (kerning and all), by making it interior to a throwaway, never-displayed text node instead
    // of a leading/trailing edge that SimpleText.Width can't see (see BuildGuestHeader above).
    private static float MeasureGap(string before, string after) =>
        MakeHeaderText($"{before} {after}", UiAnchor.LeftTop).Width - MakeHeaderText($"{before}{after}", UiAnchor.LeftTop).Width;

    private static SimpleText MakeHeaderText(string text, UiAnchor anchor, FontGroup fontGroup = FontGroup.NotJamSignature21, float fontSize = HeaderFontSize) => new(new TextConfig {
        Text = text,
        FontSize = fontSize,
        FontType = FontType.Normal,
        FontGroup = fontGroup,
        Color = HeaderColor,
        OutlineColor = HeaderOutlineColor,
        OutlineThickness = HeaderOutlineThickness,
        MaxWidth = FrameWidth - FramePaddingX * 2,
        Anchor = anchor
    });

    private void OnHeaderPlayWordPulse() {
        var pulse = HeaderWordPulseBase + HeaderWordPulseAmplitude * (float)Math.Sin(Stage.GameTime.TotalMs / HeaderWordPulseSpeedMs);
        _headerPlayWord.Scale = new Vector2(pulse);
        _headerPlayWord.X = (int)(_headerPlayWordCenterX - _headerPlayWordBaseWidth * pulse / 2f);
    }

    protected override void OnResize(ResizeEvent args) {
        // The whole block is scaled here, once - nothing nested inside _root sets its own Scale,
        // so it all inherits this uniformly through the display tree.
        var scale = Stage.ScreenScale * BlockScaleMultiplier;
        _root.Scale = scale;

        // Roughly vertically centered, nudged down a bit to line up with the drawn-in scroll.
        _root.X = (int)(Stage.StageWidth * HorizontalPositionFraction);
        _root.Y = Stage.StageHeight / 2 + (int)(VerticalNudge * scale.Y);

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

    private void OnPlay() {
        if (GlobalData.Contains<LoginData>()) {
            ScreenManager.FadeTo(new CharacterListScreen());
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

    private void OnSwitchAccounts() {
        GlobalData.Logout();
        ScreenManager.FadeTo(new TitleScreen());
    }

    // Same subtle pulse math as OnHeaderPlayWordPulse, but this button is a standalone
    // Middle-anchored sprite (not a segment inside a wider centered sentence), so a plain Scale
    // assignment recenters it correctly on its own - no need for the manual X-recentering that
    // word needs.
    private void OnSwitchAccountsPulse() {
        var pulse = HeaderWordPulseBase + HeaderWordPulseAmplitude * (float)Math.Sin(Stage.GameTime.TotalMs / HeaderWordPulseSpeedMs);
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
