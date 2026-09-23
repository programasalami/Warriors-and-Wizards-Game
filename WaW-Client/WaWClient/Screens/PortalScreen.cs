using WaWClient.Display;
using WaWClient.Screens.Components;
using WaWClient.Screens.Components.CharacterList;
using WaWClient.Screens.Components.Portal;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Extra;

namespace WaWClient.Screens;

// The Portal inside the client: the same pages as portal.<domain> (search, player profiles, guilds, leaderboards, the item and class
// wiki, the graveyard), drawn on a parchment board over the forest backdrop. Opened from the title screen's PORTAL row; its BACK
// icon walks back through the pages it visited and, from the first one, returns to the title screen.
public class PortalScreen : TitleScreenBase {
    private readonly Container _root;
    private readonly ForestBackdrop _background;

    public PortalScreen(string openPlayer = null) {
        _background = new ForestBackdrop();
        AddChild(_background);

        _root = new Container();
        AddChild(_root);
        _root.AddChild(new PortalView(OnExit, openPlayer));

        MouseEnabled = true;
    }

    // Same "contain" scaling + centring as CharacterListScreen (see the comment there).
    protected override void OnResize(ResizeEvent args) {
        base.OnResize(args);
        var scale = Stage.ScreenScale;
        _root.Scale = scale;
        _root.X = (int)((Stage.StageWidth - Settings.DefaultScreenWidth * scale.X) / 2f);
        _root.Y = (int)((Stage.StageHeight - Settings.DefaultScreenHeight * scale.Y) / 2f);
        _background.Resize(args.Width, args.Height);
    }

    private static void OnExit() {
        ScreenManager.FadeToScreen(new TitleScreen(), Easing.SineInOut, 1000, 0x0);
    }
}
