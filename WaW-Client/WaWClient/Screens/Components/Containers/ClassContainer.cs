using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using WaWClient.Assets.Libraries;
using WaWClient.Data;
using WaWClient.Display;
using WaWClient.Game;
using WaWClient.Ui;
using WaWClient.Ui.Components.Panels;
using WaWClient.Utils;
using WaW.Engine;
using WaW.UiLib;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Data;
using WaW.UiLib.Extra;
using OpenTK.Mathematics;

namespace WaWClient.Screens.Components.Containers;

// The character creation popup: no frame and no header, just the dimmed screen with one tall card per class side by side
// (Wizard, Warrior) and two floating buttons under them. Each card stacks everything about its class in one column -
// portrait, class name, description, starting stats, starting gear. Click a card to select it (it gets a gold rim); CREATE
// makes whichever card is selected. Cards, gear slots and the description box are cut from the "Dark Ages UI" pack
// (recoloured walnut); the buttons are its parchment scroll. Replaces every earlier version (the gothic "Dark Dwellers"
// popup, the book-on-a-book, the wood/gold/black-and-red frames and the two-panel layout).
public class ClassContainer : Overlay {

    private const int CardWidth = 400;
    private const int CardHeight = 540;
    private const int CardGap = 40;
    private const int CardCut = 16;
    private const int CardPadding = 22;

    private const uint Gold = 0xDCC47C;         // the pack's "honey gold"
    private const uint Cream = 0xF6E7C6;
    private const uint Tan = 0xD9BE8F;
    private const uint ParchmentInk = 0x2A1C14;
    private const uint OutlineDark = 0x120A05;
    private const uint BarTrack = 0x1F130B;
    private const uint BarBorder = 0x9C7246;    // the same tan-brown as the cards' borders

    // Stat bar colours: crimson, the pack's powder blue, its camel gold, and a green - the same hues as its bar strips.
    private const uint HpColor = 0xB8324A;
    private const uint MpColor = 0x406C91;
    private const uint AttackColor = 0xC19149;
    private const uint DefenseColor = 0x3C9A5F;

    // Multiplied over a piece: lighter while the mouse is over an unselected card, and lifted for the selected one.
    private static readonly ColorTransform NormalTint = new(1f, 1f, 1f, 1f);
    private static readonly ColorTransform HoverTint = new(1.3f, 1.27f, 1.2f, 1f);
    private static readonly ColorTransform SelectedTint = new(1.5f, 1.45f, 1.3f, 1f);
    private static readonly ColorTransform ParchmentHoverTint = new(0.86f, 0.8f, 0.7f, 1f);
    private const int SelectionBorder = 3;

    // Everything on a card, top to bottom - offsets from the card's top edge (portrait, name and stat rows are centre lines).
    private const int PortraitSize = 150;
    private const int PortraitY = 96;
    private const int NameY = 196;
    private const int DescriptionTop = 222;
    private const int DescriptionHeight = 96;
    private const int DescriptionPadding = 14;
    private const float DescriptionSize = 18f;
    private const int StatsY = 346;
    private const int StatRowHeight = 27;
    private const int StatBarWidth = 220;
    private const int StatBarHeight = 12;
    private const int GearLabelY = 466;
    private const int GearY = 500;
    private const int GearSlotSize = 44;
    private const int GearIconSize = 32;
    private const int GearSlotGap = 12;
    private const int GearSlots = 4;

    // The floating buttons under the cards.
    private const int ButtonWidth = 190;
    private const int ButtonHeight = 50;
    private const int ButtonCut = 12;
    private const int ButtonGap = 40;
    private const int ButtonsTop = CardHeight + 26;

    private sealed class ClassCard {
        public readonly Container Root = new();
        public NineSliceRect Board;
        public Container Border;
        public ObjectRect Portrait;
        public int Index;
    }

    private readonly List<ushort> _types = [];
    private readonly List<ClassCard> _cards = [];
    private int _selected;

    public ClassContainer() {
        SetAnchor(UiAnchor.Middle);

        foreach (var type in ObjectLibrary.TypeToClassProps.Keys) {
            _types.Add(type);
        }

        for (var i = 0; i < _types.Count; i++) {
            var card = BuildCard(i);
            card.Root.X = i * (CardWidth + CardGap);
            AddChild(card.Root);
            _cards.Add(card);
        }
        SelectCard(0);

        var count = Math.Max(_types.Count, 1);
        var totalWidth = count * CardWidth + (count - 1) * CardGap;
        var buttonsLeft = (totalWidth - (2 * ButtonWidth + ButtonGap)) / 2;
        AddChild(BuildButton("CANCEL", buttonsLeft, CloseOverlay));
        AddChild(BuildButton("CREATE", buttonsLeft + ButtonWidth + ButtonGap, OnCreateClicked));

        AddEventListener(Event.EnterFrame, OnFrameEnter);
    }

    private ClassCard BuildCard(int index) {
        var props = ObjectLibrary.TypeToObjectProps[_types[index]];
        var card = new ClassCard { Index = index };
        var root = card.Root;
        var centerX = CardWidth / 2;

        root.MouseEnabled = true;

        card.Board = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesPanel,
            CutX = CardCut,
            CutY = CardCut,
            Width = CardWidth,
            Height = CardHeight
        });
        root.AddChild(card.Board);

        // A gold rim shown only while this card is the selected one.
        card.Border = new Container();
        card.Border.AddChild(new ColorRect(new ColorRectConfig { Width = CardWidth, Height = SelectionBorder, Color = Gold }));
        card.Border.AddChild(new ColorRect(new ColorRectConfig { Width = CardWidth, Height = SelectionBorder, Color = Gold }) { Y = CardHeight - SelectionBorder });
        card.Border.AddChild(new ColorRect(new ColorRectConfig { Width = SelectionBorder, Height = CardHeight, Color = Gold }));
        card.Border.AddChild(new ColorRect(new ColorRectConfig { Width = SelectionBorder, Height = CardHeight, Color = Gold }) { X = CardWidth - SelectionBorder });
        root.AddChild(card.Border);

        // Portrait, then the name under it.
        card.Portrait = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(Main.Atlas.GetAnimationAtlasData("players", index).FaceDown[0], TextureType.GameAtlas),
            X = centerX,
            Y = PortraitY,
            Width = PortraitSize,
            Height = PortraitSize,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        root.AddChild(card.Portrait);

        root.AddChild(Label(props.ObjectId.ToUpperInvariant(), FontGroup.MyriadPro, 36f, centerX, NameY, UiAnchor.Middle, Gold, CardWidth - 2 * CardPadding));

        // The description in a lighter inset box, wrapped and centred both ways.
        var descriptionWidth = CardWidth - 2 * CardPadding;
        root.AddChild(new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesSlot,
            CutX = CardCut,
            CutY = CardCut,
            Width = descriptionWidth,
            Height = DescriptionHeight,
            X = CardPadding,
            Y = DescriptionTop
        }));
        AddCenteredParagraph(root, props.Description, FontGroup.MyriadPro, DescriptionSize, centerX, DescriptionTop, DescriptionHeight, descriptionWidth - 2 * DescriptionPadding, Cream, 1);

        // Starting stats.
        var stats = props.PlayerProperties;
        BuildStatRow(root, 0, "HP", stats.Hp, stats.MaxHp, HpColor);
        BuildStatRow(root, 1, "MP", stats.Mp, stats.MaxMp, MpColor);
        BuildStatRow(root, 2, "ATT", stats.Attack, stats.MaxAttack, AttackColor);
        BuildStatRow(root, 3, "DEF", stats.Defense, stats.MaxDefense, DefenseColor);

        // Starting gear.
        root.AddChild(Label("STARTING GEAR", FontGroup.MyriadPro, 14f, centerX, GearLabelY, UiAnchor.Middle, Tan, outline: 1));
        var gearWidth = GearSlots * GearSlotSize + (GearSlots - 1) * GearSlotGap;
        var gearLeft = centerX - gearWidth / 2;
        for (var i = 0; i < GearSlots; i++) {
            BuildGearSlot(root, gearLeft + i * (GearSlotSize + GearSlotGap), GearY, props.Equipment[i] ?? 0);
        }

        var down = false;
        root.AddEventListener(MouseEvent.MouseOver, () => { if (index != _selected) card.Board.ColorTransformation = HoverTint; });
        root.AddEventListener(MouseEvent.MouseOut, () => { if (index != _selected) card.Board.ColorTransformation = NormalTint; });
        root.AddEventListener(MouseEvent.LeftDown, () => down = true);
        root.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                SelectCard(index);
            }
            down = false;
        });

        return card;
    }

    private void SelectCard(int index) {
        _selected = index;
        foreach (var card in _cards) {
            var selected = card.Index == index;
            card.Board.ColorTransformation = selected ? SelectedTint : NormalTint;
            card.Border.Visible = selected;
        }
    }

    // "HP  [=====-----]  100": label on the left, value on the right, and a bar between showing the starting value
    // against the class's maximum.
    private static void BuildStatRow(Container card, int row, string label, int value, int max, uint color) {
        var centerY = StatsY + row * StatRowHeight;
        var left = CardPadding + 12;
        var right = CardWidth - CardPadding - 12;
        var barLeft = left + 56;

        card.AddChild(Label(label, FontGroup.MyriadPro, 19f, left, centerY, UiAnchor.MiddleLeft, Tan, outline: 1, type: FontType.Normal));

        // A tan border, then the dark track, then the coloured fill.
        card.AddChild(new ColorRect(new ColorRectConfig { Width = StatBarWidth + 4, Height = StatBarHeight + 4, Color = BarBorder }) { X = barLeft - 2, Y = centerY - StatBarHeight / 2 - 2 });
        card.AddChild(new ColorRect(new ColorRectConfig { Width = StatBarWidth, Height = StatBarHeight, Color = BarTrack }) { X = barLeft, Y = centerY - StatBarHeight / 2 });
        var fill = max > 0 ? (int) (StatBarWidth * Math.Clamp(value / (float) max, 0f, 1f)) : 0;
        if (fill > 0) {
            card.AddChild(new ColorRect(new ColorRectConfig { Width = fill, Height = StatBarHeight, Color = color }) { X = barLeft, Y = centerY - StatBarHeight / 2 });
        }

        card.AddChild(Label(value.ToString(), FontGroup.MyriadPro, 18f, right, centerY, UiAnchor.MiddleRight, Cream, outline: 1));
    }

    private static void BuildGearSlot(Container card, int x, int centerY, ushort itemType) {
        card.AddChild(new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesSlot,
            CutX = 12,
            CutY = 12,
            Width = GearSlotSize,
            Height = GearSlotSize,
            X = x,
            Y = centerY - GearSlotSize / 2
        }));

        if (itemType == 0) {
            return;
        }

        card.AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromGameAtlas(itemType),
            X = x + GearSlotSize / 2,
            Y = centerY,
            Width = GearIconSize,
            Height = GearIconSize,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        }));
    }

    // A parchment scroll with the label in dark ink. It darkens a little while the mouse is over it.
    private static Container BuildButton(string text, int x, Action onClicked) {
        var button = new Container { X = x, Y = ButtonsTop };
        button.MouseEnabled = true;

        var plate = new NineSliceRect(new NineSliceConfig {
            SliceData = SliceLibrary.DarkAgesParchment,
            CutX = ButtonCut,
            CutY = ButtonCut,
            Width = ButtonWidth,
            Height = ButtonHeight
        });
        plate.ColorTransformation = NormalTint;
        button.AddChild(plate);
        button.AddChild(Label(text, FontGroup.MyriadPro, 26f, ButtonWidth / 2, ButtonHeight / 2, UiAnchor.Middle, ParchmentInk, outline: 1));

        var down = false;
        button.AddEventListener(MouseEvent.MouseOver, () => plate.ColorTransformation = ParchmentHoverTint);
        button.AddEventListener(MouseEvent.MouseOut, () => plate.ColorTransformation = NormalTint);
        button.AddEventListener(MouseEvent.LeftDown, () => down = true);
        button.AddEventListener(MouseEvent.LeftUp, () => {
            if (down) {
                onClicked();
            }
            down = false;
        });
        return button;
    }

    // A wrapped paragraph centred in a box: every line is its own text sitting on the box's centre line, and the block of lines
    // is centred vertically too. (One SimpleText with a max width wraps but left-aligns its lines and anchors by its top, which
    // read as off-centre and hugging the bottom of the box.) Line breaks and the indentation in the XML are collapsed first.
    private static void AddCenteredParagraph(Container target, string text, FontGroup font, float size, int centerX, int boxTop, int boxHeight, int maxWidth, uint color, int outline) {
        var metrics = UiRender.GetFont(font, FontType.Bold);
        var words = Regex.Replace(text ?? "", @"\s+", " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words) {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && MeasureWidth(metrics, candidate, size) > maxWidth) {
                lines.Add(line.ToString());
                line.Clear();
                candidate = word;
            }
            line.Clear();
            line.Append(candidate);
        }
        if (line.Length > 0) {
            lines.Add(line.ToString());
        }

        var lineHeight = metrics.LineHeight * size;
        var y = boxTop + (boxHeight - lines.Count * lineHeight) / 2f;
        foreach (var l in lines) {
            target.AddChild(Label(l, font, size, centerX, (int) MathF.Round(y + lineHeight / 2f), UiAnchor.Middle, color, 0, outline));
            y += lineHeight;
        }
    }

    // Width of a string in the text control's own units (the same advance + kerning sum its layout uses).
    private static float MeasureWidth(BitmapFont font, string text, float size) {
        var width = 0f;
        for (var i = 0; i < text.Length; i++) {
            if (!font.Glyphs.TryGetValue(text[i], out var glyph)) {
                continue;
            }

            if (i < text.Length - 1 && font.Kernings.TryGetValue((text[i], text[i + 1]), out var kern)) {
                width += kern * size;
            }
            width += glyph.Advance * size;
        }
        return width;
    }

    // Light text with a dark outline - reads on the dark cards.
    private static SimpleText Label(string text, FontGroup font, float size, int x, int y, UiAnchor anchor, uint color = Cream, int maxWidth = 0, int outline = 2, FontType type = FontType.Bold) {
        var config = new TextConfig {
            Text = text,
            FontSize = size,
            FontType = type,
            FontGroup = font,
            Color = color,
            OutlineColor = OutlineDark,
            OutlineThickness = outline,
            X = x,
            Y = y,
            Anchor = anchor
        };
        if (maxWidth > 0) {
            config.MaxWidth = maxWidth;
        }
        return new SimpleText(config);
    }

    private void OnCreateClicked() {
        if (_types.Count == 0) {
            return;
        }

        GlobalData.CharacterType = _types[_selected];
        ScreenManager.FadeToScreen(new GameScreen(), Easing.SineInOut, 1000, 0x0);
        CloseOverlay();
    }

    // The same little "walking in place" idle the class cards always had: cycles each portrait between the two non-idle walk
    // frames so nobody sits dead still.
    private void OnFrameEnter() {
        const int frameDurationMs = 250;
        var frameIndex = 1 + (int) Stage.GameTime.TotalMs / frameDurationMs % 2;
        foreach (var card in _cards) {
            var frames = Main.Atlas.GetAnimationAtlasData("players", card.Index);
            card.Portrait.ChangeTexture(TextureHelper.Create(frames.FaceDown[frameIndex], TextureType.GameAtlas));
        }
    }
}
