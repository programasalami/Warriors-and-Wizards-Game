using System;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Data;
using Alloy.Common;
using AlloyClient.Utils;
using OpenTK.Mathematics;

namespace AlloyClient.Ui;

public static class SliceLibrary {
    
    //todo probably turn this into an xml file instead
    
    public const string StatusBar = "bar3";
    public const string ScrollBarBg = "ScrollBar/ScrollBarBackground";
    public const string ScrollBar = "ScrollBar/ScrollBarHandle";

    public const string TooltipBackgroundLarge = "tooltipBackgroundLarge";
    public const string TooltipBackgroundMedium = "tooltipBackgroundMedium";
    public const string TooltipBackgroundSmall = "tooltipBackgroundSmall";

    public const string ButtonFrameRed = "Buttons/ButtonFrameRed";
    public const string TitleButtonsFrame = "Buttons/TitleButtonsFrame";
    public const string LoginFrame = "Frames/LoginFrame";
    public const string PageButtonFrame = "Buttons/PageButtonFrame";

    // "Dark Dwellers" pack (CC0, from the user's Desktop/GUI folder) - character select + class
    // creation redesign. Picked over the pack's other, blue/gold "manaSoul" set because its
    // purple/gold palette is a near-exact match for the BookOverlay cover art already in the
    // game (Content/Ui/Book/BookCover.png). Source cells cropped out of the pack's sprite sheets
    // into Content/Ui/DarkDwellers/*.png via a one-off script, not hand-edited.
    public const string DarkDwellersFrame = "DarkDwellers/Frame";
    public const string DarkDwellersFrameAccent = "DarkDwellers/FrameAccent";
    public const string DarkDwellersHorizFrame = "DarkDwellers/HorizFrame";
    public const string DarkDwellersButton = "DarkDwellers/Button";
    public const string DarkDwellersButtonAccent = "DarkDwellers/ButtonAccent";
    public const string DarkDwellersTab = "DarkDwellers/Tab";
    public const string DarkDwellersTabActive = "DarkDwellers/TabActive";

    public static void Load() {
        CreateSlice(TextInput.BoxLookup, 2, 2, "textBox", false);
        CreateSlice(StatusBar, 7, 7, "bar3");

        CreateSlice(ScrollBarBg, 4, 4, "ScrollBar/ScrollBarBackground");
        CreateSlice(ScrollBar, 7, 7, "ScrollBar/ScrollBarHandle");

        CreateSlice(TooltipBackgroundLarge, 30, 30, "tooltipBackgroundLarge");
        CreateSlice(TooltipBackgroundMedium, 20, 20, "tooltipBackgroundMedium");
        CreateSlice(TooltipBackgroundSmall, 10, 10, "tooltipBackgroundSmall");

        CreateSlice(ButtonFrameRed, 3, 3, "Buttons/ButtonFrameRed");
        CreateSlice(TitleButtonsFrame, 50, 140, "Buttons/TitleButtonsFrame");
        CreateSlice(LoginFrame, 14, 28, "Frames/LoginFrame");

        // Cut measured directly from the 224x96 source: an 8px transparent margin around the
        // shape, then an ~18px diagonal corner cut - 26px covers both, so the fixed corner slice
        // stops right where the shape becomes a clean flat-sided rectangle.
        CreateSlice(PageButtonFrame, 26, 26, "Buttons/PageButtonFrame");

        // Cuts sized to preserve each source's corner/end-cap ornamentation at any stretched
        // size (NineSliceRect clamps the cut's on-screen size to at most half the stretched
        // width/height, so oversizing these is safe - it just clamps rather than overlapping).
        CreateSlice(DarkDwellersFrame, 30, 30, "DarkDwellers/Frame", false);
        CreateSlice(DarkDwellersFrameAccent, 30, 30, "DarkDwellers/FrameAccent", false);
        CreateSlice(DarkDwellersHorizFrame, 20, 10, "DarkDwellers/HorizFrame", false);
        CreateSlice(DarkDwellersButton, 16, 8, "DarkDwellers/Button", false);
        CreateSlice(DarkDwellersButtonAccent, 16, 8, "DarkDwellers/ButtonAccent", false);
        CreateSlice(DarkDwellersTab, 20, 10, "DarkDwellers/Tab", false);
        CreateSlice(DarkDwellersTabActive, 20, 10, "DarkDwellers/TabActive", false);
    }
    
    private static void CreateSlice(string lookup, int x, int y, string atlasLookup, bool padding = true, int lookupIndex = 0) {
        if (SliceLookup.CheckLookup(lookup)) throw new Exception($"Already contains data for lookup: {lookup}");
        
        var uv = Main.UiAtlas.GetAtlasData(atlasLookup, lookupIndex);
        if (!padding) uv.RemovePadding();
        var cuts = new Vector2(x / AtlasConfig.AtlasWidth, y / AtlasConfig.AtlasHeight);
        SliceLookup.CreateSlice(lookup, cuts, uv.ToPosition());
    }
    
}