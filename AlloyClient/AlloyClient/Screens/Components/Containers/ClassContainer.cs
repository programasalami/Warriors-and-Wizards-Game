using System;
using System.Collections.Generic;
using AlloyClient.Assets.Libraries;
using AlloyClient.Data;
using AlloyClient.Display;
using AlloyClient.Game;
using AlloyClient.Screens.Components.CharacterSelection;
using AlloyClient.Ui;
using AlloyClient.Ui.Components.Buttons;
using AlloyClient.Ui.Components.Panels;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Screens.Components.Containers;

// "The Forge" (2026-09-17, second pass) - the first pass reskinned the old plain-wood popup with
// gothic frame art but kept the same "two cards side by side" shape. This pass leans into a
// rival-crests presentation: a big divider rune circle spins behind the gap between the two
// (now much larger) ClassCards, spinning the opposite direction from each card's own compass, so
// the two hero crests read as facing off rather than just sitting in a row.
public class ClassContainer : Overlay {

    // Shrunk from an earlier 800x780 pass - at 780 tall this popup ran past the bottom of the
    // 1280x720 design canvas (Settings.DefaultScreenWidth/Height), so it visibly didn't fit on
    // screen. 690 plus the smaller ClassCard (see that file) keeps the whole popup within the
    // canvas with margin to spare.
    private const int FrameWidth = 760;
    private const int FrameHeight = 690;
    private const int FrameCutX = 44;
    private const int FrameCutY = 44;

    private const int CardGap = 40;
    private const int FooterGap = 30;

    private const int TitleBannerWidth = 360;
    private const int TitleBannerHeight = 48;
    private const int TitleBannerY = 36;

    private const int DividerCompassSize = 140;
    private const float DividerCompassSpinDegPerSec = -5f;

    private const int CloseButtonSize = 34;
    private const int CloseButtonMargin = 30;
    private const float CloseButtonHoverScale = 1.15f;

    private const int FooterButtonWidth = 170;
    private const int FooterButtonHeight = 44;

    private readonly List<ClassCard> _cards = [];
    private readonly ObjectRect _dividerCompass;
    private ushort _selectedType;
    private float _dividerRotationDeg;

    public ClassContainer() {
        SetAnchor(UiAnchor.Middle);

        var background = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkDwellersFrame,
            // Drawn bigger than the 30px the texture was registered at (see SliceLibrary) so the
            // ornate scrollwork actually reads at this frame's size instead of a faint smudge.
            CutX = FrameCutX,
            CutY = FrameCutY,
            Width = FrameWidth,
            Height = FrameHeight
        });
        AddChild(background);

        var titleBanner = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkDwellersHorizFrame,
            CutX = 20,
            CutY = 10,
            Width = TitleBannerWidth,
            Height = TitleBannerHeight,
            X = Width / 2 - TitleBannerWidth / 2,
            Y = TitleBannerY
        });
        AddChild(titleBanner);

        var title = new SimpleText(new TextConfig {
            Text = "The Forge",
            FontSize = 30,
            FontType = FontType.Bold,
            FontGroup = FontGroup.NotJamSignature21,
            Color = 0xFFFFFF,
            OutlineColor = 0x000000,
            OutlineThickness = 4,
            X = Width / 2,
            Y = TitleBannerY + TitleBannerHeight / 2,
            Anchor = UiAnchor.Middle
        });
        AddChild(title);

        var closeButton = BuildCloseButton();
        AddChild(closeButton);

        var cardsWidth = ClassCard.CardWidth * 2 + CardGap;
        var cardsLeft = Width / 2 - cardsWidth / 2;
        var cardsY = TitleBannerY + TitleBannerHeight + 28;

        // Sits behind both cards (added before the loop below), centered on the gap between
        // them at the same height as each card's own compass (ClassCard.PortraitCenterY), so
        // only a spinning ring peeks out between the two crests.
        _dividerCompass = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("DarkDwellers/Compass", 0, false),
            Width = DividerCompassSize,
            Height = DividerCompassSize,
            X = Width / 2,
            Y = cardsY + 104,
            Anchor = UiAnchor.Middle,
            Alpha = 0.7f,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_dividerCompass);
        AddEventListener(Event.EnterFrame, OnFrameEnter);

        var i = 0;
        foreach (var kvp in ObjectLibrary.TypeToClassProps) {
            var card = new ClassCard(i, kvp.Key, OnCardSelected) {
                X = cardsLeft + i * (ClassCard.CardWidth + CardGap),
                Y = cardsY
            };
            AddChild(card);
            _cards.Add(card);
            i++;
        }

        if (_cards.Count > 0) {
            OnCardSelected(_cards[0]);
        }

        var footerY = cardsY + ClassCard.CardHeight + FooterGap;

        var cancelButton = new GothicButton(new GothicButtonConfig {
            Text = "Cancel",
            FontSize = 24,
            Width = FooterButtonWidth,
            Height = FooterButtonHeight,
            OnClicked = CloseOverlay,
            X = cardsLeft + FooterButtonWidth / 2,
            Y = footerY,
            Anchor = UiAnchor.Middle
        });
        AddChild(cancelButton);

        var playButton = new GothicButton(new GothicButtonConfig {
            Text = "Play",
            FontSize = 24,
            Width = FooterButtonWidth,
            Height = FooterButtonHeight,
            OnClicked = OnPlayClicked,
            X = cardsLeft + cardsWidth - FooterButtonWidth / 2,
            Y = footerY,
            Anchor = UiAnchor.Middle
        });
        AddChild(playButton);
    }

    private Sprite BuildCloseButton() {
        var button = new Container();
        button.X = FrameWidth - CloseButtonMargin;
        button.Y = CloseButtonMargin;
        button.SetAnchor(UiAnchor.Middle);
        button.MouseEnabled = true;

        var icon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("DarkDwellers/CloseButton", 0, false),
            Width = CloseButtonSize,
            Height = CloseButtonSize,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        button.AddChild(icon);

        var leftDown = false;
        button.AddEventListener(MouseEvent.MouseOver, () => button.Scale = new Vector2(CloseButtonHoverScale));
        button.AddEventListener(MouseEvent.MouseOut, () => button.Scale = Vector2.One);
        button.AddEventListener(MouseEvent.LeftDown, () => leftDown = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (leftDown) {
                CloseOverlay();
            }
            leftDown = false;
        });

        return button;
    }

    private void OnCardSelected(ClassCard card) {
        _selectedType = card.Type;
        foreach (var c in _cards) {
            c.SetSelected(c == card);
        }
    }

    private void OnPlayClicked() {
        GlobalData.CharacterType = _selectedType;
        ScreenManager.FadeToScreen(new GameScreen(), Easing.SineInOut, 1000, 0x0);
        CloseOverlay();
    }

    private void OnFrameEnter() {
        _dividerRotationDeg += DividerCompassSpinDegPerSec * (float)(Stage.GameTime.ElapsedMs / 1000.0);
        _dividerCompass.Rotation = _dividerRotationDeg * (MathF.PI / 180f);
    }
}
