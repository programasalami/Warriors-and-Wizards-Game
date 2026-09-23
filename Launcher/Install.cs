using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarriorsAndWizards.Launcher;

// The install folder (since launcher 2.0.0, 2026-09-22) - the game sits right next to the launcher, no 'game' subfolder:
//   WaWLauncher(.exe)            this program
//   WarriorsAndWizards(.exe)     the game, one file (deploy -Client / -Linux publish it single-file)
//   Content/  runtimes/  ...     the game's data, whatever the client archive holds at its top level
//   .wawlauncher.json            what is installed + the player's launcher choices (hidden on Windows)
// Launcher 1.0.0 was called WarriorsAndWizards(.exe) and kept the game in ./game; Legacy handles moving such a folder over.
public static class Layout {
    public const string LauncherName = "WaWLauncher";
    public const string GameName = "WarriorsAndWizards";
    public const string OldGameName = "AlloyClient";            // the game's name inside client archives before 2026-09-22
    public const string StateFile = ".wawlauncher.json";
    public const string StagingFolder = ".waw-staging";
    public const string TrashFolder = ".waw-old";

    public static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
    public static string LauncherFile => Exe(LauncherName);

    // The game executable in an install folder: the current name, or the old one while an old client archive is still online.
    public static string? FindGame(string installDir) {
        foreach (var name in new[] { GameName, OldGameName }) {
            var path = Path.Combine(installDir, Exe(name));
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    // Folders the launcher offers when it is started from a place nobody wants a game unpacked into.
    public static string StandardInstallDir =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Warriors & Wizards")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "warriors-and-wizards");

    public static bool IsClutteredPlace(string dir) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(home, "Downloads"),
            home,
            Path.GetTempPath()
        };
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        return places.Where(p => !string.IsNullOrEmpty(p))
            .Any(p => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)), full, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class InstallState {
    [JsonPropertyName("gameVersion")] public string? GameVersion { get; set; }
    [JsonPropertyName("gameFiles")] public List<string> GameFiles { get; set; } = [];
    [JsonPropertyName("desktopShortcut")] public bool DesktopShortcut { get; set; } = true;
    [JsonPropertyName("menuShortcut")] public bool MenuShortcut { get; set; } = true;
    [JsonPropertyName("closeOnPlay")] public bool CloseOnPlay { get; set; } = true;

    public static InstallState? Load(string installDir) {
        var path = Path.Combine(installDir, Layout.StateFile);
        if (!File.Exists(path))
            return null;
        try {
            return JsonSerializer.Deserialize(File.ReadAllText(path), InstallJsonContext.Default.InstallState);
        }
        catch (Exception) {
            return null;        // a damaged file: treated as a fresh folder, the game is simply downloaded again
        }
    }

    public void Save(string installDir) {
        Directory.CreateDirectory(installDir);
        var path = Path.Combine(installDir, Layout.StateFile);
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);      // a hidden file cannot be overwritten on Windows
        File.WriteAllText(path, JsonSerializer.Serialize(this, InstallJsonContext.Default.InstallState));
        if (OperatingSystem.IsWindows())
            File.SetAttributes(path, FileAttributes.Hidden);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstallState))]
internal partial class InstallJsonContext : JsonSerializerContext { }

public static class GameInstaller {
    // Unpacks a verified client archive over the install folder. Only the entries the archive holds and the ones the previous
    // version installed are touched, so anything else the player keeps in the folder (and the launcher itself) is left alone.
    // Old entries are parked in .waw-old first; if anything fails, what was moved is put back and the old game still runs.
    public static void Install(string archive, string installDir, InstallState state, string version) {
        var staging = Path.Combine(installDir, Layout.StagingFolder);
        var trash = Path.Combine(installDir, Layout.TrashFolder);
        Updater.Extract(archive, staging);

        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            Layout.LauncherFile, Layout.StateFile, Layout.StagingFolder, Layout.TrashFolder
        };
        var incoming = Directory.EnumerateFileSystemEntries(staging).Select(Path.GetFileName).OfType<string>()
            .Where(n => !protectedNames.Contains(n)).ToList();
        if (incoming.Count == 0)
            throw new InvalidDataException("the game archive is empty");

        if (Directory.Exists(trash))
            Directory.Delete(trash, true);
        Directory.CreateDirectory(trash);

        var parked = new List<string>();
        var placed = new List<string>();
        try {
            foreach (var name in state.GameFiles.Concat(incoming).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !protectedNames.Contains(n))) {
                var path = Path.Combine(installDir, name);
                if (!File.Exists(path) && !Directory.Exists(path))
                    continue;
                Move(path, Path.Combine(trash, name));
                parked.Add(name);
            }
            foreach (var name in incoming) {
                Move(Path.Combine(staging, name), Path.Combine(installDir, name));
                placed.Add(name);
            }
        }
        catch (Exception) {
            foreach (var name in placed)
                TryDelete(Path.Combine(installDir, name));
            foreach (var name in parked)
                try { Move(Path.Combine(trash, name), Path.Combine(installDir, name)); } catch (Exception) { /* best effort */ }
            throw;
        }

        state.GameFiles = incoming;
        state.GameVersion = version;
        state.Save(installDir);

        var game = Layout.FindGame(installDir);
        if (game != null)
            Updater.MakeExecutable(game);
        TryDelete(staging);
        TryDelete(trash);           // a file still open (rare): the next update clears it
    }

    // True when a game started from this folder is still running (it holds its files open, so updating would fail half way).
    public static bool GameIsRunning(string installDir) {
        foreach (var name in new[] { Layout.GameName, Layout.OldGameName }) {
            foreach (var p in Process.GetProcessesByName(name)) {
                try {
                    var file = p.MainModule?.FileName;
                    if (file != null && string.Equals(Path.GetDirectoryName(file), Path.TrimEndingDirectorySeparator(installDir), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception) {
                    return true;            // cannot look inside it: assume it is ours rather than break a running game
                }
                finally { p.Dispose(); }
            }
        }
        return false;
    }

    private static void Move(string from, string to) {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    public static void TryDelete(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception) { /* left for the next run */ }
    }
}

// Launcher 1.0.0 (named WarriorsAndWizards, game in ./game) updates itself into this launcher under its OLD name. Started under that
// name, the launcher copies itself to WaWLauncher next to it and hands over (--migrate-from); the new copy then removes the old
// launcher and the old game folder, and installs the game next to itself. The player's settings are not in either, so nothing is lost.
public static class Legacy {
    public static bool IsOldName(string exePath) =>
        string.Equals(Path.GetFileNameWithoutExtension(exePath), Layout.GameName, StringComparison.OrdinalIgnoreCase);

    public static void Cleanup(string installDir, string? oldLauncher, int oldPid) {
        if (oldPid > 0) {
            try {
                using var p = Process.GetProcessById(oldPid);
                p.WaitForExit(10_000);
            }
            catch (Exception) { /* already gone */ }
        }
        if (oldLauncher != null && IsOldName(oldLauncher) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(oldLauncher)), Path.TrimEndingDirectorySeparator(installDir), StringComparison.OrdinalIgnoreCase)) {
            for (var i = 0; i < 20 && File.Exists(oldLauncher); i++) {
                GameInstaller.TryDelete(oldLauncher);
                if (File.Exists(oldLauncher))
                    Thread.Sleep(250);
            }
            GameInstaller.TryDelete(oldLauncher + ".old");
        }
        foreach (var name in new[] { "game", "game.new", "game.old" })
            GameInstaller.TryDelete(Path.Combine(installDir, name));
    }
}
