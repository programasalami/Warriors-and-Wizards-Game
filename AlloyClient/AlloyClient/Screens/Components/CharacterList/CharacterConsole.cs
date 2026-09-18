using System;
using System.Collections.Generic;
using AlloyClient.Assets.Libraries;
using AlloyClient.Data;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Screens.Components.CharacterList;

// "The Console" (2026-09-17, second pass) - full replacement of "The Muster Hall" roster-list-
// plus-stage layout, built from the "Pocket Inventory Series #9 Pixel Console v1.1" pack
// (Desktop/GUI, "Console 1" color) per request: a handheld-console shell with a D-pad on a grey
// left panel and ABXY buttons on a gold right panel, framing a central "screen" that shows one
// character's profile at a time - browsed with D-pad left/right instead of a scrollable list,
// like flipping through save slots on a real console. A top tab row (Characters/Graveyard) mimics
// the pack's own "Profile" example screen, which has the same kind of tab strip across its top.
//
// The entrance animation was rebuilt to match the pack's actual "Open" animation frame sequence
// (Console 1/PNG/Console 1 - Animation/Gold & Silver/Open, 19 frames) rather than a guessed
// fly-in-from-off-screen slide - that 19-frame sequence's own bounding box stays frozen for
// frames 0-9 (panels sitting closed, touching at center - this is where the screen "powers up")
// and only moves from frame ~10 onward (panels sliding out to the screen's edges). Reproduced
// procedurally (elapsed-time tween through the same two phases) rather than by packing the actual
// frames - each reference frame is a full 759x591 canvas, and 19 of them is far more pixel data
// than the Ui atlas has room for (see the panel/screen assets already needing to be kept small -
// this pack's assets are shared with everything else already packed in Ui.atlas).
//
// All text on the console (tabs, currency, roster names, empty/graveyard messages) renders in
// FontGroup.Occular (the user's own Occular.ttf) rather than the app-wide default MyriadPro, per
// request - this is a 4th font slot (Text4/RegisterQuaternaryFont/PixelRange4 etc., see UiRender/
// Ui.frag), added the same way the 3rd slot (CrunchyFont) was.

public sealed class CharacterConsole : Container {

    // Sized to fill most of the 1280x720 canvas ("almost fullscreen" per request, room for a lot
    // more content later) rather than sitting as a smaller centered block - ScreenCenterY is
    // nudged down from the canvas's true center (360) only enough to clear the header banner
    // above it, not by an arbitrary amount, so the whole shell reads as centered in the space
    // actually available to it.
    private const int ScreenWidth = 780;
    private const int ScreenHeight = 560;
    private const int ScreenCenterX = 640;
    private const int ScreenCenterY = 400;

    private const int PanelWidth = 120;
    private const int PanelHeight = ScreenHeight;

    // Docked (open) panel positions overlap the screen's edge by this much rather than sitting
    // exactly flush against it - the panel art (PanelGrey.png/PanelGold.png) has a few px of its
    // own transparent padding baked in, so a perfectly flush dock still left a visible sliver of
    // gap between the panel's actual opaque pixels and the screen. The panels render on top of
    // the screen (added after it, see the constructor), so overlapping just covers that sliver
    // instead of exposing it. Measured directly via pixel sampling a live screenshot (same
    // technique as the earlier centering-bug fix) - an 8px overlap still left a real ~8-9px gap
    // of visible starfield showing through on both sides, so this needed to be noticeably bigger
    // than the original guess, not just a small bump.
    private const int PanelOverlap = 20;
    private const int GreyPanelDockX = ScreenCenterX - ScreenWidth / 2 - PanelWidth + PanelOverlap;
    private const int GoldPanelDockX = ScreenCenterX + ScreenWidth / 2 - PanelOverlap;
    private const int PanelY = ScreenCenterY - PanelHeight / 2;

    // Closed state: the two panels sit touching at the screen's own center, not off-screen - see
    // the class comment above for why (that's what the pack's own Open animation frames show).
    private const int GreyPanelClosedX = ScreenCenterX - PanelWidth;
    private const int GoldPanelClosedX = ScreenCenterX;

    private const int JogDialSize = 54;
    private const float JogDialFractionY = 0.15f;

    private const int ButtonWidth = 34;
    private const int ButtonHeight = 36;
    private const float ButtonClusterFractionY = 0.36f;
    private const int ButtonDiamondOffset = 30;
    private const float ButtonHoverScale = 1.15f;

    // Four-phase entrance, slowed down considerably from an earlier pass per feedback that it
    // read as one quick blur - each phase now needs to actually register as its own beat: sit
    // invisible for a moment after the screen loads, fade in fully *closed* (so the user
    // consciously sees a shut console appear before anything moves), hold there once fully
    // visible, then slide the panels open.
    private const int InitialDelayMs = 1000;
    private const int FadeInMs = 900;
    private const int HoldClosedMs = 350;
    private const int OpenSlideMs = 700;
    private const int EntranceTotalMs = InitialDelayMs + FadeInMs + HoldClosedMs + OpenSlideMs;

    // A little transparency at rest so the Milky Way background is still visible through the
    // whole shell, per request - applied as this Container's own Alpha, which cascades to every
    // child (screen, panels, text) the same way Scale/Rotation already do elsewhere in this
    // codebase (see BookOverlay's entrance animation for the same cascading-Alpha pattern).
    private const float IdleAlpha = 0.78f;

    private const uint TerminalGreen = 0x8CFFB0;
    private const uint TerminalDim = 0x4E8C68;
    private const uint TextOutline = 0x000000;

    // Tab strip across the top of the screen, mirroring the pack's own "Profile" example (same
    // kind of tab row above the page content) - Profile/Characters/Graveyard.
    private const int TabY = ScreenCenterY - ScreenHeight / 2 + 26;
    private const int TabGap = 28;
    private const float TabFontSize = 17f;

    // A content-safe area inside the screen - same width as the screen, but a bit shorter so
    // nothing sits under the tab row above or the hotkey hint below. Corner-anchored content
    // (the Profile page's gold/fame readout, for now) sits inset from this box's own corner, not
    // the screen's literal corner - "a corner, not the very very corner" per request.
    private const int ContentAreaPaddingTop = 60;
    private const int ContentAreaPaddingBottom = 60;
    private const int ContentAreaTop = ScreenCenterY - ScreenHeight / 2 + ContentAreaPaddingTop;
    private const int ContentAreaBottom = ScreenCenterY + ScreenHeight / 2 - ContentAreaPaddingBottom;
    private const int ContentAreaLeft = ScreenCenterX - ScreenWidth / 2;
    private const int ContentAreaRight = ScreenCenterX + ScreenWidth / 2;
    private const int CornerInset = 26;

    // Roster grid (Characters page) - a fixed-size tile per character (portrait + name) laid out
    // left-to-right, top-to-bottom within the content-safe area, replacing the old one-at-a-time
    // browse view per request. Tile selection is mouse-click-only now (BuildCharacterTile) - the
    // D-pad was repurposed to move between tabs instead (see NextTab/PreviousTab), so there's no
    // D-pad-driven roster browsing anymore.
    private const int TileWidth = 112;
    private const int TileHeight = 112;
    private const int TileGap = 14;
    private const int TilePortraitSize = 44;
    private const float TileNameFontSize = 12f;
    private const uint TileSelectedTint = 0x2A5A3E;
    private const uint TileUnselectedTint = 0x10141C;

    // Profile page currency readout - just the coin icons plus their amounts now (the "GOLD"/
    // "FAME" words removed per request), a single still frame each, no animation (Pixel Reward
    // Series #1 Coins, Static variant frame 0 - Gold for currency, Copper standing in for Fame).
    // Tried the pack's 14-frame Animated/Shine variant first (too intense a flash at this icon
    // size), then the Static variant's own 8-frame slow-spin cycle (still read as distracting
    // motion for something this small) - landed on a single fixed frame per final request.
    private const int CoinIconSize = 20;
    private const int CurrencyIconTextGap = 6;
    private const int CurrencyGroupGap = 22;
    private const float CurrencyFontSize = 17f;

    private enum ConsolePage { Profile, Characters, Graveyard }

    private readonly Container _greyGroup;
    private readonly Container _goldGroup;
    private readonly ObjectRect _screen;
    private readonly Container _rosterContainer;
    private readonly SimpleText _emptyText;
    private readonly SimpleText _profileTab;
    private readonly SimpleText _charactersTab;
    private readonly SimpleText _graveyardTab;
    private readonly SimpleText _graveyardText;
    private readonly ObjectRect _goldCoinIcon;
    private readonly SimpleText _goldAmountText;
    private readonly ObjectRect _fameCoinIcon;
    private readonly SimpleText _fameAmountText;

    private readonly Action _onPlay;
    private readonly Action _onForge;
    private readonly Action _onBack;

    private List<Character> _characters = [];
    private readonly List<ColorRect> _rosterTileBackdrops = [];
    private int _currentIndex;
    private ConsolePage _currentPage = ConsolePage.Profile;

    private double _entranceElapsedMs;
    private bool _entranceComplete;

    private bool _hasCharacter;

    public CharacterConsole(Action onPlay, Action onForge, Action onBack) : base(new ContainerConfig { Width = 1280, Height = 720 }) {
        _onPlay = onPlay;
        _onForge = onForge;
        _onBack = onBack;

        // Stays hidden (see OnEntranceFrame) until the console is fully open - shown from the
        // start, its own "off" texture read as a plain grey box sitting behind the closed panels
        // for the whole entrance, which looked like a rendering glitch rather than the "screen
        // powering on" moment it was meant to be.
        _screen = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/ScreenOn", 0, false),
            Width = ScreenWidth,
            Height = ScreenHeight,
            X = ScreenCenterX,
            Y = ScreenCenterY,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        _screen.Visible = false;
        AddChild(_screen);

        // Every piece of screen content below (tabs, profile/portrait/name/stats, hint) starts
        // hidden and only becomes visible once the console is fully open (see the
        // entrance-complete gate in RefreshDisplay/OnEntranceFrame) - character data can arrive
        // from the server at any point during the entrance, and this content sits well within
        // the still-closed panels' horizontal span, so showing it as soon as it's ready let it
        // peek through the gap between the closed panels before the console had even started
        // opening.
        //
        // Three tabs now (was two) - Profile is the page the console opens to by default (see
        // _currentPage's initializer), with the account-level gold/fame readout living on it as
        // actual page content instead of sitting in the tab row itself. The tab row is reserved
        // for tab labels only (soon to become icons, per request) - built with a measure-then-
        // lay-out pass (construct each at X=0, read its rendered Width, then place all three as
        // one centered group) since the labels aren't all the same width.
        _profileTab = BuildTab("PROFILE", ShowProfileTab);
        _charactersTab = BuildTab("CHARACTERS", ShowCharactersTab);
        _graveyardTab = BuildTab("GRAVEYARD", ShowGraveyardTab);

        var tabsTotalWidth = _profileTab.Width + TabGap + _charactersTab.Width + TabGap + _graveyardTab.Width;
        var tabsLeft = ScreenCenterX - tabsTotalWidth / 2;
        _profileTab.X = tabsLeft + _profileTab.Width / 2;
        _charactersTab.X = tabsLeft + _profileTab.Width + TabGap + _charactersTab.Width / 2;
        _graveyardTab.X = tabsLeft + _profileTab.Width + TabGap + _charactersTab.Width + TabGap + _graveyardTab.Width / 2;

        _profileTab.SetColor(TerminalGreen);
        _profileTab.Visible = false;
        AddChild(_profileTab);
        _charactersTab.Visible = false;
        AddChild(_charactersTab);
        _graveyardTab.Visible = false;
        AddChild(_graveyardTab);

        // Profile page currency readout - gold/fame, moved here from the screen's old header
        // chrome per request. Sits in the top-right corner of the content-safe area (inset from
        // it, not the screen's literal corner) rather than dead-center, per a follow-up request -
        // the safe area itself is inset from the screen's true edges so this never sits under the
        // tab row or the hotkey hint. Icon + number only now, no "GOLD"/"FAME" words - actual
        // X positions get computed in LayoutCurrencyDisplay once the amount text (and therefore
        // its width) is known, called from SetAccountInfo; these starting positions are just
        // placeholders.
        _goldCoinIcon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/GoldCoin0", 0, false),
            Width = CoinIconSize,
            Height = CoinIconSize,
            Y = ContentAreaTop,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        _goldCoinIcon.Visible = false;
        AddChild(_goldCoinIcon);

        _goldAmountText = new SimpleText(new TextConfig {
            Text = "",
            FontSize = CurrencyFontSize,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalGreen,
            OutlineColor = TextOutline,
            OutlineThickness = 2,
            Y = ContentAreaTop,
            Anchor = UiAnchor.MiddleLeft
        });
        AddChild(_goldAmountText);

        _fameCoinIcon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/CopperCoin0", 0, false),
            Width = CoinIconSize,
            Height = CoinIconSize,
            Y = ContentAreaTop,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        _fameCoinIcon.Visible = false;
        AddChild(_fameCoinIcon);

        _fameAmountText = new SimpleText(new TextConfig {
            Text = "",
            FontSize = CurrencyFontSize,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalGreen,
            OutlineColor = TextOutline,
            OutlineThickness = 2,
            Y = ContentAreaTop,
            Anchor = UiAnchor.MiddleLeft
        });
        AddChild(_fameAmountText);

        // Characters page - a grid of character tiles within the same content-safe area, built
        // fresh each time SetCharacters() runs (see BuildRosterGrid). Replaces the old one-
        // character-at-a-time browse view per request.
        _rosterContainer = new Container();
        _rosterContainer.Visible = false;
        AddChild(_rosterContainer);

        _emptyText = new SimpleText(new TextConfig {
            Text = "NO SIGNAL\n\nForge a hero to begin",
            FontSize = 22,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalDim,
            OutlineColor = TextOutline,
            OutlineThickness = 3,
            X = ScreenCenterX,
            Y = ScreenCenterY,
            Anchor = UiAnchor.Middle
        });
        _emptyText.Visible = false;
        AddChild(_emptyText);

        // The Graveyard was never implemented anywhere else in this codebase either (see
        // CharacterListScreen's own old LoadGraveyardList TODO) - this tab just says so plainly
        // rather than pretending it works.
        _graveyardText = new SimpleText(new TextConfig {
            Text = "GRAVEYARD OFFLINE\n\nNo records to display",
            FontSize = 22,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalDim,
            OutlineColor = TextOutline,
            OutlineThickness = 3,
            X = ScreenCenterX,
            Y = ScreenCenterY,
            Anchor = UiAnchor.Middle
        });
        _graveyardText.Visible = false;
        AddChild(_graveyardText);

        // Grey/gold panel plus every piece visually attached to it (jog dial, D-pad/ABXY
        // buttons) are built as one rigid group each, positioned and animated as a single unit -
        // previously the panel slid independently while its dial/buttons sat fixed at their
        // final docked position the whole time, which read as those pieces "floating" detached
        // from the panel during the slide instead of moving together with it.
        var dialLocalY = (int)(PanelHeight * JogDialFractionY);
        var buttonsLocalY = (int)(PanelHeight * ButtonClusterFractionY);
        var panelCenterLocalX = PanelWidth / 2;

        _greyGroup = new Container { X = GreyPanelClosedX, Y = PanelY };
        _greyGroup.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/PanelGrey", 0, false),
            Width = PanelWidth,
            Height = PanelHeight,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        _greyGroup.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/JogDial", 0, false),
            Width = JogDialSize,
            Height = JogDialSize,
            X = panelCenterLocalX,
            Y = dialLocalY,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        // D-pad drives tab navigation, not roster browsing - Up/Right move to the next tab
        // (Profile -> Characters -> Graveyard), Down/Left move to the previous one, per request.
        _greyGroup.AddChild(BuildButton("Console/DPadUp", panelCenterLocalX, buttonsLocalY - ButtonDiamondOffset, NextTab));
        _greyGroup.AddChild(BuildButton("Console/DPadDown", panelCenterLocalX, buttonsLocalY + ButtonDiamondOffset, PreviousTab));
        _greyGroup.AddChild(BuildButton("Console/DPadLeft", panelCenterLocalX - ButtonDiamondOffset, buttonsLocalY, PreviousTab));
        _greyGroup.AddChild(BuildButton("Console/DPadRight", panelCenterLocalX + ButtonDiamondOffset, buttonsLocalY, NextTab));
        AddChild(_greyGroup);

        _goldGroup = new Container { X = GoldPanelClosedX, Y = PanelY };
        _goldGroup.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/PanelGold", 0, false),
            Width = PanelWidth,
            Height = PanelHeight,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        _goldGroup.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("Console/JogDial", 0, false),
            Width = JogDialSize,
            Height = JogDialSize,
            X = panelCenterLocalX,
            Y = dialLocalY,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
        _goldGroup.AddChild(BuildButton("Console/ButtonX", panelCenterLocalX, buttonsLocalY - ButtonDiamondOffset, () => _onForge()));
        _goldGroup.AddChild(BuildButton("Console/ButtonA", panelCenterLocalX, buttonsLocalY + ButtonDiamondOffset, () => _onPlay()));
        _goldGroup.AddChild(BuildButton("Console/ButtonY", panelCenterLocalX - ButtonDiamondOffset, buttonsLocalY, () => { }));
        _goldGroup.AddChild(BuildButton("Console/ButtonB", panelCenterLocalX + ButtonDiamondOffset, buttonsLocalY, () => _onBack()));
        AddChild(_goldGroup);

        Alpha = 0f;
        AddEventListener(Event.EnterFrame, OnEntranceFrame);
    }

    public void SetCharacters(List<Character> characters, int selectIndex) {
        _characters = characters;
        _currentIndex = characters.Count == 0 ? -1 : Math.Clamp(selectIndex, 0, characters.Count - 1);
        BuildRosterGrid();
        RefreshDisplay();
    }

    public Character CurrentCharacter => _currentIndex >= 0 && _currentIndex < _characters.Count ? _characters[_currentIndex] : null;

    public void SetAccountInfo(int gold, int fame) {
        _goldAmountText.SetText(gold.ToString());
        _fameAmountText.SetText(fame.ToString());
        LayoutCurrencyDisplay();
    }

    // Right-aligns the gold/fame icon+amount groups against the content-safe area's corner - the
    // amount text's width varies with the number of digits, so (unlike the rest of this class's
    // fixed layout, see the "no content-driven sizing" convention for the outer frame/panels)
    // these two little groups DO need to measure their own text each time the numbers change,
    // same as the tab row already does for its own variable-width labels. Only the two groups'
    // internal layout depends on the measurement - the screen/panel/frame sizes around them stay
    // untouched.
    private void LayoutCurrencyDisplay() {
        // Amount text is anchored MiddleLeft (X = its own left edge); the coin icon is anchored
        // Middle (X = its own center) and sits to the icon's *left* of the text with a small gap.
        // Both groups are positioned right-to-left from the content-safe area's corner: fame
        // (rightmost) first, then gold to its left with CurrencyGroupGap between the two groups.
        var fameRightEdge = ContentAreaRight - CornerInset;
        _fameAmountText.X = fameRightEdge - _fameAmountText.Width;
        _fameAmountText.Y = ContentAreaTop;
        _fameCoinIcon.X = _fameAmountText.X - CurrencyIconTextGap - CoinIconSize / 2;
        _fameCoinIcon.Y = ContentAreaTop;

        var fameGroupLeft = _fameCoinIcon.X - CoinIconSize / 2;
        var goldRightEdge = fameGroupLeft - CurrencyGroupGap;
        _goldAmountText.X = goldRightEdge - _goldAmountText.Width;
        _goldAmountText.Y = ContentAreaTop;
        _goldCoinIcon.X = _goldAmountText.X - CurrencyIconTextGap - CoinIconSize / 2;
        _goldCoinIcon.Y = ContentAreaTop;
    }

    private void ShowProfileTab() => SetPage(ConsolePage.Profile);

    private void ShowCharactersTab() => SetPage(ConsolePage.Characters);

    private void ShowGraveyardTab() => SetPage(ConsolePage.Graveyard);

    // D-pad Up/Right and Down/Left respectively - see the D-pad wiring in the constructor.
    private void NextTab() {
        SetPage(_currentPage switch {
            ConsolePage.Profile => ConsolePage.Characters,
            ConsolePage.Characters => ConsolePage.Graveyard,
            _ => ConsolePage.Profile
        });
    }

    private void PreviousTab() {
        SetPage(_currentPage switch {
            ConsolePage.Profile => ConsolePage.Graveyard,
            ConsolePage.Characters => ConsolePage.Profile,
            _ => ConsolePage.Characters
        });
    }

    private void SetPage(ConsolePage page) {
        if (_currentPage == page) {
            return;
        }
        _currentPage = page;
        _profileTab.SetColor(page == ConsolePage.Profile ? TerminalGreen : TerminalDim);
        _charactersTab.SetColor(page == ConsolePage.Characters ? TerminalGreen : TerminalDim);
        _graveyardTab.SetColor(page == ConsolePage.Graveyard ? TerminalGreen : TerminalDim);
        RefreshDisplay();
    }

    private void RefreshDisplay() {
        // Every visibility flag below is also gated on _entranceComplete - see the constructor
        // comment above _profileTab. Character data/tab clicks can arrive at any point during the
        // entrance and still update _currentPage/_hasCharacter/the roster grid normally; they
        // just don't actually become visible until the console has finished opening.
        _profileTab.Visible = _entranceComplete;
        _charactersTab.Visible = _entranceComplete;
        _graveyardTab.Visible = _entranceComplete;
        var onProfilePage = _entranceComplete && _currentPage == ConsolePage.Profile;
        _goldCoinIcon.Visible = onProfilePage;
        _goldAmountText.Visible = onProfilePage;
        _fameCoinIcon.Visible = onProfilePage;
        _fameAmountText.Visible = onProfilePage;
        _graveyardText.Visible = _entranceComplete && _currentPage == ConsolePage.Graveyard;

        var onCharactersPage = _currentPage == ConsolePage.Characters;
        _hasCharacter = CurrentCharacter != null;

        _rosterContainer.Visible = _entranceComplete && onCharactersPage && _hasCharacter;
        _emptyText.Visible = _entranceComplete && onCharactersPage && !_hasCharacter;

        for (var i = 0; i < _rosterTileBackdrops.Count; i++) {
            _rosterTileBackdrops[i].SetColor(i == _currentIndex ? TileSelectedTint : TileUnselectedTint);
        }
    }

    // Rebuilt from scratch each time the character list changes (see SetCharacters) - laid out
    // left-to-right, top-to-bottom within the content-safe area (see its own const block above),
    // wrapping to a new row whenever the next tile would run past the area's right edge.
    private void BuildRosterGrid() {
        _rosterContainer.RemoveChildren();
        _rosterTileBackdrops.Clear();

        var columns = Math.Max(1, (ContentAreaRight - ContentAreaLeft + TileGap) / (TileWidth + TileGap));
        var gridWidth = columns * TileWidth + (columns - 1) * TileGap;
        var gridLeft = ScreenCenterX - gridWidth / 2;

        for (var i = 0; i < _characters.Count; i++) {
            var col = i % columns;
            var row = i / columns;
            var x = gridLeft + col * (TileWidth + TileGap);
            var y = ContentAreaTop + row * (TileHeight + TileGap);
            _rosterContainer.AddChild(BuildCharacterTile(_characters[i], i, x, y));
        }
    }

    private Sprite BuildCharacterTile(Character character, int index, int x, int y) {
        var tile = new Container { X = x, Y = y };
        tile.MouseEnabled = true;

        var backdrop = new ColorRect(new ColorRectConfig {
            Width = TileWidth,
            Height = TileHeight,
            Color = index == _currentIndex ? TileSelectedTint : TileUnselectedTint
        });
        tile.AddChild(backdrop);
        _rosterTileBackdrops.Add(backdrop);

        var props = ObjectLibrary.TypeToObjectProps[character.ObjectType];
        var textureData = ObjectLibrary.TypeToTextureData[character.ObjectType];
        var portrait = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(textureData.AnimatedTextures.FaceDown[0], TextureType.GameAtlas),
            Width = TilePortraitSize,
            Height = TilePortraitSize,
            X = TileWidth / 2,
            Y = TileHeight / 2 - 14,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        tile.AddChild(portrait);

        var name = new SimpleText(new TextConfig {
            Text = props.ObjectId.ToUpperInvariant(),
            FontSize = TileNameFontSize,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalGreen,
            OutlineColor = TextOutline,
            OutlineThickness = 2,
            MaxWidth = TileWidth - 10,
            X = TileWidth / 2,
            Y = TileHeight - 18,
            Anchor = UiAnchor.Middle
        });
        tile.AddChild(name);

        var leftDown = false;
        tile.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        tile.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                _currentIndex = index;
                RefreshDisplay();
            }
            leftDown = false;
        });

        return tile;
    }

    private SimpleText BuildTab(string text, Action onClicked) {
        var tab = new SimpleText(new TextConfig {
            Text = text,
            FontSize = TabFontSize,
            FontType = FontType.Bold,
            FontGroup = FontGroup.Occular,
            Color = TerminalDim,
            OutlineColor = TextOutline,
            OutlineThickness = 2,
            Y = TabY,
            Anchor = UiAnchor.Middle
        });
        tab.MouseEnabled = true;

        var leftDown = false;
        tab.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        tab.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                onClicked();
            }
            leftDown = false;
        });

        return tab;
    }

    private Sprite BuildButton(string iconLookup, int centerX, int centerY, Action onClicked) {
        var button = new Container();
        button.X = centerX;
        button.Y = centerY;
        button.SetAnchor(UiAnchor.Middle);
        button.MouseEnabled = true;

        var icon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas(iconLookup, 0, false),
            Width = ButtonWidth,
            Height = ButtonHeight,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        button.AddChild(icon);

        var leftDown = false;
        button.AddEventListener(MouseEvent.MouseOver, () => button.Scale = new Vector2(ButtonHoverScale));
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

    private void OnEntranceFrame() {
        _entranceElapsedMs += Stage.GameTime.ElapsedMs;

        if (_entranceElapsedMs <= InitialDelayMs) {
            // Phase 0: nothing on screen yet - the Milky Way background gets a moment to itself
            // right after this screen loads before the console shows up at all.
            Alpha = 0f;
            return;
        }

        var sinceDelay = _entranceElapsedMs - InitialDelayMs;

        if (sinceDelay <= FadeInMs) {
            // Phase 1: fade in while the panels sit closed, touching at the screen's center - a
            // full, deliberate fade, not a flash, so a shut console is clearly visible before
            // anything starts moving.
            Alpha = IdleAlpha * (float)(sinceDelay / FadeInMs);
            return;
        }

        Alpha = IdleAlpha;

        if (sinceDelay <= FadeInMs + HoldClosedMs) {
            // Phase 2: hold closed for a beat, fully faded in - the screen "warming up" moment.
            return;
        }

        // Phase 3: slide the panels apart to their docked positions while the screen grows from
        // nothing, centered, in lockstep with the same eased progress - the gap between the two
        // panels' facing inner edges is exactly ScreenWidth * eased at every moment (both panels
        // move by a linear function of eased, so the gap between them does too), so sizing the
        // screen to that same product keeps it exactly filling the gap the whole time instead of
        // just appearing once the panels finish sliding.
        _screen.Visible = true;

        var slideElapsed = sinceDelay - FadeInMs - HoldClosedMs;
        var t = Math.Clamp(slideElapsed / OpenSlideMs, 0.0, 1.0);
        var eased = 1.0 - Math.Pow(1.0 - t, 3.0);

        _greyGroup.X = (int)Lerp(GreyPanelClosedX, GreyPanelDockX, eased);
        _goldGroup.X = (int)Lerp(GoldPanelClosedX, GoldPanelDockX, eased);
        _screen.Resize(Math.Max(1, (int)(ScreenWidth * eased)), ScreenHeight);

        if (_entranceElapsedMs >= EntranceTotalMs) {
            _greyGroup.X = GreyPanelDockX;
            _goldGroup.X = GoldPanelDockX;
            _screen.Resize(ScreenWidth, ScreenHeight);
            _entranceComplete = true;
            RefreshDisplay();
            RemoveEventListener(Event.EnterFrame, OnEntranceFrame);
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
