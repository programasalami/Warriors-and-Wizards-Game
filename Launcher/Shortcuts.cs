using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WarriorsAndWizards.Launcher;

// Desktop + start menu (Windows) / applications menu (Linux) shortcuts to the LAUNCHER, so a shortcut always updates before it plays.
public static class Shortcuts {
    private const string Title = "Warriors & Wizards";
    private const string LinuxId = "warriors-and-wizards";
    public const string IconFile = ".waw-icon.png";         // Linux only: written next to the launcher for the .desktop entries

    public static string DesktopPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Title + ".lnk")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinuxId + ".desktop");

    public static string MenuPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Title + ".lnk")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications", LinuxId + ".desktop");

    public static string MenuLabel => OperatingSystem.IsWindows() ? "Start menu shortcut" : "Applications menu entry";

    // Makes the shortcuts match the player's choices. Errors are returned, not thrown: a missing shortcut must never stop the game.
    public static string? Apply(InstallState state, string launcherPath) {
        try {
            Set(DesktopPath, state.DesktopShortcut, launcherPath);
            Set(MenuPath, state.MenuShortcut, launcherPath);
            return null;
        }
        catch (Exception e) {
            return "Could not update the shortcuts: " + e.Message;
        }
    }

    private static void Set(string path, bool wanted, string launcherPath) {
        if (!wanted) {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsWindows())
            WindowsLink.Create(path, launcherPath, Path.GetDirectoryName(launcherPath)!, "Play Warriors & Wizards");
        else
            WriteDesktopEntry(path, launcherPath);
    }

    private static void WriteDesktopEntry(string path, string launcherPath) {
        var dir = Path.GetDirectoryName(launcherPath)!;
        var icon = Path.Combine(dir, IconFile);
        if (!File.Exists(icon)) {
            using var src = Assets.Open("icon.png");
            using var dst = File.Create(icon);
            src.CopyTo(dst);
        }
        var text = "[Desktop Entry]\nType=Application\nName=" + Title + "\nComment=Play Warriors & Wizards\n" +
                   $"Exec=\"{launcherPath}\"\nPath={dir}\nIcon={icon}\nTerminal=false\nCategories=Game;\n";
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
    }
}

// A .lnk through the shell's own ShellLink object. Plain vtable calls instead of COM interop types: nothing for the trimmer to cut.
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsLink {
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IidPersistFile = new("0000010b-0000-0000-C000-000000000046");

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr obj);

    public static void Create(string lnkPath, string target, string workingDir, string description) {
        CoInitializeEx(IntPtr.Zero, 2);     // apartment; "already initialised" answers are fine
        Check(CoCreateInstance(ClsidShellLink, IntPtr.Zero, 1, IidShellLinkW, out var link), "CoCreateInstance");
        IntPtr file = IntPtr.Zero;
        try {
            var vt = *(IntPtr**) link;
            fixed (char* t = target, w = workingDir, d = description) {
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int>) vt[20])(link, t), "SetPath");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int>) vt[9])(link, w), "SetWorkingDirectory");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int>) vt[7])(link, d), "SetDescription");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>) vt[17])(link, t, 0), "SetIconLocation");
            }
            var iid = IidPersistFile;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>) vt[0])(link, &iid, &file), "QueryInterface");
            var pf = *(IntPtr**) file;
            fixed (char* p = lnkPath)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>) pf[6])(file, p, 1), "Save");
        }
        finally {
            if (file != IntPtr.Zero)
                Marshal.Release(file);
            Marshal.Release(link);
        }
    }

    private static void Check(int hr, string what) {
        if (hr < 0)
            throw new IOException($"{what} failed (0x{hr:X8})");
    }
}
