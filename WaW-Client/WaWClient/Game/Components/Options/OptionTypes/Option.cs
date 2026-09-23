using WaWClient.Game.Components.Options.Ui;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Options.OptionTypes;

public abstract class Option : Sprite {
    // TODO: tooltip
    public readonly ISettingType Setting;
    protected readonly SimpleText DescText;
    protected bool _disabled;

    protected Option(ISettingType setting, string desc, string tooltipDesc) {
        Setting = setting;
        
        if (!string.IsNullOrEmpty(desc)) {
            DescText = OptionsStyle.Label(desc, FontGroup.MyriadPro, 19f, KeyCodeBox.BoxWidth + 22, KeyCodeBox.BoxHeight / 2, UiAnchor.MiddleLeft, OptionsStyle.Cream);
            AddChild(DescText);
        }

        if (!string.IsNullOrEmpty(tooltipDesc)) {
            // Add tooltip here
        }
    }

    public abstract void Refresh();

    public abstract void SetDisabled(bool val);
}