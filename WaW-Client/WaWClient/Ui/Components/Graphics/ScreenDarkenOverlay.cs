using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;

namespace WaWClient.Ui.Components.Graphics;

public class ScreenDarkenOverlay : UiElement {
    
    private readonly ColorRect _darken = new ColorRect(new ColorRectConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight, Color = 0x2B2B2B, Alpha = 0.8f, MouseEnabled = true});

    public ScreenDarkenOverlay() {
        AddChild(_darken);
    }

    protected override void OnResize(ResizeEvent args) {
        _darken.Resize(args.Width, args.Height);
    }
}