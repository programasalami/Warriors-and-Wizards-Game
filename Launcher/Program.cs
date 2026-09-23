using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace WarriorsAndWizards.Launcher;

// Warriors & Wizards launcher (WaWLauncher). A window since 2.0.0 (2026-09-22): install folder + shortcuts on the first run, then
// check / update / PLAY. The game is installed next to the launcher (see Layout). One codebase for Windows and Linux.
//   --check        only report whether an update is due (no window)
//   --no-launch    update without a window and without starting the game
//   --migrate-from <old launcher> --pid <n>   internal: the hand-over from launcher 1.0.0 (see Legacy)
public static class Program {
    public static string LauncherPath { get; private set; } = "";
    public static string InstallDir { get; private set; } = "";
    public static bool Migrated { get; private set; }

    [STAThread]
    public static int Main(string[] args) {
        LauncherPath = SelfUpdate.ExecutablePath;
        InstallDir = Path.GetDirectoryName(LauncherPath)!;
        SelfUpdate.CleanupOld();

        // Started under launcher 1.0.0's name (it just updated itself into this program): become WaWLauncher and hand over.
        if (Legacy.IsOldName(LauncherPath)) {
            var target = Path.Combine(InstallDir, Layout.LauncherFile);
            try {
                File.Copy(LauncherPath, target, overwrite: true);
                Updater.MakeExecutable(target);
            }
            catch (Exception) { /* a WaWLauncher is already there and busy: start that one */ }
            Process.Start(new ProcessStartInfo(target) {
                UseShellExecute = false, WorkingDirectory = InstallDir,
                ArgumentList = { "--migrate-from", LauncherPath, "--pid", Environment.ProcessId.ToString() }
            });
            return 0;
        }

        var from = ArgAfter(args, "--migrate-from");
        if (from != null) {
            int.TryParse(ArgAfter(args, "--pid"), out var pid);
            Legacy.Cleanup(InstallDir, from, pid);
            Migrated = true;
        }

        if (args.Contains("--check") || args.Contains("--no-launch"))
            return Headless(args.Contains("--check"));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();

    private static int Headless(bool checkOnly) {
        var state = InstallState.Load(InstallDir) ?? new InstallState();
        var engine = new LauncherEngine(InstallDir, LauncherPath, state);
        engine.Status += text => Console.WriteLine("  " + text);
        var outcome = engine.RunAsync(checkOnly).GetAwaiter().GetResult();
        if (outcome == Outcome.Restarting)
            Console.WriteLine("  The launcher was updated; run it again.");
        return outcome == Outcome.Failed ? 1 : 0;
    }

    private static string? ArgAfter(string[] args, string name) {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

public sealed class App : Application {
    public override void Initialize() {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
        // the theme's accent (check boxes, focus rings, the progress bar) in the game's purple instead of Windows blue
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                                    "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3" })
            Resources[key] = Palette.Purple;
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new LauncherWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

// The pictures inside the launcher (Assets/*.png, embedded).
public static class Assets {
    public static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("WarriorsAndWizards.Launcher.Assets." + name)
        ?? throw new FileNotFoundException("embedded asset missing: " + name);
}

public static class Palette {
    public static readonly Color Background = Color.Parse("#120D16");
    public static readonly Color Panel = Color.Parse("#1E1624");
    public static readonly Color Line = Color.Parse("#3A2C44");
    public static readonly Color Text = Color.Parse("#EFE6F5");
    public static readonly Color Dim = Color.Parse("#A898B4");
    public static readonly Color Purple = Color.Parse("#9B4DFF");
    public static readonly Color Orange = Color.Parse("#FF7A2F");
    public static readonly Color Bad = Color.Parse("#FF6B6B");
}
