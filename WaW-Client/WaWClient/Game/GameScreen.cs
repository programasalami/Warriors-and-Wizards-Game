using WaWClient.Display;
using WaWClient.Game.Components;
using WaWClient.Networking;
using WaW.Engine;
using WaW.UiLib.Core;
using WaWClient.Game.Components.Hud;
using WaWClient.Game.Components.Hud.Chat;
using WaWClient.Game.Components.Options;
using WaWClient.Game.Music;
using WaWClient.Loading;
using WaWClient.Rendering;
using WaWClient.Ui.Character;
using WaWClient.Ui.Chat;
using WaWClient.Ui.Components.Elements;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;

namespace WaWClient.Game;

public sealed class GameScreen : Screen {

    // The simulation step in MILLISECONDS (GameTime is in ms). It used to be 1d / 60 - a value in seconds compared against a
    // millisecond accumulator - which ran ~1000 steps per frame; see FixedStepper.
    public const double FixedUpdateStep = FixedStepper.DefaultStepMs;

    public static GameScreen GameSprite;

    private readonly UserInput _userInput;
    private readonly ChatLayer _chatLayer;
    private readonly NotificationLayer _notificationLayer;
    private readonly HudView _hud;
    private readonly ChatBox _chat;
    private readonly DebugStats _debugStats;
    private readonly WorldLoadCover _cover;

    private readonly FixedStepper _fixedStepper = new(FixedUpdateStep, FixedStepper.DefaultMaxStepsPerFrame);
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private Camera _camera;

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
        _hud.OnOpenAdmin = OpenAdmin;
        _hud.OnDevChanged = OnDevChanged;
        _cover.Begin(false);

        GameSprite = this;
    }

    // A world switch (Reconnect): the loading cover goes up at once (no fade, see WorldLoadCover.Begin) while the new world loads.
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

    // The Nexus Bug Board: like the options menu it closes any open tab, stops the player and takes the keyboard while it is open.
    public void OpenBugBoard() {
        _hud.CloseTabs();
        _userInput.ClearMovement();
        UserInput.SetManualFocus(false);
        OverlayManager.Set(new Components.BugBoard.BugBoardView());
    }

    // The Nexus News Board (the patch notes): opens like the Bug Board.
    public void OpenNewsBoard() {
        _hud.CloseTabs();
        _userInput.ClearMovement();
        UserInput.SetManualFocus(false);
        OverlayManager.Set(new Components.News.NewsBoardView());
    }

    // The admin dashboard (ADMIN tab, moderators and owners): opens like the Bug Board - closes the tabs, stops the player, takes the keyboard.
    public void OpenAdmin() {
        _hud.CloseTabs();
        _userInput.ClearMovement();
        UserInput.SetManualFocus(false);
        OverlayManager.Set(new Components.Admin.AdminDashboardView());
    }

    // The Nexus Jukebox: the same way as the Bug Board (closes tabs, stops the player, takes the keyboard while it is open).
    public void OpenJukebox() {
        _hud.CloseTabs();
        _userInput.ClearMovement();
        UserInput.SetManualFocus(false);
        OverlayManager.Set(new Components.Jukebox.JukeboxView());
    }

    public void ToggleDebugStats() => _hud.ToggleTab("dev");

    private void OnDevChanged(bool on) {
        _debugStats.Visible = on;
        PlaceDebugStats();
    }

    // The FPS / memory readout hangs to the LEFT of the minimap, level with its top (the bottom-right corner is the chat's). Its text is
    // static-width, so it doesn't drift as the numbers change.
    private void PlaceDebugStats() {
        var scale = Stage.ScreenScale;
        _debugStats.Scale = scale;
        var gap = (int) (8 * scale.X);   // a little room for the text outline
        _debugStats.X = (int) (_hud.MinimapLeft * scale.X) - _debugStats.Width - gap;
        _debugStats.Y = (int) (_hud.MinimapTop * scale.Y);
    }

    public void SetChatVisible(bool visible) => _chat.Visible = visible;

    public void SetChatScale(float scale) => _chat.Scale = new Vector2(scale);

    public override void Update(GameTime gameTime) {
        PerfCounters.BeginFrame();
        PerfSections.BeginFrame();
        _frameClock.Restart();

        var sect = PerfSections.Begin();
        Client.Tick();
        PerfSections.End(PerfSections.Section.Net, sect);
        InGameMusic.Update();      // the shared in-game playlist (follows the server, crossfades between songs)

        if (Map.LocalPlayer is null) {
            PerfCounters.UpdateMs = _frameClock.Elapsed.TotalMilliseconds;
            return;
        }

        // The camera for this frame's input (mouse -> world) and culling: where the character stood at the end of the last frame.
        _camera = CameraOnPlayer();

        sect = PerfSections.Begin();
        _userInput.Update(gameTime, _camera);
        _chatLayer.Update(gameTime, _camera);
        _notificationLayer.Update(gameTime, _camera);
        _hud.Update();
        if (_debugStats.Visible) {
            _debugStats.Update(gameTime);
        }
        PerfSections.End(PerfSections.Section.Hud, sect);

        // Fixed simulation steps (hit tests, trails): at most a few per frame, never a runaway backlog.
        var fixedSect = PerfSections.Begin();
        var fixedStart = _frameClock.Elapsed.TotalMilliseconds;
        var steps = _fixedStepper.Advance(gameTime.ElapsedMs);
        PerfCounters.FixedStepsThisFrame = steps;
        PerfCounters.FixedStepsDroppedThisFrame = _fixedStepper.LastDropped;
        PerfCounters.FixedStepsDroppedTotal = _fixedStepper.TotalDropped;
        for (var i = 0; i < steps; i++) {
            Map.FixedUpdate(new GameTime(gameTime.TotalMs, FixedUpdateStep));
        }
        PerfCounters.FixedUpdateMs = _frameClock.Elapsed.TotalMilliseconds - fixedStart;
        PerfSections.End(PerfSections.Section.Fixed, fixedSect);

        sect = PerfSections.Begin();
        Map.Update(gameTime, _camera);
        PartyData.Update(gameTime.TotalMs);
        PerfSections.End(PerfSections.Section.MapUpdate, sect);
        Dev.DevPerfTest.Update(gameTime);

        // Place the camera again on the character's position AFTER it moved this frame, so Draw puts the character exactly where the camera
        // looks. Drawing with the camera from before Map.Update left the character one frame of movement (dt x speed) off-centre; with steady
        // frame times that is a constant, invisible offset, but in the browser frame times vary, so the offset changed every frame and the
        // character shifted back and forth a pixel or two while walking (2026-09-22). Camera.Update is pure, so a second call costs nothing.
        _camera = CameraOnPlayer();
        PerfCounters.UpdateMs = _frameClock.Elapsed.TotalMilliseconds;
    }

    // Where the camera looks: the character, or (Player Position = Lowered) a point LowerViewTiles up the screen from it. Screen-up in world
    // coordinates is the same direction the "move up" key walks (see Player.HandleRelativeMovement): (sin angle, -cos angle).
    private Camera CameraOnPlayer() {
        var focus = Map.LocalPlayer.Position;
        if (Settings.LowerPlayerView) {
            float angle = Settings.CameraAngle;
            focus += new Vector2(System.MathF.Sin(angle), -System.MathF.Cos(angle)) * Settings.LowerViewTiles;
        }

        return Camera.Update(focus, new Vector3i(Stage.StageWidth, Stage.StageHeight, 0), Settings.CameraAngle, Settings.CameraZoom);
    }

    public override void Draw(GameTime gameTime) {
        var drawStart = _frameClock.Elapsed.TotalMilliseconds;
        Render.SetShaderParams(gameTime, _camera);
        Map.Draw(gameTime, _camera);
        var sect = PerfSections.Begin();
        MinimapTexture.PreDrawUpdate();
        PerfSections.End(PerfSections.Section.Minimap, sect);

        if (Map.LocalPlayer is not null) {
            WorldLoad.Mark(WorldMilestone.FirstFrame);
        }
        PerfCounters.DrawMs = _frameClock.Elapsed.TotalMilliseconds - drawStart;
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