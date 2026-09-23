using System;
using WaWClient.Game.Components.Options.Ui;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Options.OptionTypes;

public class SliderOption : Option {
    private readonly SimpleText _text;

    public SliderOption(ValueSetting<float> setting, string text, Action<float> sliderCallback) : base(setting, null, null) {
        _text = OptionsStyle.Label(text, FontGroup.MyriadPro, 19f, 0, KeyCodeBox.BoxHeight / 2, UiAnchor.MiddleLeft, OptionsStyle.Tan);
        AddChild(_text);
    }

    public override void Refresh() {
    }
    
    public override void SetDisabled(bool val) {
        _disabled = val;
        Alpha = val ? 0.6f : 1f;
    }
}