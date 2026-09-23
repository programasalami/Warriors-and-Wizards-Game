using System;
using WaWClient.Ui.Components.Tooltips;
using WaW.UiLib.Core;

namespace WaWClient.Display;

public sealed class TooltipManager : Sprite {

    private static TooltipManager _instance;
    
    private static Tooltip _current;

    public TooltipManager() {
        _instance = this;
    }

    private const int CursorOffset = 12;

    public static void AddTooltip(Tooltip tooltip) {
        if (_current != null)
            _instance.RemoveChild(_current);

        _current = tooltip;
        _instance.AddChild(_current);

        var mouse = _instance.Stage.Mouse.GetMousePosition();
        var x = (int)mouse.X + CursorOffset;
        var y = (int)mouse.Y + CursorOffset;

        // Keep the tooltip fully on-screen instead of letting it run off the right/bottom edge.
        x = Math.Min(x, _instance.Stage.StageWidth - tooltip.ToolWidth);
        y = Math.Min(y, _instance.Stage.StageHeight - tooltip.ToolHeight);

        _current.X = x;
        _current.Y = y;
    }
    
    public static void RemoveTooltip(Tooltip tooltip) {
        if (_current != tooltip) return;
        _instance.RemoveChild(_current);
    }
}