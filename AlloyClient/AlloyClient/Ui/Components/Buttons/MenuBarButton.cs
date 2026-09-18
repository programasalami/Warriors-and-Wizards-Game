using System;
using Alloy.UiLib.Core;
using Alloy.Common;
using OpenTK.Mathematics;

namespace AlloyClient.Ui.Components.Buttons;

public sealed class MenuBarButton : TextButton {

    private Vector2 _baseScale = Vector2.One;

    public Vector2 BaseScale {
        get => _baseScale;
        set {
            _baseScale = value;
            Scale = value;
        }
    }

    public MenuBarButton(string text, float size, Action callback, bool pulse = false, FontGroup fontGroup = FontGroup.MyriadPro) : base (new TextButtonConfig {
        Text = text, FontSize = size, OnClicked = callback, OutlineThickness = 4, FontGroup = fontGroup,
        FontType = fontGroup == FontGroup.MyriadPro ? FontType.Bold : FontType.Normal
    }) {
        AddPulse(pulse);
    }

    public MenuBarButton(TextButtonConfig config, bool pulse = false) : base(config) {
        AddPulse(pulse);
    }

    private void AddPulse(bool pulse) {
        if (!pulse) {
            return;
        }

        AddEventListener(Event.AddedToStage, () => AddEventListener(Event.EnterFrame, OnFrameEnter));
        RemoveEventListener(Event.AddedToStage, () => RemoveEventListener(Event.EnterFrame, OnFrameEnter));
    }

    private void OnFrameEnter() {
        var gameTime = Stage.GameTime;
        var pulse = 1.05f + 0.05f * (float)Math.Sin(gameTime.TotalMs / 200);
        Scale = _baseScale * pulse;
    }
}