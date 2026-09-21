using System;
using AlloyClient.Assets;
using AlloyClient.Assets.Libraries;
using AlloyClient.Assets.XmlStructs;
using AlloyClient.Game.Components.Options;
using AlloyClient.Game.Objects;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components.Hud;

// Your character's numbers: for HP, MP and the six stats - the current value, any boost on top of it, a bar toward your class's
// maximum, and how much is still left to max. Opened from the STATS side tab.
public sealed class StatsPopup : HudPopup {
    // As wide as the player plate above it, so their sides line up; the height comes from the layout (the gap above the gear).
    public const int PopupW = PlayerPlate.Width;
    private const int MinHeight = 300;

    private const int FirstRowY = 42;         // no header any more: the rows start just under the close button
    private const int RowHeight = 30;
    private const int BarX = 150;
    private const int BarWidth = 120;

    private static readonly string[] Names = ["HP", "MP", "ATT", "DEF", "SPD", "DEX", "VIT", "WIS"];

    private readonly Row[] _rows = new Row[Names.Length];

    public StatsPopup() : base("", PopupW, MinHeight) {
        for (var i = 0; i < _rows.Length; i++) {
            _rows[i] = new Row(this, i, FirstRowY + 12 + i * RowHeight);
        }
    }

    protected override void OnOpened() => Refresh();

    public override void Refresh() {
        var p = Map.LocalPlayer;
        if (p == null) {
            return;
        }

        ObjectLibrary.TypeToClassProps.TryGetValue(p.Type, out var cls);

        // (value shown, boost on it, class maximum). HP / MP show the max the character has, not the current pool.
        (int Value, int Boost, int Max)[] data = [
            (p.MaxHp, p.MaxHpBoost, cls?.MaxHp ?? 0),
            (p.MaxMp, p.MaxMpBoost, cls?.MaxMp ?? 0),
            (p.Attack, p.AttackBoost, cls?.MaxAttack ?? 0),
            (p.Defense, p.DefenseBoost, cls?.MaxDefense ?? 0),
            (p.Speed, p.SpeedBoost, cls?.MaxSpeed ?? 0),
            (p.Dexterity, p.DexterityBoost, cls?.MaxDexterity ?? 0),
            (p.Vitality, p.VitalityBoost, cls?.MaxVitality ?? 0),
            (p.Wisdom, p.WisdomBoost, cls?.MaxWisdom ?? 0),
        ];

        for (var i = 0; i < _rows.Length; i++) {
            _rows[i].Update(data[i].Value, data[i].Boost, data[i].Max);
        }
    }

    private sealed class Row {
        private const uint Green = 0x7FD46B;
        private const uint MaxedGold = 0xF2D27A;

        private readonly SimpleText _value;
        private readonly SimpleText _left;
        private readonly HudBar _bar;
        private int _lastValue = int.MinValue;
        private int _lastBoost = int.MinValue;
        private int _lastMax = int.MinValue;

        public Row(Sprite parent, int index, int centreY) {
            parent.AddChild(OptionsStyle.Label(Names[index], FontGroup.MyriadPro, 16f, 18, centreY, UiAnchor.MiddleLeft, OptionsStyle.Tan, 1));

            _value = OptionsStyle.Label("", FontGroup.MyriadPro, 17f, 68, centreY, UiAnchor.MiddleLeft, OptionsStyle.Cream, 1);
            parent.AddChild(_value);

            _bar = new HudBar(BarWidth, 2, WaWStyle.Orange, 0x402E2B, showText: false) { X = BarX, Y = centreY - 6 };
            parent.AddChild(_bar);

            _left = OptionsStyle.Label("", FontGroup.MyriadPro, 14f, PopupW - 16, centreY, UiAnchor.MiddleRight, OptionsStyle.Tan, 1);
            parent.AddChild(_left);
        }

        public void Update(int value, int boost, int max) {
            if (value == _lastValue && boost == _lastBoost && max == _lastMax) {
                return;
            }

            _lastValue = value;
            _lastBoost = boost;
            _lastMax = max;

            _value.SetText(boost > 0 ? $"{value}  +{boost}" : boost < 0 ? $"{value}  {boost}" : $"{value}");
            _value.SetColor(boost > 0 ? Green : OptionsStyle.Cream);

            // "to max" counts the character's own points: the value minus whatever gear / effects add on top
            var own = Math.Max(0, value - boost);
            _bar.SetRatio(max > 0 ? own / (float) max : 0f);

            var left = max - own;
            if (max <= 0) {
                _left.SetText("");
            } else if (left <= 0) {
                _left.SetText("MAXED");
                _left.SetColor(MaxedGold);
            } else {
                _left.SetText($"{left} left");
                _left.SetColor(OptionsStyle.Tan);
            }
        }
    }
}
