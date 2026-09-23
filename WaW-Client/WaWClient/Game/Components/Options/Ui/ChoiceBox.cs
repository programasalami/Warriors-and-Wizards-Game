using System;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Game.Components.Options.Ui;

public class ChoiceBox<T> : Sprite {
    private const int BoxWidth = KeyCodeBox.BoxWidth;
    private const int BoxHeight = KeyCodeBox.BoxHeight;

    private readonly ValueSetting<T> _setting;
    private readonly string[] _labels;
    private readonly object[] _values;
    private readonly Action _callback;

    private readonly NineSliceRect _background;
    private readonly SimpleText _char;
    private int _selected;

    public ChoiceBox(ValueSetting<T> setting, string[] labels, object[] values, Action callback) {
        //todo:SetBaseDimensions(BoxWidth, BoxHeight);
        MouseEnabled = true;

        _setting = setting;
        _labels = labels;
        _values = values;
        _callback = callback;

        if (setting != null) {
            for (var i = 0; i < values.Length; i++) {
                if (!setting.Value.Equals((T) values[i])) {
                    continue;
                }

                _selected = i;
                break;
            }
        }

        _background = OptionsStyle.Slot(BoxWidth, BoxHeight);
        AddChild(_background);

        _char = OptionsStyle.Label(labels[_selected], FontGroup.MyriadPro, 20f, BoxWidth / 2, BoxHeight / 2, UiAnchor.Middle, OptionsStyle.Gold);
        AddChild(_char);

        AddEventListener(MouseEvent.MouseOver, () => _background.ColorTransformation = OptionsStyle.SlotHoverTint);
        AddEventListener(MouseEvent.MouseOut, () => _background.ColorTransformation = OptionsStyle.NormalTint);
        AddEventListener(MouseEvent.LeftClick, OnClick);
    }

    private void OnClick() {
        SetSelected(_selected + 1);
        _callback.Invoke();
    }

    private void SetSelected(int selected) {
        _selected = selected >= _values.Length ? 0 : selected;
        _char.SetText(_labels[_selected]);
        _setting?.Set((T) _values[_selected]);
        // Saved the moment it changes (2026-09-22): settings used to reach settings.xml only on a clean exit or PLAY, so a crash, a forced close or a
        // second game window saving on exit threw the change away (the user's VSync-off kept coming back as on).
        Settings.SaveSettings();
    }
}