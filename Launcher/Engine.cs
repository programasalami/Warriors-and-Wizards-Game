using System.Diagnostics;

namespace WarriorsAndWizards.Launcher;

public enum Outcome { Ready, ReadyOffline, Restarting, Failed }

// The launcher's work, without any window: check the manifest, replace the launcher if a newer one is online, install / update the
// game, start it. The window (LauncherWindow) and the --check / --no-launch command lines both drive this.
public sealed class LauncherEngine {
    public const string DefaultDownloadBase = "https://play.warriorsandwizards.com/download/";

    public string InstallDir { get; }
    public string LauncherPath { get; }
    public InstallState State { get; }
    public Manifest? Manifest { get; private set; }
    public string? Error { get; private set; }

    public event Action<string>? Status;
    public event Action<long, long>? Progress;      // bytes done, bytes total (0 total = unknown / not downloading)

    private readonly string _downloadBase;

    public LauncherEngine(string installDir, string launcherPath, InstallState state, string? downloadBase = null) {
        InstallDir = installDir;
        LauncherPath = launcherPath;
        State = state;
        _downloadBase = downloadBase ?? DownloadBase();
    }

    // A local test server can stand in for the VPS in DEBUG builds only (WAW_DOWNLOAD_BASE=http://127.0.0.1:8000/).
    private static string DownloadBase() {
#if DEBUG
        var over = Environment.GetEnvironmentVariable("WAW_DOWNLOAD_BASE");
        if (!string.IsNullOrWhiteSpace(over))
            return over.EndsWith('/') ? over : over + "/";
#endif
        return DefaultDownloadBase;
    }

    public bool GameInstalled => Layout.FindGame(InstallDir) != null && !string.IsNullOrEmpty(State.GameVersion);

    // skipLauncherUpdate: set right after a self-update restart, so a manifest that names a launcher version the binary does not
    // carry (a deploy slip) cannot make it replace and restart itself forever.
    public async Task<Outcome> RunAsync(bool checkOnly = false, bool skipLauncherUpdate = false) {
        Error = null;
        Say("Checking for updates...");
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var json = await Updater.Http.GetStringAsync(_downloadBase + "manifest.json?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cts.Token);
            Manifest = Manifest.Parse(json);
        }
        catch (Exception e) {
            Manifest = null;
            Error = $"Could not reach the update server ({e.Message}).";
        }

        if (Manifest == null) {
            if (GameInstalled) {
                Say("Offline: the installed version can still be played.");
                return Outcome.ReadyOffline;
            }
            return Fail(Error ?? "The update server sent nothing usable.");
        }

        // ---- the launcher itself (only ever upward: an older manifest never downgrades it)
        var launcherArchive = Manifest.Launcher?.ForThisPlatform();
        if (!checkOnly && !skipLauncherUpdate && launcherArchive != null && Versions.IsNewer(Manifest.Launcher!.Version, SelfUpdate.Version)) {
            Say($"Updating the launcher to {Manifest.Launcher.Version}...");
            var temp = Path.Combine(Path.GetTempPath(), "waw-launcher-" + launcherArchive.File);
            try {
                await Updater.DownloadAsync(_downloadBase + launcherArchive.File, temp, launcherArchive.Size, Report);
                if (!Updater.HashMatches(temp, launcherArchive.Sha256))
                    throw new InvalidDataException("the download did not match its checksum");
                SelfUpdate.ReplaceWith(temp);
                return Outcome.Restarting;
            }
            catch (Exception e) {
                Say($"Launcher update skipped: {e.Message}");      // the old launcher still works; carry on with the game
            }
            finally {
                GameInstaller.TryDelete(temp);
                Report(0, 0);
            }
        }

        // ---- the game
        var archive = Manifest.ForThisPlatform();
        if (archive == null || string.IsNullOrEmpty(archive.File))
            return GameInstalled ? Ready("No new build for this system yet.") : Fail("The update server has no game for this system yet.");

        var needed = Layout.FindGame(InstallDir) == null || Versions.Differ(State.GameVersion, Manifest.Version);
        if (!needed)
            return Ready("Ready to play.");
        if (checkOnly) {
            Say(State.GameVersion == null ? "Not installed yet." : $"Update available: {State.GameVersion} -> {Manifest.Version}");
            return Outcome.Ready;
        }
        if (GameInstaller.GameIsRunning(InstallDir))
            return Fail("The game is running. Close it, then press Retry to update.");

        var what = State.GameVersion == null ? $"Downloading Warriors & Wizards {Manifest.Version}" : $"Updating {State.GameVersion} -> {Manifest.Version}";
        Say($"{what} ({Updater.Human(archive.Size)})...");
        var download = Path.Combine(Path.GetTempPath(), "waw-" + archive.File);
        try {
            await Updater.DownloadAsync(_downloadBase + archive.File, download, archive.Size, Report);
            Report(0, 0);
            Say("Verifying...");
            if (!await Task.Run(() => Updater.HashMatches(download, archive.Sha256)))
                return Fail("The download did not match its checksum. Press Retry.");
            Say("Installing...");
            var version = Manifest.Version;
            await Task.Run(() => GameInstaller.Install(download, InstallDir, State, version));
            return Ready($"Installed version {version}. Ready to play.");
        }
        catch (Exception e) {
            return Fail($"Update failed: {e.Message}");
        }
        finally {
            GameInstaller.TryDelete(download);
            Report(0, 0);
        }
    }

    public string? Launch() {
        var exe = Layout.FindGame(InstallDir);
        if (exe == null)
            return "The game is not installed yet.";
        try {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = InstallDir });
            return null;
        }
        catch (Exception e) {
            return $"Could not start the game: {e.Message}";
        }
    }

    // "Repair": forget the installed version so the next run downloads the whole game again.
    public void ForgetInstalledVersion() {
        State.GameVersion = null;
        State.Save(InstallDir);
    }

    private Outcome Ready(string text) { Say(text); return Outcome.Ready; }
    private Outcome Fail(string text) { Error = text; Say(text); return Outcome.Failed; }
    private void Say(string text) => Status?.Invoke(text);
    private void Report(long done, long total) => Progress?.Invoke(done, total);
}
