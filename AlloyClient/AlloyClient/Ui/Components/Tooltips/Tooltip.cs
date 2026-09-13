using System.Reflection.Metadata.Ecma335;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Ui.Components.Tooltips;

public abstract class Tooltip : Sprite {

    private NineSliceRect TooltipSprite;
    private NineSliceConfig TooltipConfig;


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

    public virtual void DrawSprite()
    {
        TooltipConfig = new NineSliceConfig
        {
            SliceData = SliceLibrary.TooltipBackgroundSmall,
            CutX = 5,
            CutY = 5,
            Width = ToolWidth,
            Height = ToolHeight
        };
        TooltipSprite = new NineSliceRect(TooltipConfig);
        Contain.AddChild(TooltipSprite);
        Width = ToolWidth;
        Height = ToolHeight;
        //todo:SetBaseDimensions(ToolWidth, ToolHeight);
    }
    
}