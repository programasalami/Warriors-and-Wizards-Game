// Entry point + JS <-> C# bridge for the browser build. JavaScript (wwwroot/main.js) downloads the game content into the in-memory file
// system, then calls Start(); after that requestAnimationFrame calls Frame() and the DOM events call the On* methods, which raise the
// same event objects OpenTK would have raised on desktop.
using System.Runtime.InteropServices.JavaScript;
using AlloyClient;
using Alloy.Engine;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace WarriorsWeb;

public static partial class WebHost {
    internal static string ClipboardText;
    private static Main _game;

    [JSImport("setFullscreen", "host")] private static partial void JsSetFullscreen(bool on);
    [JSImport("log", "host")] private static partial void JsLog(string text);
    [JSImport("persistFile", "host")] private static partial void JsPersistFile(string name, string base64);
    [JSImport("getApi", "host")] private static partial string JsGetApi();
    [JSImport("getGameUrl", "host")] private static partial string JsGetGameUrl();
    [JSImport("openUrl", "host")] private static partial void JsOpenUrl(string url);
    [JSImport("reloadPage", "host")] private static partial void JsReloadPage();

    // Where the account server (HTTP api, ending in "/") and the game server bridge (WebSocket) live - from window.WW_CONFIG in main.js.
    public static string ApiBase { get { var a = JsGetApi(); return a.EndsWith("/") ? a : a + "/"; } }
    public static string GameUrl => JsGetGameUrl();
    internal static void SetFullscreen(bool on) { try { JsSetFullscreen(on); } catch { } }
    internal static void OpenUrl(string url) { try { JsOpenUrl(url); } catch { } }
    internal static void ReloadPage() { try { JsReloadPage(); } catch { } }

    public static async Task Main() {
        await JSHost.ImportAsync("gl", "../gl.js");
        await JSHost.ImportAsync("audio", "../audio.js");
        await JSHost.ImportAsync("host", "../host.js");
        Console.WriteLine("[web] host ready");
    }

    // ---- saved login / settings: the desktop client keeps them in files under LocalApplicationData; here that folder lives in memory, so the page
    // copies the two files to localStorage (PersistNow, every few seconds) and puts them back before Start (see main.js) ----------------------
    private static readonly string[] PersistedFiles = ["account.xml", "settings.xml"];

    private static string LocalDir() {
        var name = (string)typeof(Settings).GetField("LocalFolderName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetRawConstantValue() ?? "AlloyClient";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
    }

    [JSExport] public static string LocalFolder() => LocalDir();
    [JSExport] public static string PersistedFileList() => string.Join("|", PersistedFiles);

    [JSExport]
    public static void PersistNow(bool saveSettings) {
        try {
            if (saveSettings) Settings.SaveSettings();   // the desktop client only does this when the process exits
            foreach (var f in PersistedFiles) {
                var path = Path.Combine(LocalDir(), f);
                if (File.Exists(path)) JsPersistFile(f, Convert.ToBase64String(File.ReadAllBytes(path)));
            }
        } catch (Exception e) { Console.WriteLine("[web] persist failed: " + e.Message); }
    }

    // ---- content files --------------------------------------------------------------------------------------------------
    [JSExport]
    public static void WriteFile(string path, byte[] data) {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, data);
    }

    // ---- lifecycle ----------------------------------------------------------------------------------------------------
    [JSExport]
    public static int Start(int width, int height) {
        try {
            Toolkit.Window.ClientSize = new Vector2i(width, height);
            if (GL_Init() == 0) { Console.WriteLine("[web] WebGL2 is not available"); return 0; }

            Settings.LoadSettings();
            _game = new Main();
            _game.Run();

            Toolkit.Event.Raise(new FocusEventArgs { GotFocus = true });
            Toolkit.Event.Raise(new WindowResizeEventArgs { NewClientSize = new Vector2i(width, height) });
            return 1;
        } catch (Exception e) {
            Console.WriteLine("[web] Start failed: " + e);
            return 0;
        }
    }

    private static int GL_Init() => OpenTK.Graphics.OpenGL.GL.JsInit("game-canvas");

    [JSExport]
    public static void Frame(double nowMs) {
        try { GameWindow.Tick(nowMs); }
        catch (Exception e) { Console.WriteLine("[web] frame error: " + e); }
    }

    // ---- input ------------------------------------------------------------------------------------------------------
    [JSExport]
    public static void OnResize(int width, int height) {
        Toolkit.Window.ClientSize = new Vector2i(width, height);
        Toolkit.Event.Raise(new WindowResizeEventArgs { NewClientSize = new Vector2i(width, height) });
    }

    [JSExport] public static void OnFocus(bool focused) => Toolkit.Event.Raise(new FocusEventArgs { GotFocus = focused });
    [JSExport] public static void OnMouseMove(float x, float y) => Toolkit.Event.Raise(new MouseMoveEventArgs { ClientPosition = new Vector2(x, y) });
    [JSExport] public static void OnScroll(float dx, float dy) => Toolkit.Event.Raise(new ScrollEventArgs { Delta = new Vector2(dx, dy) });
    [JSExport] public static void OnText(string text) => Toolkit.Event.Raise(new TextInputEventArgs { Text = text });
    [JSExport] public static void OnClipboard(string text) => ClipboardText = text;

    // JS mouse button (0 left, 1 middle, 2 right) -> OpenTK (Button1 left, Button2 right, Button3 middle)
    [JSExport]
    public static void OnMouseButton(bool down, int jsButton) {
        var button = jsButton switch { 0 => MouseButton.Button1, 2 => MouseButton.Button2, 1 => MouseButton.Button3, 3 => MouseButton.Button4, _ => MouseButton.Button5 };
        if (down) Toolkit.Event.Raise(new MouseButtonDownEventArgs { Button = button });
        else Toolkit.Event.Raise(new MouseButtonUpEventArgs { Button = button });
    }

    [JSExport]
    public static void OnKey(bool down, string code) {
        if (!KeyMap.TryGetValue(code, out var k)) return;
        if (down) Toolkit.Event.Raise(new KeyDownEventArgs { Key = k.Key, Scancode = k.Scan });
        else Toolkit.Event.Raise(new KeyUpEventArgs { Key = k.Key, Scancode = k.Scan });
    }

    // KeyboardEvent.code -> (Key, Scancode)
    private static readonly Dictionary<string, (Key Key, Scancode Scan)> KeyMap = BuildKeyMap();

    private static Dictionary<string, (Key, Scancode)> BuildKeyMap() {
        var m = new Dictionary<string, (Key, Scancode)>();
        for (var c = 'A'; c <= 'Z'; c++) m["Key" + c] = (Enum.Parse<Key>(c.ToString()), Enum.Parse<Scancode>(c.ToString()));
        for (var d = 0; d <= 9; d++) {
            m["Digit" + d] = (Enum.Parse<Key>("D" + d), Enum.Parse<Scancode>("D" + d));
            m["Numpad" + d] = (Enum.Parse<Key>("Keypad" + d), Enum.Parse<Scancode>("Keypad" + d));
        }
        for (var f = 1; f <= 24; f++) m["F" + f] = (Enum.Parse<Key>("F" + f), Enum.Parse<Scancode>("F" + f));

        void Add(string code, Key key, Scancode scan) => m[code] = (key, scan);
        Add("Enter", Key.Return, Scancode.Return);
        Add("NumpadEnter", Key.KeypadEnter, Scancode.KeypadEnter);
        Add("Escape", Key.Escape, Scancode.Escape);
        Add("Backspace", Key.Backspace, Scancode.Backspace);
        Add("Tab", Key.Tab, Scancode.Tab);
        Add("Space", Key.Space, Scancode.Spacebar);
        Add("Minus", Key.Minus, Scancode.Dash);
        Add("Equal", Key.Plus, Scancode.Equals);
        Add("BracketLeft", Key.OEM4, Scancode.LeftBrace);
        Add("BracketRight", Key.OEM6, Scancode.RightBrace);
        Add("Backslash", Key.OEM5, Scancode.Pipe);
        Add("Semicolon", Key.OEM1, Scancode.SemiColon);
        Add("Quote", Key.OEM7, Scancode.LeftApostrophe);
        Add("Backquote", Key.OEM3, Scancode.GraveAccent);
        Add("Comma", Key.Comma, Scancode.Comma);
        Add("Period", Key.Period, Scancode.Period);
        Add("Slash", Key.OEM2, Scancode.QuestionMark);
        Add("CapsLock", Key.CapsLock, Scancode.CapsLock);
        Add("PrintScreen", Key.PrintScreen, Scancode.PrintScreen);
        Add("ScrollLock", Key.ScrollLock, Scancode.ScrollLock);
        Add("Pause", Key.PauseBreak, Scancode.Pause);
        Add("Insert", Key.Insert, Scancode.Insert);
        Add("Home", Key.Home, Scancode.Home);
        Add("PageUp", Key.PageUp, Scancode.PageUp);
        Add("Delete", Key.Delete, Scancode.Delete);
        Add("End", Key.End, Scancode.End);
        Add("PageDown", Key.PageDown, Scancode.PageDown);
        Add("ArrowRight", Key.RightArrow, Scancode.RightArrow);
        Add("ArrowLeft", Key.LeftArrow, Scancode.LeftArrow);
        Add("ArrowDown", Key.DownArrow, Scancode.DownArrow);
        Add("ArrowUp", Key.UpArrow, Scancode.UpArrow);
        Add("NumLock", Key.NumLock, Scancode.NumLock);
        Add("NumpadDivide", Key.KeypadDivide, Scancode.KeypadForwardSlash);
        Add("NumpadMultiply", Key.KeypadMultiply, Scancode.KeypadStar);
        Add("NumpadSubtract", Key.KeypadSubtract, Scancode.KeypadDash);
        Add("NumpadAdd", Key.KeypadAdd, Scancode.KeypadPlus);
        Add("NumpadDecimal", Key.KeypadDecimal, Scancode.KeypadPeriod);
        Add("ShiftLeft", Key.LeftShift, Scancode.LeftShift);
        Add("ShiftRight", Key.RightShift, Scancode.RightShift);
        Add("ControlLeft", Key.LeftControl, Scancode.LeftControl);
        Add("ControlRight", Key.RightControl, Scancode.RightControl);
        Add("AltLeft", Key.LeftAlt, Scancode.LeftAlt);
        Add("AltRight", Key.RightAlt, Scancode.RightAlt);
        Add("MetaLeft", Key.LeftGUI, Scancode.LeftGUI);
        Add("MetaRight", Key.RightGUI, Scancode.RightGUI);
        Add("ContextMenu", Key.Application, Scancode.Application);
        return m;
    }
}
