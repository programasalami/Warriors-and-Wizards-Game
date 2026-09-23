using Alloy.UiLib;
using Alloy.UiLib.Core;
using Alloy.Engine;
using OpenTK.Graphics.OpenGL;

namespace AlloyClient.Display;

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
        var sect = AlloyClient.Game.PerfSections.Begin();
        _stage.Update(gameTime);
        AlloyClient.Game.PerfSections.End(AlloyClient.Game.PerfSections.Section.StageUpdate, sect);
    }

    public static void Draw(GameTime gameTime) {
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        UiRender.LastRenderCount = 0;
        ScreenManager.Draw(gameTime);
        var sect = AlloyClient.Game.PerfSections.Begin();
        _stage.Draw(gameTime);
        AlloyClient.Game.PerfSections.End(AlloyClient.Game.PerfSections.Section.Ui, sect);
    }

}