using System;
using System.Collections.Generic;
using AlloyClient.Core;
using AlloyClient.Game.Components.Options.OptionTypes;
using AlloyClient.Ui.Components.Scrollbars;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using OpenTK.Platform;

namespace AlloyClient.Game.Components.Options;

public class OptionTabView : Container {
    private static readonly string[] OnOffLabels = ["On", "Off"];
    private static readonly object[] OnOffValues = [true, false];
    private static readonly string[] WindowLabels = ["Windowed", "Maximized", "Borderless", "Fullscreen"];
    private static readonly object[] WindowValues = [WindowMode.Normal, WindowMode.Maximized, WindowMode.WindowedFullscreen, WindowMode.ExclusiveFullscreen];

    private const int RowHeight = 56;

    private readonly Container _container;
    private readonly List<Option> _options = [];
    // Section headers (used by the Extra tab, 2026-09-22): the header text and the index of the first option under it.
    private readonly List<(string Title, int FirstOption)> _sections = [];
    private const int SectionHeaderHeight = 44;

    private void Section(string title) => _sections.Add((title, _options.Count));
    private readonly VerticalScrollBar _scrollbar;

    public OptionTabView(string name) : base(new ContainerConfig { Width = OptionsView.ViewWidth, Height = OptionsView.ViewHeight, EnableClip = true }) {
        _container = new Container();
        AddChild(_container);

        switch (name) {
            case OptionsView.ControlsTab:
                AddControlsOptions();
                break;
            case OptionsView.HotkeysTab:
                AddHotkeysOptions();
                break;
            case OptionsView.ChatTab:
                AddChatOptions();
                break;
            case OptionsView.GraphicsTab:
                AddGraphicsOptions();
                break;
            case OptionsView.SoundTab:
                AddSoundOptions();
                break;
            case OptionsView.ExtraTab:
                AddExtraOptions();
                break;
        }

        PositionChildren();

        // The viewport is the fixed ViewHeight, NOT this.Height: a sprite's Height grows to include its children, so once the
        // options overflow, this.Height == _container.Height and the scrollbar got a scroll range of 0 (drag -> divide by zero ->
        // the options were flung off-screen for good).
        if (_container.Height > OptionsView.ViewHeight) {
            _scrollbar = new VerticalScrollBar(this, new VerticalScrollBarConfig {
                X = OptionsView.ViewWidth - 30,
                Width = 15,
                Height = OptionsView.ViewHeight,
                TotalContentHeight = _container.Height,
                VisibleContentHeight = OptionsView.ViewHeight,
                OnValueChanged = val => _container.Y = -val
            });
            AddChild(_scrollbar);
        }
    }

    private void PositionChildren() {
        var y = 8;          // top of the current row
        var column = 0;     // 0 = left, 1 = right
        var section = 0;
        for (var index = 0; index < _options.Count; index++) {
            if (section < _sections.Count && _sections[section].FirstOption == index) {
                if (column == 1) {           // a half-filled row is closed before a new section
                    y += RowHeight;
                    column = 0;
                }
                AddSectionHeader(_sections[section].Title, y);
                y += SectionHeaderHeight;
                section++;
            }

            var option = _options[index];
            if (option == null) {
                continue;
            }

            option.X += column == 0 ? 48 : 628;
            option.Y += y;
            _container.AddChild(option);

            if (column == 1) {
                y += RowHeight;
                column = 0;
            } else {
                column = 1;
            }
        }
    }

    // A gold title across both columns with a faint rule under it.
    private void AddSectionHeader(string title, int y) {
        _container.AddChild(OptionsStyle.Label(title.ToUpperInvariant(), FontGroup.MyriadPro, 22f, 48, y + 6, UiAnchor.LeftTop, OptionsStyle.Gold, 1));
        _container.AddChild(new ColorRect(new ColorRectConfig { X = 48, Y = y + 34, Width = OptionsView.ViewWidth - 120, Height = 1, Color = OptionsStyle.Tan, Alpha = 0.45f }));
    }

    public void Refresh() {
        foreach (var option in _options) {
            option?.Refresh();
        }
    }

    private void AddControlsOptions() {
        _options.Add(new KeyMapperOption(Settings.MoveUp, "Move Up", "Key to will move character up"));
        _options.Add(new KeyMapperOption(Settings.MoveLeft, "Move Left", "Key to will move character to the left"));
        _options.Add(new KeyMapperOption(Settings.MoveDown, "Move Down", "Key to will move character down"));
        _options.Add(new KeyMapperOption(Settings.MoveRight, "Move Right", "Key to will move character to the right"));
        _options.Add(new ChoiceOption<bool>(Settings.ShowStatusBars, OnOffLabels, OnOffValues, "Show HP/MP Bars", "Shows or hides the small HP and MP bars under your character (hotkey: Toggle HP/MP Bars)"));
        _options.Add(new ChoiceOption<bool>(Settings.AllowRotation, OnOffLabels, OnOffValues, "Allow Camera Rotation", "Toggles whether to allow for camera rotation"));
        _options.Add(null);
        _options.Add(new KeyMapperOption(Settings.RotateLeft, "Rotate Left", "Key to will rotate the camera to the left"));
        _options.Add(new KeyMapperOption(Settings.RotateRight, "Rotate Right", "Key to will rotate the camera to the right"));
        _options.Add(new KeyMapperOption(Settings.Special, "Use Special Ability", "This key will activate your special ability"));
        _options.Add(new KeyMapperOption(Settings.AutoFire, "Autofire Toggle", "This key will toggle autofire"));
        _options.Add(new KeyMapperOption(Settings.ResetCameraAngle, "Reset To Default Camera Angle", "This key will reset the camera angle to the default position"));
        _options.Add(new KeyMapperOption(Settings.ToggleStatusBars, "Toggle HP/MP Bars", "This key shows or hides the small HP and MP bars under your character"));
        _options.Add(new KeyMapperOption(Settings.Interact, "Interact/Buy", "This key will allow you to enter a portal or buy an item without using your mouse."));
    }

    private void AddHotkeysOptions() {
        _options.Add(new KeyMapperOption(Settings.HealthPotion, "Use Health Potion", "This key will use health potions if available"));
        _options.Add(new KeyMapperOption(Settings.MagicPotion, "Use Magic Potion", "This key will use magic potions if available"));
        _options.Add(new KeyMapperOption(Settings.InvOne, "Use Inventory Slot 1", "Use item in inventory slot 1"));
        _options.Add(new KeyMapperOption(Settings.InvTwo, "Use Inventory Slot 2", "Use item in inventory slot 2"));
        _options.Add(new KeyMapperOption(Settings.InvThree, "Use Inventory Slot 3", "Use item in inventory slot 3"));
        _options.Add(new KeyMapperOption(Settings.InvFour, "Use Inventory Slot 4", "Use item in inventory slot 4"));
        _options.Add(new KeyMapperOption(Settings.InvFive, "Use Inventory Slot 5", "Use item in inventory slot 5"));
        _options.Add(new KeyMapperOption(Settings.InvSix, "Use Inventory Slot 6", "Use item in inventory slot 6"));
        _options.Add(new KeyMapperOption(Settings.InvSeven, "Use Inventory Slot 7", "Use item in inventory slot 7"));
        _options.Add(new KeyMapperOption(Settings.InvEight, "Use Inventory Slot 8", "Use item in inventory slot 8"));
        _options.Add(new KeyMapperOption(Settings.Escape, "Escape To Nexus", "This key will instantly escape you to the Nexus"));
        _options.Add(new KeyMapperOption(Settings.Options, "Show Options", "This key will bring up the options screen"));//TODO: force this to be disabled to prevent changing it
        _options.Add(new KeyMapperOption(Settings.SwitchTabs, "Switch Tabs", "This key will switch from available tabs"));
    }

    private void AddChatOptions() {
        _options.Add(new KeyMapperOption(Settings.Chat, "Activate Chat", "This key will bring up the chat input box"));
        _options.Add(new KeyMapperOption(Settings.ChatCommand, "Start Chat Command",
            "This key will bring up the chat with a \\'/\\' prepended to \" + \"allow for commands such as /who, /ignore, etc."));
        _options.Add(new KeyMapperOption(Settings.TellKey, "Start Chat Command", "This key will bring up a tell in the chat input box"));
        _options.Add(new ChoiceOption<int>(Settings.ChatInclude, ["None", "Guild", "Party", "GP"], [0, 1, 2, 3], "Include In Begin Tell",
            "This key will include the chat in the chat input box"));
        _options.Add(new KeyMapperOption(Settings.GuildChat, "Begin Guild Chat", "This key will bring up a guild chat in the chat input box"));
        _options.Add(new KeyMapperOption(Settings.PartyChat, "Begin Party Chat", "This key will bring up a party chat in the chat input box"));
        _options.Add(new ChoiceOption<bool>(Settings.ChatVisible, OnOffLabels, OnOffValues, "Chat visible", "Turn chat visibility ON/OFF.", OnChatVisible));
        _options.Add(new ChoiceOption<float>(Settings.ChatScaling, ["100%", "90%", "80%", "70%", "60%", "50%"], [1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f], "Chat Scale",
            "Click here to change the scale of the chat", OnChatBoxScale));
        _options.Add(new KeyMapperOption(Settings.ChatHistoryUp, "Navigate Chat History Up", "Navigate previous messages"));
        _options.Add(new KeyMapperOption(Settings.ChatHistoryDown, "Navigate Chat History Down", "Navigate next messages"));
        _options.Add(new ChoiceOption<int>(Settings.ChatHideList, ["All", "None", "Locked", "Guild", "Party", "GPL"], [0, 1, 2, 3, 4, 5], "Show Player Messages",
            "Choose which players messages should be shown to you. This includes whispers and global chat."));
    }

    private void AddGraphicsOptions() {
        _options.Add(new ChoiceOption<bool>(Settings.EyeCandyParticles, OnOffLabels, OnOffValues, "Eye Candy Particles", "This toggles whether to show eye candy particles, disabling this will improve performance."));
        _options.Add(new ChoiceOption<bool>(Settings.ReducedParticles, OnOffLabels, OnOffValues, "Reduced Particles", "This toggles whether to show reduced particles, enabling this will improve performance."));
        _options.Add(new ChoiceOption<int>(Settings.FpsCap, ["30", "60", "90", "120", "144", "165", "240", "300", "360", "None"], [30, 60, 90, 120, 144, 165, 240, 300, 360, -1], "FPS Cap", "This allows you to choose a frame rate cap", OnFPSChange));
        _options.Add(new ChoiceOption<int>(Settings.MaxRenderDistance, ["Low", "Medium", "High", "Max"], [15, 20, 25, 60], "Max Render Distance", "Pick the maximum render distance of your client. Can improve performance greatly."));
        _options.Add(new ChoiceOption<float>(Settings.CameraZoom, ["100%", "90%", "80%", "70%", "60%", "50%", "200%", "150%"], [1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 2f, 1.5f], "Zoom", "Zooms your game in and out so you can see more or less things at once. You can also use the /mscale command or use Shift + Scroll. (Available options: 100%, 90%, 80%, 70%, 60%, 50%, 200%, 150%)"));
        _options.Add(new ChoiceOption<bool>(Settings.VSync, OnOffLabels, OnOffValues, "VSync", "This toggles whether to have VSync enabled or not.", OnVSyncToggle));
    }

    private void AddSoundOptions() {
        _options.Add(new ChoiceOption<bool>(Settings.PlayMaster, OnOffLabels, OnOffValues, "Play Master", "This toggles whether all sound is played", OnPlayMasterChange));
        _options.Add(new SliderOption(Settings.MasterVolume, "Vol:", OnMasterVolumeChange));
        _options.Add(new ChoiceOption<bool>(Settings.PlayMusic, OnOffLabels, OnOffValues, "Play Music", "This toggles whether music is played", OnPlayMusicChange));
        _options.Add(new SliderOption(Settings.MusicVolume, "Vol:", OnMusicVolumeChange));
        _options.Add(new ChoiceOption<bool>(Settings.PlaySfx, OnOffLabels, OnOffValues, "Play Sound Effects", "This toggles whether sound effects are played", OnPlaySoundEffectsChange));
        _options.Add(new SliderOption(Settings.SfxVolume, "Vol:", OnSfxVolumeChange));
    }

    // The experimental corner (2026-09-22). First: what marks YOU on the minimap. A later idea: emoji icons from a library the client ships.
    // The experimental corner (2026-09-22), grouped: the tab scrolls when the sections run past the window. A later idea: emoji minimap icons.
    private void AddExtraOptions() {
        Section("Minimap");
        _options.Add(new ChoiceOption<MinimapIconShape>(Settings.MinimapIconShape, ["Square", "Circle", "Diamond", "Triangle"],
            [MinimapIconShape.Square, MinimapIconShape.Circle, MinimapIconShape.Diamond, MinimapIconShape.Triangle],
            "Minimap Icon Shape", "The shape that marks you on the minimap."));
        _options.Add(new ChoiceOption<MinimapIconColor>(Settings.MinimapIconColor, ["Green", "Blue", "Red", "Yellow", "White", "Orange", "Purple", "Cyan", "Pink"],
            [MinimapIconColor.Green, MinimapIconColor.Blue, MinimapIconColor.Red, MinimapIconColor.Yellow, MinimapIconColor.White, MinimapIconColor.Orange, MinimapIconColor.Purple, MinimapIconColor.Cyan, MinimapIconColor.Pink],
            "Minimap Icon Colour", "The colour of your marker on the minimap."));
        _options.Add(new KeyMapperOption(Settings.ResetMScale, "Reset Map Scale", "Resets your map scale to default."));

        Section("Camera");
        _options.Add(new ChoiceOption<float>(Settings.CameraAngle, ["45", "0"], [7 * MathF.PI, 0f], "Default Camera Angle", "This toggles the default camera angle"));
        _options.Add(new ChoiceOption<bool>(Settings.SnapRotation, OnOffLabels, OnOffValues, "Snap Camera Rotation", "Rotate in eased 45 degree steps instead of spinning continuously"));
        _options.Add(new ChoiceOption<bool>(Settings.LowerPlayerView, ["Lowered", "Centered"], [true, false], "Player Position", "Lowered puts your character below the middle of the screen so you see further ahead; Centered keeps it in the exact middle"));
        _options.Add(new KeyMapperOption(Settings.CenterPlayerKey, "Toggle Player Position", "This key switches Player Position between Centered and Lowered"));

        Section("Display");
        _options.Add(new KeyMapperOption(Settings.FullscreenKey, "Fullscreen", "Toggles fullscreen mode."));
        _options.Add(new ChoiceOption<FullscreenType>(Settings.FullscreenMode, ["Exclusive", "Borderless"], [FullscreenType.Exclusive, FullscreenType.Borderless], "Fullscreen type", "Changes which type fullscreen uses", OnWindowModeChange));
        _options.Add(new KeyMapperOption(Settings.PerformanceStats, "Toggle Performance Stats", "This key will toggle a display of fps and memory usage"));

        Section("Items");
        _options.Add(new ChoiceOption<bool>(Settings.InventorySwap, OnOffLabels, OnOffValues, "Switch item to/from backpack", "Hold the Ctrl key and click on an item to swap it between your inventory and your backpack."));
        _options.Add(new KeyMapperOption(Settings.SetBagPriority, "Bag priority", "Toggle whether to make bags easier to interact with or not."));
    }

    private void OnPlayMasterChange() {
        Audio.SetMasterVolume(Settings.GetMasterVolume());
    }

    private void OnMasterVolumeChange(float obj) {
        Settings.MasterVolume.Set(obj);
        Audio.SetMasterVolume(Settings.GetSfxVolume());
    }

    private void OnPlaySoundEffectsChange() {
        Audio.SfxChannel.SetVolume(Settings.GetSfxVolume());
    }

    private void OnSfxVolumeChange(float obj) {
        Settings.SfxVolume.Set(obj);
        Audio.SfxChannel.SetVolume(Settings.GetSfxVolume());
    }

    private void OnPlayMusicChange() {
        Audio.MusicChannel.SetVolume(Settings.GetMusicVolume());
    }

    private void OnMusicVolumeChange(float obj) {
        Settings.MusicVolume.Set(obj);
        Audio.MusicChannel.SetVolume(Settings.GetMusicVolume());
    }

    private void OnChatVisible() {
        GameScreen.GameSprite?.SetChatVisible(Settings.ChatVisible.Value);
    }

    private void OnChatBoxScale() {
        GameScreen.GameSprite?.SetChatScale(Settings.ChatScaling.Value);
    }

    private void OnVSyncToggle() {
        Main.OnScreenChange.Dispatch(ScreenType.Game);
        var option = GetOption(Settings.FpsCap);
        option.SetDisabled(Settings.VSync);
    }

    private void OnFPSChange() {
        Main.OnScreenChange.Dispatch(ScreenType.Game);
    }

    private void OnWindowModeChange() {
        Main.OnFullscreenToggle.Dispatch();
    }

    private Option GetOption(ISettingType setting) {
        return _options.Find(option => option.Setting == setting);
    }
}