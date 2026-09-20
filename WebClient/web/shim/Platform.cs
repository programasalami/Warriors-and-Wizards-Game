// Browser stand-ins for the OpenTK.Platform types the client uses (window / input events / clipboard). The desktop build gets these
// from OpenTK's native windowing; here the page's JavaScript raises the same event objects (see WebHost).
using OpenTK.Mathematics;

namespace OpenTK.Platform;

public sealed class WindowHandle { }
public sealed class OpenGLContextHandle { }
public sealed class DisplayHandle { }

public sealed class FocusEventArgs : EventArgs { public bool GotFocus; }
public sealed class CloseEventArgs : EventArgs { }
public sealed class KeyDownEventArgs : EventArgs { public Key Key; public Scancode Scancode; }
public sealed class KeyUpEventArgs : EventArgs { public Key Key; public Scancode Scancode; }
public sealed class TextInputEventArgs : EventArgs { public string Text; }
public sealed class WindowResizeEventArgs : EventArgs { public Vector2i NewClientSize; }
public sealed class WindowMoveEventArgs : EventArgs { public Vector2i WindowPosition; }
public sealed class MouseButtonDownEventArgs : EventArgs { public MouseButton Button; }
public sealed class MouseButtonUpEventArgs : EventArgs { public MouseButton Button; }
public sealed class MouseMoveEventArgs : EventArgs { public Vector2 ClientPosition; }
public sealed class ScrollEventArgs : EventArgs { public Vector2 Delta; }

public static class Toolkit {
    public static class Event {
        public static event Action<EventArgs> EventRaised;
        public static void Raise(EventArgs args) => EventRaised?.Invoke(args);
    }

    public static class Window {
        internal static Vector2i ClientSize = new(1280, 720);
        internal static WindowMode Mode = WindowMode.Normal;
        public static DisplayHandle GetDisplay(WindowHandle w) => new();
        public static void SetPosition(WindowHandle w, Vector2i p) { }
        public static void SetSize(WindowHandle w, Vector2i s) { }
        public static void SetMode(WindowHandle w, WindowMode m) { Mode = m; WarriorsWeb.WebHost.SetFullscreen(m == WindowMode.ExclusiveFullscreen || m == WindowMode.WindowedFullscreen); }
        public static void SetMinClientSize(WindowHandle w, int x, int y) { }
        public static void SetTitle(WindowHandle w, string t) { }
        public static void GetClientSize(WindowHandle w, out Vector2i size) => size = ClientSize;
        public static WindowMode GetMode(WindowHandle w) => Mode;
    }

    public static class Display {
        public static Box2i GetWorkArea(DisplayHandle d) => new(0, 0, Window.ClientSize.X, Window.ClientSize.Y);
    }

    public static class OpenGL {
        public static void SetSwapInterval(int interval) { }
    }

    public static class Clipboard {
        public static ClipboardFormat GetClipboardFormat() => string.IsNullOrEmpty(WarriorsWeb.WebHost.ClipboardText) ? ClipboardFormat.None : ClipboardFormat.Text;
        public static string GetClipboardText() => WarriorsWeb.WebHost.ClipboardText ?? "";
    }
}
