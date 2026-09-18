using System;
using AlloyClient.Assets.Libraries;
using AlloyClient.Game;
using AlloyClient.Ui;
using AlloyClient.Utils;
using Alloy.Engine;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using OpenTK.Mathematics;

namespace AlloyClient.Screens.Components.CharacterSelection;

// "The Forge" redesign (2026-09-17, second pass) - the first pass just reskinned the old plain-
// wood ClassCard with gothic frame art in the same layout. This pass restructures it into a big
// "hero crest": the class stands on a slowly-turning rune circle (the same DarkDwellers/Compass
// used by HeroStage on the character select screen, tying the two redesigned screens together as
// one visual language) instead of sitting behind a static ornate arch, and the card itself is
// substantially bigger so the crest reads as the centerpiece of the popup, not a dense info card.
public sealed class ClassCard : Container {

    // Shrunk from an earlier 340x520 pass - at that size two cards plus the popup's own title/
    // footer chrome no longer fit inside the 1280x720 design canvas (see ClassContainer), so the
    // bottom of the popup ran off the actual window. Card content below is laid out to fit this
    // smaller budget, not scaled proportionally from the old numbers.
    public const int CardWidth = 300;
    public const int CardHeight = 460;

    // Drawn bigger than the 30px the texture was registered at (see SliceLibrary) so the ornate
    // scrollwork actually reads at this card's size instead of a faint smudge in the corner.
    private const int FrameCutX = 36;
    private const int FrameCutY = 36;
    private const int GlowMargin = 14;

    private const int CompassSize = 150;
    private const float CompassSpinDegPerSec = 6f;
    private const int PortraitBackdropSize = 108;
    private const int PortraitSize = 90;
    private const int PortraitCenterY = 104;

    private const float NameFontSize = 25f;
    private const float DescriptionFontSize = 14f;
    private const float StatLabelFontSize = 13f;
    private const float StatValueFontSize = 12f;
    private const float GearLabelFontSize = 13f;

    private const uint TextColor = 0xFFFFFF;
    private const uint DescriptionColor = 0xE4D9BE;
    private const uint StatLabelColor = 0xCBB7F0;
    private const uint OutlineColor = 0x000000;
    private const float OutlineThickness = 3f;

    private const int BarWidth = 148;
    private const int BarHeight = 13;
    private const int BarLabelWidth = 32;
    private const int BarRowGap = 6;
    private const int NumBars = 4;

    private const int GearSlotSize = 34;
    private const int GearIconSize = 26;
    private const int GearSlotGap = 8;
    private const int NumGearSlots = 4;

    // Warm gold brighten on the frame when this card is the selected one - matches
    // HomeButtonHoverColor's warm-gold family used for hover/selected states elsewhere (TitleScreen
    // rows, BookOverlay hub cards) rather than inventing a new accent color. Selection used to also
    // scale the whole card up (1.03x) - dropped that: the scale plus the glow ring together pushed
    // a selected card's true bounds past the footer buttons below it, since ClassContainer lays
    // those out from the card's *unscaled* height. Tint + glow ring alone still reads clearly as
    // "selected" without changing the card's footprint.
    private static readonly ColorTransform SelectedTint = new(1.25f, 1.12f, 0.85f, 1f);
    private static readonly ColorTransform UnselectedTint = new(1f, 1f, 1f, 1f);
    private const float UnselectedAlpha = 0.82f;

    public readonly ushort Type;
    private readonly int _characterIndex;
    private readonly Action<ClassCard> _onSelected;

    private readonly NineSliceRect _glow;
    private readonly NineSliceRect _background;
    private readonly ObjectRect _compass;
    private readonly ObjectRect _portrait;

    private float _compassRotationDeg;

    public ClassCard(int characterIndex, ushort type, Action<ClassCard> onSelected) : base(new ContainerConfig { Width = CardWidth, Height = CardHeight }) {
        _characterIndex = characterIndex;
        Type = type;
        _onSelected = onSelected;

        MouseEnabled = true;

        // Sits behind _background (added first) at a slightly larger size, so only its border
        // peeks out around the main frame's edge when selected - a cheap "glow ring" without a
        // second texture or a shader effect.
        _glow = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkDwellersFrameAccent,
            CutX = FrameCutX,
            CutY = FrameCutY,
            Width = CardWidth + GlowMargin * 2,
            Height = CardHeight + GlowMargin * 2,
            X = -GlowMargin,
            Y = -GlowMargin
        });
        AddChild(_glow);

        _background = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkDwellersFrame,
            CutX = FrameCutX,
            CutY = FrameCutY,
            Width = CardWidth,
            Height = CardHeight
        });
        AddChild(_background);

        _compass = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("DarkDwellers/Compass", 0, false),
            Width = CompassSize,
            Height = CompassSize,
            X = CardWidth / 2,
            Y = PortraitCenterY,
            Anchor = UiAnchor.Middle,
            Alpha = 0.85f,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_compass);

        var backdrop = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("BlackCircle"),
            Width = PortraitBackdropSize,
            Height = PortraitBackdropSize,
            X = CardWidth / 2,
            Y = PortraitCenterY,
            Anchor = UiAnchor.Middle,
            Alpha = 0.4f,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        backdrop.ColorTransformation = new ColorTransform(0.65f, 0.45f, 0.95f, 1f);
        AddChild(backdrop);

        var frames = Main.Atlas.GetAnimationAtlasData("players", characterIndex);
        _portrait = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(frames.FaceDown[0], TextureType.GameAtlas),
            Width = PortraitSize,
            Height = PortraitSize,
            X = CardWidth / 2,
            Y = PortraitCenterY,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_portrait);

        var props = ObjectLibrary.TypeToObjectProps[type];

        var nameText = new SimpleText(new TextConfig {
            Text = props.ObjectId,
            FontSize = NameFontSize,
            FontType = FontType.Bold,
            FontGroup = FontGroup.NotJamSignature21,
            Color = TextColor,
            OutlineColor = OutlineColor,
            OutlineThickness = OutlineThickness,
            X = CardWidth / 2,
            Y = PortraitCenterY + CompassSize / 2 + 22,
            Anchor = UiAnchor.Middle
        });
        AddChild(nameText);

        var description = new SimpleText(new TextConfig {
            Text = props.Description,
            FontSize = DescriptionFontSize,
            FontType = FontType.Bold,
            Color = DescriptionColor,
            MaxWidth = CardWidth - 56,
            X = CardWidth / 2,
            Y = nameText.Y + nameText.Height / 2 + 14,
            Anchor = UiAnchor.MiddleTop
        });
        AddChild(description);

        var playerProps = props.PlayerProperties;
        var barsTop = (int)(description.Y + description.Height + 22);
        BuildStatBar(barsTop + 0 * (BarHeight + BarRowGap), "HP", playerProps.Hp, playerProps.MaxHp);
        BuildStatBar(barsTop + 1 * (BarHeight + BarRowGap), "MP", playerProps.Mp, playerProps.MaxMp);
        BuildStatBar(barsTop + 2 * (BarHeight + BarRowGap), "ATK", playerProps.Attack, playerProps.MaxAttack);
        BuildStatBar(barsTop + 3 * (BarHeight + BarRowGap), "DEF", playerProps.Defense, playerProps.MaxDefense);

        var gearLabelY = barsTop + NumBars * (BarHeight + BarRowGap) + 16;
        var gearLabel = new SimpleText(new TextConfig {
            Text = "Starting Gear",
            FontSize = GearLabelFontSize,
            FontType = FontType.Bold,
            Color = StatLabelColor,
            OutlineColor = OutlineColor,
            OutlineThickness = 2,
            X = CardWidth / 2,
            Y = gearLabelY,
            Anchor = UiAnchor.Middle
        });
        AddChild(gearLabel);

        var gearRowWidth = NumGearSlots * GearSlotSize + (NumGearSlots - 1) * GearSlotGap;
        var gearLeft = CardWidth / 2 - gearRowWidth / 2;
        var gearY = gearLabelY + gearLabel.Height / 2 + 14;
        for (var i = 0; i < NumGearSlots; i++) {
            BuildGearSlot(gearLeft + i * (GearSlotSize + GearSlotGap), gearY, props.Equipment[i] ?? 0);
        }

        AddEventListener(MouseEvent.LeftDown, () => _onSelected(this));
        AddEventListener(Event.EnterFrame, OnFrameEnter);

        SetSelected(false);
    }

    private void BuildStatBar(int y, string label, int value, int max) {
        var labelText = new SimpleText(new TextConfig {
            Text = label,
            FontSize = StatLabelFontSize,
            FontType = FontType.Bold,
            Color = StatLabelColor,
            OutlineColor = OutlineColor,
            OutlineThickness = 2,
            X = CardWidth / 2 - BarWidth / 2 - BarLabelWidth,
            Y = y + BarHeight / 2,
            Anchor = UiAnchor.MiddleLeft
        });
        AddChild(labelText);

        var barLeft = CardWidth / 2 - BarWidth / 2;
        var track = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("DarkDwellers/BarTrack", 0, false),
            Width = BarWidth,
            Height = BarHeight,
            X = barLeft,
            Y = y,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(track);

        var fraction = max > 0 ? Math.Clamp(value / (float)max, 0f, 1f) : 0f;
        var fillWidth = (int)(BarWidth * fraction);
        if (fillWidth > 0) {
            var fillClip = new Container(new ContainerConfig { X = barLeft, Y = y, Width = fillWidth, Height = BarHeight, EnableClip = true });
            var fill = new ObjectRect(new ObjectRectConfig {
                Texture = TextureHelper.FromUiAtlas("DarkDwellers/BarFill", 0, false),
                Width = BarWidth,
                Height = BarHeight,
                OutlineEnabled = false,
                GlowEnabled = false
            });
            fillClip.AddChild(fill);
            AddChild(fillClip);
        }

        var valueText = new SimpleText(new TextConfig {
            Text = value.ToString(),
            FontSize = StatValueFontSize,
            FontType = FontType.Bold,
            Color = TextColor,
            OutlineColor = OutlineColor,
            OutlineThickness = 2,
            X = barLeft + BarWidth / 2,
            Y = y + BarHeight / 2,
            Anchor = UiAnchor.Middle
        });
        AddChild(valueText);
    }

    private void BuildGearSlot(int x, int y, ushort itemType) {
        var slotBg = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromUiAtlas("DarkDwellers/ItemSlot", 0, false),
            Width = GearSlotSize,
            Height = GearSlotSize,
            X = x,
            Y = y,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(slotBg);

        if (itemType == 0) {
            return;
        }

        var icon = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromGameAtlas(itemType),
            Width = GearIconSize,
            Height = GearIconSize,
            X = x + GearSlotSize / 2,
            Y = y + GearSlotSize / 2,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(icon);
    }

    public void SetSelected(bool selected) {
        _background.ColorTransformation = selected ? SelectedTint : UnselectedTint;
        _glow.Visible = selected;
        Alpha = selected ? 1f : UnselectedAlpha;
    }

    // Same little idle "walk in place" jitter the old card used - cycles between the two non-idle
    // walk frames so the portrait doesn't just sit dead still. The compass spins continuously
    // underneath, independent of selection - same always-on pedestal HeroStage uses.
    private void OnFrameEnter() {
        _compassRotationDeg += CompassSpinDegPerSec * (float)(Stage.GameTime.ElapsedMs / 1000.0);
        _compass.Rotation = _compassRotationDeg * (MathF.PI / 180f);

        const int frameDurationMs = 250;
        var frames = Main.Atlas.GetAnimationAtlasData("players", _characterIndex);
        var totalMs = (int)Stage.GameTime.TotalMs;
        var frameIndex = 1 + totalMs / frameDurationMs % 2;
        _portrait.ChangeTexture(TextureHelper.Create(frames.FaceDown[frameIndex], TextureType.GameAtlas));
    }
}
