using System;
using System.Collections.Generic;
using System.Globalization;
using AlloyClient.Assets.Libraries;
using AlloyClient.Assets.XmlStructs;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Screens.Components.Portal;

// The wiki pages (items, classes) and the graveyard. Items and classes come straight from the client's own XML (ObjectLibrary), so this is
// always exactly what the running build has - the website's data files are generated from the same XML.
public sealed partial class PortalView {

    private int _itemSlotFilter = -1;      // -1 = every slot

    private const int CardCols = 4;
    private const int CardRows = 3;
    private const int CardW = 272;
    private const int CardH = 128;
    private const int CardGap = 18;

    #region Items

    private static List<ItemDesc> AllItems() {
        var list = new List<ItemDesc>();
        foreach (var item in ObjectLibrary.TypeToItem.Values) {
            if (item != null && !string.IsNullOrEmpty(item.ObjectId)) {
                list.Add(item);
            }
        }

        list.Sort((a, b) => {
            var c = a.SlotType.CompareTo(b.SlotType);
            if (c == 0) c = a.Tier.CompareTo(b.Tier);
            return c != 0 ? c : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        });
        return list;
    }

    private void BuildItemsPage() {
        var all = AllItems();
        PageTitle("ITEMS", $"{all.Count} items in this build");

        // Slot filter chips.
        var slots = new List<int>();
        foreach (var item in all) {
            if (!slots.Contains(item.SlotType)) {
                slots.Add(item.SlotType);
            }
        }

        var chipY = ContentTop + 66;
        var x = ContentLeft;
        var chips = new List<(int Slot, string Label)> { (-1, "ALL") };
        foreach (var s in slots) {
            chips.Add((s, SlotName(s).ToUpperInvariant()));
        }

        foreach (var (slot, label) in chips) {
            var w = MeasureWidth(label, SmallSize - 2) + 20;
            var current = slot == _itemSlotFilter;
            var chip = BuildRowButton(x, chipY - 14, w, 28, () => { _itemSlotFilter = slot; _pageIndex = 0; Render(); }, current ? 0.3f : 0.08f);
            chip.AddChild(Text(label, SmallSize - 2, w / 2, 14, UiAnchor.Middle, color: current ? Hover : Ink, outline: 1));
            _content.AddChild(chip);
            x += w + 8;
        }

        var shown = _itemSlotFilter < 0 ? all : all.FindAll(i => i.SlotType == _itemSlotFilter);
        if (shown.Count == 0) {
            Notice("Nothing here.");
            return;
        }

        var gridTop = chipY + 30;
        var (first, count) = Pager(shown.Count, CardCols * CardRows);
        for (var i = 0; i < count; i++) {
            var item = shown[first + i];
            var cx = ContentLeft + (i % CardCols) * (CardW + CardGap);
            var cy = gridTop + (i / CardCols) * (CardH + CardGap);
            _content.AddChild(ItemCard(item, cx, cy));
        }
    }

    private Container ItemCard(ItemDesc item, int x, int y) {
        var type = item.ObjectType;
        var card = BuildRowButton(x, y, CardW, CardH, () => Go(Page.Item, type.ToString()), 0.08f);
        card.AddChild(ItemIcon(type, 40, CardH / 2, 56));
        card.AddChild(Text(item.DisplayName, SmallSize + 1, 80, 22, UiAnchor.MiddleLeft, maxWidth: CardW - 88, outline: 1));
        card.AddChild(Text(TierLabel(item) + SlotName(item.SlotType), SmallSize - 3, 80, 48, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        var line = DamageLine(item) ?? BoostLine(item) ?? (item.TypeOfConsumable ? "Consumable" : "");
        card.AddChild(Text(line, SmallSize - 3, 80, 72, UiAnchor.MiddleLeft, color: InkSoft, outline: 1, maxWidth: CardW - 88));
        return card;
    }

    private static string TierLabel(ItemDesc item) => item.Tier > 0 || HasTier(item) ? $"T{item.Tier}  -  " : "";

    // Untiered items (potions) have no <Tier>; ItemDesc leaves them at 0, the same as a real T0. Weapons and gear always have a slot type.
    private static bool HasTier(ItemDesc item) => item.SlotType != 0 && !item.TypeOfConsumable;

    private static string DamageLine(ItemDesc item) {
        var p = item.Projectiles;
        if (p == null) {
            return null;
        }

        var line = $"{p.MinDamage}-{p.MaxDamage} damage";
        if (item.NumProjectiles > 1) {
            line += $" x {item.NumProjectiles}";
        }

        if (Math.Abs(item.RateOfFire - 1f) > 0.001f) {
            line += $", {Math.Round(item.RateOfFire * 100)}% rate of fire";
        }

        return line;
    }

    private static string BoostLine(ItemDesc item) {
        if (item.StatBoosts == null || item.StatBoosts.Length == 0) {
            return null;
        }

        var parts = new List<string>();
        foreach (var b in item.StatBoosts) {
            parts.Add((b.Amount >= 0 ? "+" : "") + b.Amount + " " + (StatNames.TryGetValue(b.Stat, out var n) ? n : "stat " + b.Stat));
        }

        return string.Join(", ", parts);
    }

    private void BuildItemPage(string arg) {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type) || !ObjectLibrary.TypeToItem.TryGetValue((ushort) type, out var item) || item == null) {
            PageTitle("ITEM");
            Notice("No such item.");
            return;
        }

        PageTitle(item.DisplayName.ToUpperInvariant() + (HasTier(item) ? $"   -   TIER {item.Tier}" : ""));
        _content.AddChild(BuildTextButton("< ALL ITEMS", SmallSize - 2, ContentRight, ContentTop + 12, UiAnchor.RightTop, () => Go(Page.Items), InkSoft));

        var top = ContentTop + 70;
        _content.AddChild(Box(ContentLeft, top, 160, 160, 0.1f));
        _content.AddChild(ItemIcon(type, ContentLeft + 80, top + 80, 128));

        var left = ContentLeft + 190;
        var width = ContentW - 190;
        _content.AddChild(Text(item.Description ?? "", BodySize - 2, left, top, UiAnchor.LeftTop, maxWidth: width, outline: 1));

        var y = top + 110;
        const int step = 30;
        var facts = new List<(string Label, string Value)> { ("SLOT", SlotName(item.SlotType)) };
        if (DamageLine(item) != null) {
            facts.Add(("DAMAGE", DamageLine(item)));
        }

        if (BoostLine(item) != null) {
            facts.Add(("STAT BONUS", BoostLine(item)));
        }

        if (item.TypeOfConsumable) {
            facts.Add(("TYPE", "Consumable"));
        }

        if (item.Soulbound) {
            facts.Add(("SOULBOUND", "Yes"));
        }

        if (item.FameBonus > 0) {
            facts.Add(("FAME BONUS", item.FameBonus + "%"));
        }

        facts.Add(("ID", $"0x{type:x4}"));

        foreach (var (label, value) in facts) {
            _content.AddChild(Text(label, SmallSize - 3, left, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
            _content.AddChild(Text(value, SmallSize + 1, left + 150, y, UiAnchor.MiddleLeft, outline: 1, maxWidth: width - 150));
            y += step;
        }

        // Who can use it: every class whose slot types include this one (a slot type of 0 fits everyone).
        _content.AddChild(Text("USED BY", SmallSize - 3, left, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        var users = new List<ObjectProperties>();
        foreach (var props in ObjectLibrary.TypeToObjectProps.Values) {
            if (props.IsPlayer && props.SlotTypes != null && props.SlotTypes.Contains(item.SlotType)) {
                users.Add(props);
            }
        }

        if (item.SlotType == 0 || item.TypeOfConsumable) {
            _content.AddChild(Text("every class", SmallSize + 1, left + 150, y, UiAnchor.MiddleLeft, outline: 1));
        } else if (users.Count == 0) {
            _content.AddChild(Text("no class", SmallSize + 1, left + 150, y, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        } else {
            var x = left + 150;
            foreach (var props in users) {
                var classType = props.ObjectType;
                _content.AddChild(BuildTextButton(props.ObjectId, SmallSize + 1, x, y, UiAnchor.MiddleLeft, () => Go(Page.Class, classType.ToString())));
                x += MeasureWidth(props.ObjectId, SmallSize + 1) + 24;
            }
        }
    }

    #endregion

    #region Classes

    private static List<ObjectProperties> AllClasses() {
        var list = new List<ObjectProperties>();
        foreach (var props in ObjectLibrary.TypeToObjectProps.Values) {
            if (props.IsPlayer && props.PlayerProperties != null) {
                list.Add(props);
            }
        }

        list.Sort((a, b) => a.ObjectType.CompareTo(b.ObjectType));
        return list;
    }

    private void BuildClassesPage() {
        var classes = AllClasses();
        PageTitle("CLASSES", $"{classes.Count} classes in this build");

        const int cardW = 560;
        const int cardH = 150;
        var top = ContentTop + 66;
        var (first, count) = Pager(classes.Count, 6);
        for (var i = 0; i < count; i++) {
            var props = classes[first + i];
            var x = ContentLeft + (i % 2) * (cardW + CardGap + 6);
            var y = top + (i / 2) * (cardH + CardGap);
            var classType = props.ObjectType;
            var card = BuildRowButton(x, y, cardW, cardH, () => Go(Page.Class, classType.ToString()), 0.08f);
            var portrait = Portrait(classType, 70, cardH / 2, 110);
            if (portrait != null) {
                card.AddChild(portrait);
            }

            card.AddChild(Text(props.ObjectId.ToUpperInvariant(), BodySize, 140, 26, UiAnchor.MiddleLeft, outline: 1));
            card.AddChild(Text(props.Description ?? "", SmallSize - 1, 140, 46, UiAnchor.LeftTop, color: InkSoft, maxWidth: cardW - 156, outline: 1));
            card.AddChild(Text(string.Join("  -  ", ClassSlotNames(props)), SmallSize - 3, 140, cardH - 18, UiAnchor.MiddleLeft, color: InkSoft, outline: 1, maxWidth: cardW - 156));
            _content.AddChild(card);
        }
    }

    private static List<string> ClassSlotNames(ObjectProperties props) {
        var names = new List<string>();
        foreach (var s in ObjectLibraryHelper.ClassSlots(props.ObjectType)) {
            names.Add(SlotName(s));
        }

        return names;
    }

    private void BuildClassPage(string arg) {
        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type) || !ObjectLibrary.TypeToObjectProps.TryGetValue((ushort) type, out var props) || props.PlayerProperties == null) {
            PageTitle("CLASS");
            Notice("No such class.");
            return;
        }

        PageTitle(props.ObjectId.ToUpperInvariant());
        _content.AddChild(BuildTextButton("< ALL CLASSES", SmallSize - 2, ContentRight, ContentTop + 12, UiAnchor.RightTop, () => Go(Page.Classes), InkSoft));

        var top = ContentTop + 66;
        var portrait = Portrait(type, ContentLeft + 80, top + 80, 150);
        if (portrait != null) {
            _content.AddChild(portrait);
        }

        var left = ContentLeft + 190;
        _content.AddChild(Text(props.Description ?? "", BodySize - 2, left, top, UiAnchor.LeftTop, maxWidth: 420, outline: 1));

        // Stats table (start / max) under the description.
        var p = props.PlayerProperties;
        (string Label, int Start, int Max)[] stats = [
            ("HP", p.Hp, p.MaxHp), ("MP", p.Mp, p.MaxMp), ("ATT", p.Attack, p.MaxAttack), ("DEF", p.Defense, p.MaxDefense),
            ("SPD", p.Speed, p.MaxSpeed), ("DEX", p.Dexterity, p.MaxDexterity), ("VIT", p.Vitality, p.MaxVitality), ("WIS", p.Wisdom, p.MaxWisdom)
        ];
        var tableTop = top + 176;
        _content.AddChild(Text("STAT", SmallSize - 3, ContentLeft, tableTop, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Text("START", SmallSize - 3, ContentLeft + 200, tableTop, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
        _content.AddChild(Text("MAX", SmallSize - 3, ContentLeft + 320, tableTop, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
        _content.AddChild(Rule(ContentLeft + 160, tableTop + 12, 320));
        for (var i = 0; i < stats.Length; i++) {
            var y = tableTop + 30 + i * 26;
            _content.AddChild(Text(stats[i].Label, SmallSize, ContentLeft, y, UiAnchor.MiddleLeft, outline: 1));
            _content.AddChild(Text(stats[i].Start.ToString(), SmallSize, ContentLeft + 200, y, UiAnchor.MiddleRight, outline: 1));
            _content.AddChild(Text(stats[i].Max.ToString(), SmallSize, ContentLeft + 320, y, UiAnchor.MiddleRight, color: Gold, outline: 1));
        }

        // Right side: the equipment slots with the starting gear, then every item this class can use.
        var rightX = ContentLeft + 640;
        _content.AddChild(Text("EQUIPMENT", SmallSize - 3, rightX, top, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        var slots = ObjectLibraryHelper.ClassSlots(type);
        var starting = props.Equipment ?? [];
        for (var s = 0; s < slots.Count && s < 4; s++) {
            var sx = rightX + s * 120;
            var item = s < starting.Count && starting[s].HasValue ? starting[s].Value : (ushort) 0;
            _content.AddChild(Box(sx, top + 16, 48, 48, item > 0 ? 0.18f : 0.08f));
            if (item > 0) {
                _content.AddChild(ItemIcon(item, sx + 24, top + 40, 42));
                var itemType = item;
                _content.AddChild(BuildTextButton(ItemName(item), SmallSize - 4, sx, top + 76, UiAnchor.LeftTop, () => Go(Page.Item, itemType.ToString()), maxWidth: 112));
            } else {
                _content.AddChild(Text("empty", SmallSize - 4, sx, top + 76, UiAnchor.LeftTop, color: InkSoft, outline: 1));
            }

            _content.AddChild(Text(SlotName(slots[s]), SmallSize - 3, sx + 56, top + 40, UiAnchor.MiddleLeft, color: InkSoft, outline: 1, maxWidth: 60));
        }

        var gearTop = top + 116;
        _content.AddChild(Text("GEAR THIS CLASS CAN USE", SmallSize - 3, rightX, gearTop, UiAnchor.MiddleLeft, color: InkSoft, outline: 1));
        _content.AddChild(Rule(rightX + (ContentRight - rightX) / 2, gearTop + 12, ContentRight - rightX));
        var gear = AllItems().FindAll(i => slots.Contains(i.SlotType));
        var rowY = gearTop + 20;
        const int rowH = 36;
        var perPage = Math.Max(1, (ContentBottom - 28 - rowY) / rowH);
        var (first, count) = Pager(gear.Count, perPage);
        for (var i = 0; i < count; i++) {
            var item = gear[first + i];
            var itemType = item.ObjectType;
            var row = BuildRowButton(rightX, rowY + i * rowH, ContentRight - rightX, rowH - 3, () => Go(Page.Item, itemType.ToString()), 0.05f);
            row.AddChild(ItemIcon(itemType, 20, (rowH - 3) / 2, 28));
            row.AddChild(Text(item.DisplayName, SmallSize, 44, (rowH - 3) / 2, UiAnchor.MiddleLeft, outline: 1, maxWidth: 260));
            row.AddChild(Text(TierLabel(item) + SlotName(item.SlotType), SmallSize - 3, ContentRight - rightX - 8, (rowH - 3) / 2, UiAnchor.MiddleRight, color: InkSoft, outline: 1));
            _content.AddChild(row);
        }
    }

    #endregion

    #region Graveyard

    private void BuildGraveyardPage() {
        PageTitle("GRAVEYARD");
        _content.AddChild(Text("The realm keeps no record of its dead yet.", BodySize, ContentCenterX, ContentTop + 120, UiAnchor.Middle, color: InkSoft, outline: 1));
        _content.AddChild(Text("When the game starts writing down how each character fell, this page will list every death with its class, level, fame, equipment and killer.",
            SmallSize + 1, ContentCenterX, ContentTop + 170, UiAnchor.Middle, color: InkSoft, maxWidth: ContentW - 200, outline: 1));
        _content.AddChild(BuildScrollButton("THE LIVING", ContentCenterX, ContentTop + 260, 200, 44, () => Go(Page.Top, "fame")));
    }

    #endregion
}

// The four gear slot types of a class, in slot order (Players.xml <SlotTypes>, first four; the rest are inventory).
internal static class ObjectLibraryHelper {
    public static List<int> ClassSlots(int classType) {
        var slots = new List<int>();
        if (ObjectLibrary.TypeToObjectProps.TryGetValue((ushort) classType, out var props) && props.SlotTypes != null) {
            for (var i = 0; i < props.SlotTypes.Count && i < 4; i++) {
                slots.Add(props.SlotTypes[i]);
            }
        }

        return slots;
    }
}
