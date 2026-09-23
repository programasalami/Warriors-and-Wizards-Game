using System.Reflection.Metadata.Ecma335;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Ui.Components.Tooltips;

public abstract class Tooltip : Sprite {

    private Container Contain;

    public int ToolWidth;
    public int ToolHeight;
    protected Tooltip(int width, int height) {
        TooltipMode = true;

        ToolWidth = width;
        ToolHeight = height;

        // Width/Height aren't plain pixel fields here - the setters compute a scale factor
        // from ContentWidth/ContentHeight (Width = ContentWidth * ScaleX). Set before any
        // children exist, ContentWidth is 0 and GetScale(0, ...) returns 0, permanently
        // locking the tooltip at zero scale (invisible) since nothing resets it later.
        // Set them in DrawSprite() instead, once there's real content to scale against.

        Contain = new Container();
        AddChild(Contain);
    }

    // The pack's frame is thick (8px borders at 2x), so it is drawn Pad pixels bigger than the content box on every side and the content is
    // shifted in to match.
    private const int Pad = 10;

    public virtual void DrawSprite()
    {
        for (var i = 0; i < NumChildren; i++) {
            var child = GetChildAt(i);
            if (child != Contain) {
                child.X += Pad;
                child.Y += Pad;
            }
        }

        var panel = WaWClient.Game.Components.Hud.WaWStyle.Panel(ToolWidth + Pad * 2, ToolHeight + Pad * 2);
        Contain.AddChild(panel);
        Width = ToolWidth + Pad * 2;
        Height = ToolHeight + Pad * 2;
    }

}
