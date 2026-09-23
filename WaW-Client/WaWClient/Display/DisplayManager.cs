using WaW.UiLib;
using WaW.UiLib.Core;
using WaW.Engine;
using OpenTK.Graphics.OpenGL;

namespace WaWClient.Display;

public static class DisplayManager {

    private static Stage _stage;

    public static void Init(Stage stage) {
        if (_stage != null)
            return;

        _stage = stage;
        _stage.AddChild(ScreenManager.FadeScreen);
        _stage.AddChild(new ScreenManager());
        _stage.AddChild(new OverlayManager());
        _stage.AddChild(new DialogManager());
        _stage.AddChild(new TooltipManager());
    }

    public static void Update(GameTime gameTime) {
        ScreenManager.Update(gameTime);
        var sect = WaWClient.Game.PerfSections.Begin();
        _stage.Update(gameTime);
        WaWClient.Game.PerfSections.End(WaWClient.Game.PerfSections.Section.StageUpdate, sect);
    }

    public static void Draw(GameTime gameTime) {
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        UiRender.LastRenderCount = 0;
        ScreenManager.Draw(gameTime);
        var sect = WaWClient.Game.PerfSections.Begin();
        _stage.Draw(gameTime);
        WaWClient.Game.PerfSections.End(WaWClient.Game.PerfSections.Section.Ui, sect);
    }

}