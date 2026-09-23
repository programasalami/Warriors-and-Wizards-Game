using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AlloyClient.Data;
using Alloy.Common;
using AlloyClient.Logging;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace AlloyClient.Core;

public static class Settings {
    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(Settings));

    private const string LocalFolderName = "AlloyClient";
    private const string AccountFileName = "account.xml";
    private const string SettingsFileName = "settings.xml";

    private static readonly string AccountFilePath;
    private static readonly string SettingsFilePath;

    public const string BuildVersion = "0.3.7";
    public const string BuildLabel = $"Alloy v{BuildVersion}";

    // Where the servers are. In the everyday source this is your own PC; `deploy` builds the public client with -p:DeployTarget=vps, which defines
    // TARGET_VPS and swaps in the VPS address (see deploy.ps1). Never edit these back and forth by hand.
#if TARGET_VPS
    public const string AppEngineAddress = "104.152.50.196";
#else
    public const string AppEngineAddress = "127.0.0.1";
#endif
    public const string AppEnginePort = "8080";
    public const string AppEngineUrl = $"http://{AppEngineAddress}:{AppEnginePort}";

    public const int AppEngineTimeout = 10000;

#if TARGET_VPS
    public const string GameServerAddress = "104.152.50.196";
#else
    public const string GameServerAddress = "127.0.0.1";
#endif
    public const ushort GameServerPort = 2050;

    public const int DefaultScreenWidth = 1280;
    public const int DefaultScreenHeight = 720;

    public const float MinCameraZoom = 0.5f;
    public const float MaxCameraZoom = 5;

    public static Vector2i ScreenSize;

    #region HOTKEYS

    // Movement
    public static readonly InputSetting MoveUp = new(Scancode.W);
    public static readonly InputSetting MoveDown = new(Scancode.S);
    public static readonly InputSetting MoveLeft = new(Scancode.A);
    public static readonly InputSetting MoveRight = new(Scancode.D);

    // Camera
    public static readonly InputSetting RotateLeft = new(Scancode.Q);
    public static readonly InputSetting RotateRight = new(Scancode.E);
    // Defaults since 2026-09-22 (the user's picks): Z centres the player, X resets the camera angle, O opens the options, F escapes to the Nexus,
    // C drinks a health potion (F used to; Escape used to open the options - the in-game windows still close on Escape as well).
    public static readonly InputSetting ResetCameraAngle = new(Scancode.X);
    public static readonly InputSetting CenterPlayerKey = new(Scancode.Z);

    // Key
    public static readonly InputSetting Options = new(Scancode.O);
    public static readonly InputSetting AutoFire = new(Scancode.I);
    public static readonly InputSetting Special = new(Scancode.Spacebar);
    public static readonly InputSetting Interact = new(Scancode.D0);
    public static readonly InputSetting Escape = new(Scancode.F);

    // Chat
    public static readonly InputSetting Chat = new(Scancode.Return);
    public static readonly InputSetting ChatCommand = new(Scancode.QuestionMark);
    public static readonly InputSetting TellKey = new(Scancode.Tab);
    public static readonly InputSetting GuildChat = new(Scancode.G);
    public static readonly InputSetting PartyChat = new(Scancode.P);
    public static readonly InputSetting ChatHistoryUp = new(Scancode.PageUp);
    public static readonly InputSetting ChatHistoryDown = new(Scancode.PageDown);

    // Inventory
    public static readonly InputSetting HealthPotion = new(Scancode.C);
    public static readonly InputSetting MagicPotion = new(Scancode.V);
    public static readonly InputSetting InvOne = new(Scancode.D1);
    public static readonly InputSetting InvTwo = new(Scancode.D2);
    public static readonly InputSetting InvThree = new(Scancode.D3);
    public static readonly InputSetting InvFour = new(Scancode.D4);
    public static readonly InputSetting InvFive = new(Scancode.D5);
    public static readonly InputSetting InvSix = new(Scancode.D6);
    public static readonly InputSetting InvSeven = new(Scancode.D7);
    public static readonly InputSetting InvEight = new(Scancode.D8);

    // Misc
    public static readonly InputSetting PerformanceStats = new(Scancode.F5);
    public static readonly InputSetting SwitchTabs = new(Scancode.B);
    public static readonly InputSetting ResetMScale = new(Scancode.Unknown);
    public static readonly InputSetting SetBagPriority = new(Scancode.Unknown);
    public static readonly InputSetting FullscreenKey = new(Scancode.F11);
    public static readonly InputSetting ToggleStatusBars = new(Scancode.H);   // shows / hides the small HP and MP bars under the player's own character

    #endregion

    #region VALUES

    // Random
    public static readonly ValueSetting<PacketLogLevel> PacketLogging = new(PacketLogLevel.Off);
    public static readonly ValueSetting<ushort> SelectedGameServerPort = new(GameServerPort);
    // Id of the character the player last pressed PLAY with - shown on the character book's "LAST PLAYED" page.
    public static readonly ValueSetting<int> LastPlayedCharacterId = new(-1);

    // The music on the title screen and in the book: a file in Content/Sound/Music. Every client starts with Main_Music; the settings page will
    // let a player pick another (Menu_Music* files are meant for that). Never part of the in-game library.
    public static readonly ValueSetting<string> MenuMusic = new("Main_Music.wav");

    // Where PLAY puts the character: a FastTravel.Destinations key ("Nexus", "Vault", "GuildHall"), chosen on the Character Book's FAST TRAVEL page.
    public static readonly ValueSetting<string> FastTravel = new(Data.FastTravel.DefaultKey);

    // Camera
    public static readonly ValueSetting<int> MaxRenderDistance = new(20);
    public static readonly ValueSetting<float> CameraAngle = new(0f);
    public static readonly ValueSetting<float> CameraZoom = new(1f);
    public static readonly ValueSetting<bool> AllowRotation = new(true);
    // Free-form by default (hold Q/E for a smooth continuous spin, RotateSpeed radians per ms); the 45-degree snap stays available in Options.
    public static readonly ValueSetting<bool> SnapRotation = new(false);
    public static readonly ValueSetting<float> RotateSpeed = new(0.003f);

    // Screen
    public static readonly ValueSetting<int> FpsCap = new(-1);
    // ON by default: with it off the client runs uncapped and unsynced, which shows as screen tearing that looks like GUI flicker (worst on the weak Intel HD 4400).
    public static readonly ValueSetting<bool> VSync = new(true);
    public static readonly ValueSetting<WindowMode> LastWindowMode = new(WindowMode.Normal);
    public static readonly ValueSetting<int> LastWindowPositionX = new(0);
    public static readonly ValueSetting<int> LastWindowPositionY = new(0);
    public static readonly ValueSetting<int> LastWindowWidth = new(DefaultScreenWidth);
    public static readonly ValueSetting<int> LastWindowHeight = new(DefaultScreenHeight);
    public static readonly ValueSetting<FullscreenType> FullscreenMode = new(FullscreenType.Borderless);
    public static readonly ValueSetting<bool> FullscreenState = new(false);

    // Your own marker on the minimap (Extra tab): a green square by default, bigger than the other markers (MinimapIcon.cs).
    public static readonly ValueSetting<MinimapIconShape> MinimapIconShape = new(Core.MinimapIconShape.Square);
    public static readonly ValueSetting<MinimapIconColor> MinimapIconColor = new(Core.MinimapIconColor.Green);

    // Audio
    public static readonly ValueSetting<float> MasterVolume = new(0.5f);
    public static readonly ValueSetting<float> MusicVolume = new(1f);
    public static readonly ValueSetting<float> SfxVolume = new(1f);
    public static readonly ValueSetting<bool> PlayMaster = new(true);
    public static readonly ValueSetting<bool> PlayMusic = new(true);
    public static readonly ValueSetting<bool> PlaySfx = new(true);

    // Chat
    public static readonly ValueSetting<int> ChatInclude = new(0);
    public static readonly ValueSetting<bool> ChatVisible = new(true);
    public static readonly ValueSetting<float> ChatScaling = new(0f);
    public static readonly ValueSetting<int> ChatHideList = new(0);

    // Particles
    public static readonly ValueSetting<bool> EyeCandyParticles = new(true);
    public static readonly ValueSetting<bool> ReducedParticles = new(false);

    // Other
    public static readonly ValueSetting<bool> ToggleLeftToMax = new(true);
    public static readonly ValueSetting<bool> ToggleBarText = new(true);
    public static readonly ValueSetting<bool> ShowStatusBars = new(true);

    // "Player Position" in the options: Centered (the character in the exact middle of the screen) or Lowered (the camera looks LowerViewTiles ahead of
    // the character in the direction the camera faces, so the character sits lower on the screen and more of what is in front is visible - for
    // playing with rotation + movement together). The offset turns with the camera, so "ahead" always means "up the screen".
    // The CenterPlayerKey hotkey (Z) flips it (UserInput). The old "Center On Player" on/off option and its CenterPlayer setting were dead (nothing read
    // them) and were removed on 2026-09-22; a stale <CenterPlayer> tag in an old settings.xml is ignored.
    public static readonly ValueSetting<bool> LowerPlayerView = new(false);
    public const float LowerViewTiles = 2.5f;
    public static readonly ValueSetting<bool> InventorySwap = new(true);
    public static readonly ValueSetting<bool> MovementInterpolation = new(true);

    #endregion

    public static readonly InputSetting[] Inputs;

    private static readonly ReadOnlyDictionary<string, ISettingType> SettingsLookup;

    static Settings() {
        var localFolderPath = Path.CombineAlt(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LocalFolderName);
        Directory.CreateDirectory(localFolderPath);

        AccountFilePath = Path.CombineAlt(localFolderPath, AccountFileName);
        SettingsFilePath = Path.CombineAlt(localFolderPath, SettingsFileName);

        SettingsLookup = typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => typeof(ISettingType).IsAssignableFrom(field.FieldType))
            .Select(field => (field.Name, (ISettingType) field.GetValue(null)))
            .ToDictionary().AsReadOnly();

        Inputs = SettingsLookup.Select(pair => pair.Value).OfType<InputSetting>().ToArray();
    }

    public static void ResetToDefault() {
        foreach (var (key, setting) in SettingsLookup) {
            setting.ResetToDefault();
        }
    }

    public static float GetMasterVolume() => PlayMaster ? MasterVolume : 0;

    public static float GetMusicVolume() => PlayMusic ? MusicVolume : 0;

    public static float GetSfxVolume() => PlaySfx ? SfxVolume : 0;

    #region SettingParsing

    public static void LoadSettings() {
        LoadLocalAccount();
        try {
            TryLoadSettings();
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, $"Error loading settings: {e.Message}");
        }
        SaveSettings();
    }

    private static void TryLoadSettings() {
        if (!File.Exists(SettingsFilePath)) {
            Logger.Log(LogLevel.Trace, "Settings file not found.");
            return;
        }

        var settingsXml = new XmlDocument();
        settingsXml.LoadXml(File.ReadAllText(SettingsFilePath));

        var settingsRoot = settingsXml.DocumentElement;
        if (settingsRoot == null) {
            Logger.Log(LogLevel.Warning, "Settings file is empty.");
            return;
        }

        var count = 0;
        foreach (var (key, setting) in SettingsLookup) {
            var tag = settingsRoot[key];

            if (tag == null) {
                continue;
            }

            try {
                setting.Deserialize(tag.InnerText);
                count++;
            } catch (Exception e) {
                Logger.Log(LogLevel.Warning, $"Error loading setting {key}: {e.Message}");
            }
        }

        Logger.Log(LogLevel.Trace, $"Loaded {count} of {SettingsLookup.Count} settings, {SettingsLookup.Count - count} reset to default");
    }

    public static void SaveSettings() {
        var xml = new XmlDocument();
        var root = xml.CreateElement("Settings");
        xml.AppendChild(root);

        var count = 0;
        foreach (var (key, setting) in SettingsLookup) {
            var tag = xml.CreateElement(key);
            try {
                tag.InnerText = setting.Serialize();
            } catch (Exception e) {
                Logger.Log(LogLevel.Warning, $"Error saving setting {key}: {e.Message}");
                continue;
            }
            root.AppendChild(tag);
            count++;
        }

        try {
            xml.Save(SettingsFilePath);
            Logger.Log(LogLevel.Trace, $"Saved {count} of {SettingsLookup.Count} settings");
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, $"Failed to save settings: {e}");
        }
    }

    #endregion

    #region LocalAccountParsing

    public static void LoadLocalAccount() {
        if (!File.Exists(AccountFilePath)) {
            Logger.Log(LogLevel.Debug, "No local account data found");
            return;
        }

        string text;
        try {
            text = File.ReadAllText(AccountFilePath);
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, $"Failed to read to file {AccountFilePath}: {e.Message}");
            return;
        }

        var xml = XDocument.Parse(text);
        var info = xml.Root?.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);

        if (info == null) {
            Logger.Log(LogLevel.Debug, "Failed to parse local account data");
            return;
        }

        var loadedUser = info.TryGetValue("Username", out var username) && !string.IsNullOrWhiteSpace(username);
        var loadedPass = info.TryGetValue("Password", out var password) && !string.IsNullOrWhiteSpace(password);

        if (username == string.Empty && password == string.Empty) {
            Logger.Log(LogLevel.Debug, "No local account data");
            return;
        }

        if (!loadedUser || !loadedPass) {
            Logger.Log(LogLevel.Debug, "Incomplete/Invalid local account data");
            return;
        }

        var data = new byte[(password.Length * 3 + 3) / 4];

        if (!Convert.TryFromBase64String(password, data.AsSpan(), out var count)) {
            Logger.Log(LogLevel.Error, "Invalid Base64 encoding on password");
            return;
        }

        GlobalData.Add(new LoginData(username, Encoding.UTF8.GetString(data.AsSpan(0, count))));
    }

    public static void SaveLocalAccount() {
        var data = GlobalData.Get<LoginData>() ?? LoginData.Default;

        var bytes = new byte[Encoding.UTF8.GetByteCount(data.Password.AsSpan())];
        if (!Encoding.UTF8.TryGetBytes(data.Password.AsSpan(), bytes.AsSpan(), out var byteCount)) {
            Logger.Log(LogLevel.Error, "Failed to get password bytes");
            return;
        }

        var chars = new char[4 * (data.Password.Length + 2) / 3];
        if (!Convert.TryToBase64Chars(bytes.AsSpan(0, byteCount), chars.AsSpan(), out var charCount)) {
            Logger.Log(LogLevel.Error, "Failed to Base64 encode password");
            return;
        }

        var username = data.Username;
        var password = new string(chars.AsSpan(0, charCount));

        var tags = new Dictionary<string, string>{{"Username", username}, {"Password", password}};
        var xml = new XDocument(new XElement("Account", tags.Select(kvp => new XElement(kvp.Key, kvp.Value))));

        try {
            xml.Save(AccountFilePath);
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, $"Failed to write to file {AccountFilePath}: {e.Message}");
        }
    }

    #endregion
}

#region SettingTypes

public interface ISettingType {
    string Serialize();
    void Deserialize(string str);
    void ResetToDefault();
}

public class InputSetting(Scancode def = Scancode.Unknown) : ISettingType {

    public Scancode Key { get; private set; } = def;

    private readonly Scancode _default = def;

    public void Set(Scancode key) => Key = key;

    public bool Equals(Scancode key) => key == Key;

    public string Serialize() => $"{Key}";

    public void Deserialize(string str) => Key = Enum.Parse<Scancode>(str);

    public void ResetToDefault() {
        Key = _default;
    }

    public override string ToString() => $"{Key}";
}

public class ValueSetting<T>(T def = default) : ISettingType {

    public T Value = def;

    private readonly T _default = def;

    public string Serialize() {
        if (!IsNumericType<T>() && !typeof(T).IsEnum && typeof(T) != typeof(string) && typeof(T) != typeof(char)) {
            throw new NotSupportedException($"Type '{typeof(T).Name}' not supported");
        }
        return $"{Value}";
    }

    public void Deserialize(string str) {
        if (typeof(T).IsEnum) {
            Value = (T) Enum.Parse(typeof(T), str);
        } else if (typeof(T) == typeof(string)) {
            Value = (T) (object) str;
        } else if (typeof(T) == typeof(char)) {
            Value = (T) (object) str[0];
        } else if (IsNumericType<T>()) {
            Value = (T) Convert.ChangeType(str, typeof(T));
        } else {
            throw new NotSupportedException($"Type '{typeof(T).Name}' not supported");
        }
    }

    public T Get() => Value;

    public void Set(T value) => Value = value;

    public void ResetToDefault() => Value = _default;

    public static implicit operator T(ValueSetting<T> valueSetting) => valueSetting.Value;

    private static bool IsNumericType<TValue>() => Type.GetTypeCode(typeof(TValue)) switch { TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => true, _ => false };

    public override string ToString() => $"{Value}";
}

#endregion