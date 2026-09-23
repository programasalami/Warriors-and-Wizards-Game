using System.Diagnostics;

namespace WarriorsAndWizards.Launcher;

// The launcher replacing itself. A running executable cannot be overwritten on Windows, but it CAN be renamed: the new file
// is put next to it, the running one is renamed to *.old, the new one takes the real name, and the new launcher is started.
// The .old file is deleted by the next launcher start.
public static class SelfUpdate {
    public const string Version = "2.0.0";     // keep equal to <Version> in Launcher.csproj; deploy -Launcher publishes it in the manifest

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("cannot find my own executable");

    public static void CleanupOld() {
        var old = ExecutablePath + ".old";
        if (File.Exists(old)) {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // archive: the launcher archive from the manifest (zip on Windows, tar.gz on Linux). Since 2.0.0 it holds the launcher under
    // the OLD name WarriorsAndWizards(.exe), because launcher 1.0.0 only accepts a file with that name when it updates itself;
    // whatever it is called inside, it takes this launcher's own file name here.
    public static void ReplaceWith(string archive) {
        var me = ExecutablePath;
        var stage = me + ".stage";
        if (Directory.Exists(stage))
            Directory.Delete(stage, true);
        Updater.Extract(archive, stage);

        var fresh = FindLauncherIn(stage) ?? throw new FileNotFoundException("the launcher archive holds no launcher executable");

        var old = me + ".old";
        if (File.Exists(old))
            File.Delete(old);
        File.Move(me, old);
        File.Move(fresh, me);
        Updater.MakeExecutable(me);
        Directory.Delete(stage, true);
    }

    public static string? FindLauncherIn(string folder) {
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var name in new[] { Layout.LauncherName, Layout.GameName }) {
            var path = Path.Combine(folder, name + ext);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public static void Restart(string? extraArgs = null) {
        var info = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExecutablePath) };
        if (extraArgs != null)
            info.Arguments = extraArgs;
        Process.Start(info);
    }
}
