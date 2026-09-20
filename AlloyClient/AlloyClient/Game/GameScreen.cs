using AlloyClient.Display;
using AlloyClient.Game.Components;
using AlloyClient.Networking;
using Alloy.Engine;
using Alloy.UiLib.Core;
using AlloyClient.Game.Components.Hud;
using AlloyClient.Game.Components.Hud.Chat;
using AlloyClient.Game.Components.Options;
using AlloyClient.Loading;
using AlloyClient.Rendering;
using AlloyClient.Ui.Character;
using AlloyClient.Ui.Chat;
using AlloyClient.Ui.Components.Elements;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;

namespace AlloyClient.Game;

public sealed class GameScreen : Screen {

    public const double FixedUpdateStep = 1d / 60;

    public static GameScreen GameSprite;
    
    private readonly UserInput _userInput;
    private readonly ChatLayer _chatLayer;
    private readonly NotificationLayer _notificationLayer;
    private readonly HudView _hud;
    private readonly ChatBox _chat;
    private readonly DebugStats _debugStats;
    private readonly WorldLoadCover _cover;

    private double _fixedUpdateElapsed;
    private Camera _camera;
    private bool _diagCameraLogged;

    public GameScreen() {
        // The first entry into a world: the cover (added last below, so it sits on top) stays until every milestone in
        // WorldLoad has really happened - connecting, map info, character accepted, player spawned, first frame.
        WorldLoad.Begin(false);
        Client.Connect(Settings.GameServerAddress, Settings.SelectedGameServerPort);
        
        AddChild(_userInput = new UserInput()); // add map as param
        AddChild(_chatLayer = new ChatLayer());
        AddChild(_notificationLayer = new NotificationLayer());
        AddChild(_hud = new HudView());
        AddChild(_chat= new ChatBox());
        AddChild(_debugStats = new DebugStats());
        AddChild(_cover = new WorldLoadCover());

        // The FPS / memory readout is off until the DEV tab (or F5) turns it on; the MENU tab does what the options hotkey does.
        _debugStats.Visible = false;
        _hud.OnOpenMenu = OpenOptions;
        _hud.OnDevChanged = OnDevChanged;
        _cover.Begin(false);
        
        GameSprite = this;
    }

    // A world switch (Reconnect): fade the cover back in while the new world loads.
    public void OnWorldLoadBegan(bool switching) => _cover.Begin(switching);

    public void CreatePlayerDependentAssets() => _hud.CreatePlayerDependentAssets();

    // True if a screen position (pixels) is over a piece of the HUD - a click there must not fire the weapon.
    public bool IsOverHud(int pixelX, int pixelY) => _hud.IsOver(pixelX, pixelY, Stage.ScreenScale.X);

    // Opening the options closes every tab-controlled thing first (popups, the DEV readout); the menu then locks the screen.
    public void OpenOptions() {
        _hud.CloseTabs();
        _userInput.ClearMovement();
        UserInput.SetManualFocus(false);
        OverlayManager.Set(new OptionsView());
    }

    public void ToggleDebugStats() => _hud.ToggleTab("dev");

    private void OnDevChanged(bool on) {
        _debugStats.Visible = on;
        PlaceDebugStats();
    }

    // The FPS / memory readout goes in the bottom-right corner, flush with both edges (its text is static-width, so it doesn't drift as
    // the numbers change), well out of the way of the action.
    private void PlaceDebugStats() {
        var scale = Stage.ScreenScale;
        _debugStats.Scale = scale;
        var pad = (int) (6 * scale.X);   // a little room for the text outline
        _debugStats.X = Stage.StageWidth - _debugStats.Width - pad;
        _debugStats.Y = Stage.StageHeight - _debugStats.Height - pad;
    }

    public void SetChatVisible(bool visible) => _chat.Visible = visible;

    public void SetChatScale(float scale) => _chat.Scale = new Vector2(scale);

    public override void Update(GameTime gameTime) {
        Client.Tick();
        
        if (Map.LocalPlayer is null) {
            return;
        }
        
        _camera = Camera.Update(Map.LocalPlayer.Position, new Vector3i(Stage.StageWidth, Stage.StageHeight, 0), Settings.CameraAngle, Settings.CameraZoom);

        if (!_diagCameraLogged) {
            _diagCameraLogged = true;
            Client.Logger.Log(LogLevel.Debug,
                $"[DIAG] camera pos={Map.LocalPlayer.Position} viewport=({Stage.StageWidth},{Stage.StageHeight}) " +
                $"angle={Settings.CameraAngle} zoom={Settings.CameraZoom} matrix={_camera.Matrix} visibleTiles={_camera.VisibleTileRadius}");
        }

        _userInput.Update(gameTime, _camera);
        _chatLayer.Update(gameTime, _camera);
        _notificationLayer.Update(gameTime, _camera);
        _hud.Update();
        if (_debugStats.Visible) {
            _debugStats.Update(gameTime);
        }

        _fixedUpdateElapsed += gameTime.ElapsedMs;

        while (_fixedUpdateElapsed > FixedUpdateStep) {
            _fixedUpdateElapsed -= FixedUpdateStep;
            Map.FixedUpdate(new GameTime(gameTime.TotalMs, FixedUpdateStep));
        }
        
        Map.Update(gameTime, _camera);
        PartyData.Update(gameTime.TotalMs);
    }

    public override void Draw(GameTime gameTime) {
        Render.SetShaderParams(gameTime, _camera);
        Map.Draw(gameTime, _camera);
        MinimapTexture.PreDrawUpdate();

        if (Map.LocalPlayer is not null) {
            WorldLoad.Mark(WorldMilestone.FirstFrame);
        }
    }

    protected override void OnResize(ResizeEvent args) {
        var width = args.Width;
        var height = args.Height;
        
        // The HUD fills the whole screen (its pieces hug the corners), scaled like everything else; it lays itself out in design units.
        var scale = Stage.ScreenScale;
        _hud.X = 0;
        _hud.Y = 0;
        _hud.Scale = scale;
        _hud.Layout((int) (width / scale.X), (int) (height / scale.Y));

        // Bottom-left is the equipment / inventory now, so the chat moves to the bottom-right corner.
        _chat.X = (int) (width - ChatBox.MaxWidth * scale.X);
        _chat.Y = height;
        _chat.Scale = scale;

        PlaceDebugStats();

        _cover.Resize(width, height);
    }
}