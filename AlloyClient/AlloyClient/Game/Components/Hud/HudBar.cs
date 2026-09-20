using System;
using AlloyClient.Game.Components.Options;
using AlloyClient.Ui;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Game.Components.Hud;

// A bar in the pack's style: its outlined frame (24x6 art pixels, stretched sideways) with a dark trough and a fill of three solid
// rows (light / mid / dark - the pack's own bar strips) running inside. The frame's interior is 3 art rows tall; `extraRows` makes the bar
// taller by stretching the frame's middle and the fill's mid row, so the bar keeps its interior height despite the border eating some.
public sealed class HudBar : Sprite {
    private const int InnerInset = 1;       // art pixels between the frame's outer edge and its interior (sides and top)
    private const int NativeHeight = 6;

    private readonly int _scale;
    private readonly int _width;
    private readonly int _height;
    private readonly int _innerX;
    private readonly int _innerW;
    private readonly ColorRect[] _rows = new ColorRect[3];
    private readonly int[] _rowHeights = new int[3];
    private readonly ColorRect _track;
    private SimpleText _label;
    private readonly SimpleText _valueText;
    private readonly bool _showText;

    private bool _mouseOver;
    private TextState _textState;

    public string LabelString;

    public int PixelWidth => _width;
    public int PixelHeight => _height;

    public HudBar(int pixelWidth, int scale, uint[] fillRows, uint trackColor, string label = "", bool showText = true, int extraRows = 0) {
        _scale = scale;
        _showText = showText;
        _width = pixelWidth / scale * scale;
        _height = (NativeHeight + extraRows) * scale;
        _innerX = InnerInset * scale;
        _innerW = _width - 2 * InnerInset * scale;

        AddChild(WaWStyle.Scaled(SliceLibrary.WaWBar, 4, 2, _width, _height, scale));

        _rowHeights[0] = scale;
        _rowHeights[1] = (1 + extraRows) * scale;
        _rowHeights[2] = scale;
        _track = new ColorRect(new ColorRectConfig { X = _innerX, Y = _innerX, Width = _innerW, Height = (3 + extraRows) * scale, Color = trackColor });
        AddChild(_track);
        var y = _innerX;
        for (var i = 0; i < 3; i++) {
            _rows[i] = new ColorRect(new ColorRectConfig { X = _innerX, Y = y, Width = _innerW, Height = _rowHeights[i], Color = fillRows[i] });
            AddChild(_rows[i]);
            y += _rowHeights[i];
        }

        if (!showText) {
            return;
        }

        LabelString = label;
        _label = MakeLabel(label);
        AddChild(_label);

        _valueText = OptionsStyle.Label("", FontGroup.MyriadPro, TextSize, 0, _height / 2, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);
        AddChild(_valueText);

        MouseEnabled = true;
        AddEventListener(MouseEvent.MouseOver, () => _mouseOver = true);
        AddEventListener(MouseEvent.MouseOut, () => _mouseOver = false);
    }

    private float TextSize => _scale >= 3 ? 14f : 11f;

    private SimpleText MakeLabel(string text) =>
        OptionsStyle.Label(text, FontGroup.MyriadPro, TextSize, _innerX + 5, _height / 2, UiAnchor.MiddleLeft, WaWStyle.Highlight, 1);

    public void UpdateLabel(string label) {
        if (!_showText || label == LabelString) {
            return;
        }

        RemoveChild(_label);
        LabelString = label;
        _label = MakeLabel(label);
        AddChild(_label);
    }

    // Sets the fill from 0..1 (used directly by bars that show no numbers).
    public void SetRatio(float ratio) {
        var w = (int) (_innerW * Math.Clamp(ratio, 0f, 1f));
        for (var i = 0; i < _rows.Length; i++) {
            _rows[i].Visible = w > 0;
            if (w > 0) {
                _rows[i].Resize(w, _rowHeights[i]);
            }
        }
    }

    public void Update(int val, int max, int boost = 0, int baseMax = -1, int level = -1) {
        SetRatio(max > 0 ? val / (float) max : 0f);

        if (!_showText) {
            return;
        }

        _valueText.Visible = _mouseOver || Settings.ToggleBarText;
        UpdateText(val, max, boost, baseMax, level);
    }

    private void UpdateText(int val, int max, int boost, int baseMax, int level) {
        if (!_valueText.Visible) {
            return;
        }

        var newState = new TextState(val, max, boost, baseMax, level);
        if (newState == _textState) {
            return;
        }

        _textState = newState;

        var ltmt = "";
        if (Settings.ToggleLeftToMax) {
            var ltm = baseMax - max - boost;
            if (level >= 20 && ltm > 0) {
                ltmt = $"|{Math.Ceiling(ltm / 5f)}";
            }
        }

        _valueText.SetText(max > 0 ? $"{val}/{max}" + ltmt : $"{val}");
        _valueText.X = _width - _valueText.Width - _innerX - 5;
    }

    private record struct TextState(int Value, int Max, int Boost, int BaseMax, int Level);
}
