using System;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Data;
using WaW.Common;
using WaWClient.Utils;
using OpenTK.Mathematics;

namespace WaWClient.Ui;

public static class SliceLibrary {
    
    //todo probably turn this into an xml file instead
    
    public const string StatusBar = "bar3";
    public const string ScrollBarBg = "ScrollBar/ScrollBarBackground";
    public const string ScrollBar = "ScrollBar/ScrollBarHandle";

    public const string TooltipBackgroundLarge = "tooltipBackgroundLarge";
    public const string TooltipBackgroundMedium = "tooltipBackgroundMedium";
    public const string TooltipBackgroundSmall = "tooltipBackgroundSmall";

    public const string ButtonFrameRed = "Buttons/ButtonFrameRed";
    public const string LoginFrame = "Frames/LoginFrame";

    public const string PageButtonFrame = "Buttons/PageButtonFrame";

    // Pieces of the "Dark Ages UI" pack (Hypnobius; cut by Tools/DarkAges/build_darkages_assets.ps1) that stretch: the
    // character creation popup's two tall cards (a rounded dark box with a grey border), its description box and gear slots (the
    // same box, a shade lighter) and the parchment scroll its CANCEL / CREATE buttons are made from.
    public const string DarkAgesPanel = "DarkAges/Panel";
    public const string DarkAgesSlot = "DarkAges/Slot";
    public const string DarkAgesParchment = "DarkAges/Parchment";
    // The in-game GUI (HUD, popups, options menu) is built from the "Mini Medieval User Interface" pack (Tools/WaWGui/build_waw_gui.py): a gem-cornered
    // orange frame over dark brown, the inventory-slot outline, the portrait frame and the bar frame. See Game/Components/Hud/WaWStyle.cs for the scales they are drawn at.
    public const string WaWPanel = "WaW/Panel";
    public const string WaWSlot = "WaW/Slot";
    public const string WaWPortrait = "WaW/PortraitFrame";
    public const string WaWBar = "WaW/BarFrame";
    public const string WaWScrollTrack = "WaW/ScrollTrack";
    public const string WaWScrollHandle = "WaW/ScrollHandle";
    public const string WaWButton = "WaW/Button";

    public static void Load() {
        CreateSlice(TextInput.BoxLookup, 2, 2, "textBox", false);
        CreateSlice(StatusBar, 7, 7, "bar3");

        CreateSlice(ScrollBarBg, 4, 4, "ScrollBar/ScrollBarBackground");
        CreateSlice(ScrollBar, 7, 7, "ScrollBar/ScrollBarHandle");

        CreateSlice(TooltipBackgroundLarge, 30, 30, "tooltipBackgroundLarge");
        CreateSlice(TooltipBackgroundMedium, 20, 20, "tooltipBackgroundMedium");
        CreateSlice(TooltipBackgroundSmall, 10, 10, "tooltipBackgroundSmall");

        CreateSlice(ButtonFrameRed, 3, 3, "Buttons/ButtonFrameRed");
        CreateSlice(LoginFrame, 14, 28, "Frames/LoginFrame");

        // The boxes' 8px rounded, bevelled corners stay fixed; their plain edges and fill stretch.
        CreateSlice(DarkAgesPanel, 8, 8, "DarkAges/Panel", false);
        CreateSlice(DarkAgesSlot, 8, 8, "DarkAges/Slot", false);
        CreateSlice(DarkAgesParchment, 10, 10, "DarkAges/Parchment", false);
        // the pack's fixed corners (gems / studs) stay put, the plain edges and fill stretch
        CreateSlice(WaWPanel, 8, 8, "WaW/Panel", false);
        CreateSlice(WaWSlot, 6, 6, "WaW/Slot", false);
        CreateSlice(WaWPortrait, 7, 7, "WaW/PortraitFrame", false);
        CreateSlice(WaWBar, 4, 2, "WaW/BarFrame", false);
        CreateSlice(WaWScrollTrack, 1, 7, "WaW/ScrollTrack", false);
        CreateSlice(WaWScrollHandle, 1, 4, "WaW/ScrollHandle", false);
        CreateSlice(WaWButton, 4, 4, "WaW/Button", false);

        // Cut measured directly from the 224x96 source: an 8px transparent margin around the
        // shape, then an ~18px diagonal corner cut - 26px covers both, so the fixed corner slice
        // stops right where the shape becomes a clean flat-sided rectangle.
        CreateSlice(PageButtonFrame, 26, 26, "Buttons/PageButtonFrame");
    }
    
    private static void CreateSlice(string lookup, int x, int y, string atlasLookup, bool padding = true, int lookupIndex = 0) {
        if (SliceLookup.CheckLookup(lookup)) throw new Exception($"Already contains data for lookup: {lookup}");
        
        var uv = Main.UiAtlas.GetAtlasData(atlasLookup, lookupIndex);
        if (!padding) uv.RemovePadding();
        var cuts = new Vector2(x / AtlasConfig.AtlasWidth, y / AtlasConfig.AtlasHeight);
        SliceLookup.CreateSlice(lookup, cuts, uv.ToPosition());
    }
    
}