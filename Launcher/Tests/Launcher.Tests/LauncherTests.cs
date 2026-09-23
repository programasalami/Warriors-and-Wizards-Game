using System.IO.Compression;
using WarriorsAndWizards.Launcher;

namespace Launcher.Tests;

public class LauncherTests {

    [Fact]
    public void ManifestParsesWhatDeployWrites() {
        const string json = """
            {"version":"0.3.5",
             "windows":{"file":"WarriorsAndWizards-Client.zip","size":123456,"sha256":"abc"},
             "linux":{"file":"WarriorsAndWizards-Client-linux.tar.gz","size":654321,"sha256":"def"},
             "launcher":{"version":"1.0.0","windows":{"file":"WarriorsAndWizards-Launcher-windows.zip","size":1,"sha256":"x"},"linux":{"file":"WarriorsAndWizards-Launcher-linux.tar.gz","size":2,"sha256":"y"}}}
            """;
        var m = Manifest.Parse(json)!;
        Assert.Equal("0.3.5", m.Version);
        Assert.Equal("WarriorsAndWizards-Client.zip", m.Windows!.File);
        Assert.Equal(654321, m.Linux!.Size);
        Assert.Equal("1.0.0", m.Launcher!.Version);
        Assert.NotNull(m.ForThisPlatform());
        Assert.NotNull(m.Launcher.ForThisPlatform());
    }

    [Fact]
    public void AManifestWithAByteOrderMarkStillParses() {
        var m = Manifest.Parse("\uFEFF" + """{"version":"0.3.5"}""")!;
        Assert.Equal("0.3.5", m.Version);
    }

    [Fact]
    public void AManifestWithoutALauncherOrOnePlatformStillParses() {
        var m = Manifest.Parse("""{"version":"0.3.5","windows":{"file":"a.zip","size":1,"sha256":"s"}}""")!;
        Assert.Null(m.Launcher);
        Assert.Null(m.Linux);
    }

    [Theory]
    [InlineData("0.3.5", "0.3.5", false)]
    [InlineData("0.3.5 ", "0.3.5", false)]
    [InlineData("0.3.5", "0.3.6", true)]
    [InlineData(null, "0.3.5", true)]
    [InlineData("0.3.5", "", true)]
    public void VersionsDifferOnlyWhenTheyReallyDo(string? installed, string current, bool expected) {
        Assert.Equal(expected, Versions.Differ(installed, current));
    }

    [Fact]
    public void LauncherVersionConstantMatchesTheProjectFile() {
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "Launcher", "Launcher.csproj"));
        Assert.Contains($"<Version>{SelfUpdate.Version}</Version>", csproj);
    }

    [Fact]
    public void ZipExtractVerifyAndSwapWorkEndToEnd() {
        var root = Path.Combine(Path.GetTempPath(), "ww-launcher-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            // an "old" game folder and a new archive
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "old.txt"), "old");

            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "AlloyClient.exe"), "new build");
            var zip = Path.Combine(root, "client.zip");
            ZipFile.CreateFromDirectory(src, zip);

            Assert.True(Updater.HashMatches(zip, Updater.Sha256Of(zip)));
            Assert.False(Updater.HashMatches(zip, "0000"));

            var fresh = game + ".new";
            Updater.Extract(zip, fresh);
            Assert.True(File.Exists(Path.Combine(fresh, "AlloyClient.exe")));

            Updater.Swap(fresh, game);
            Assert.True(File.Exists(Path.Combine(game, "AlloyClient.exe")));
            Assert.False(File.Exists(Path.Combine(game, "old.txt")));
            Assert.False(Directory.Exists(fresh));
        }
        finally {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0", true)]
    [InlineData("2.0.10", "2.0.9", true)]
    [InlineData("1.0.0", "2.0.0", false)]     // an older manifest never downgrades the launcher
    [InlineData("2.0.0", "2.0.0", false)]
    [InlineData("", "2.0.0", false)]
    [InlineData("junk", "2.0.0", false)]
    public void LauncherOnlyUpdatesUpward(string candidate, string current, bool expected) {
        Assert.Equal(expected, Versions.IsNewer(candidate, current));
    }

    [Fact]
    public void InstallPutsTheGameNextToTheLauncherAndLeavesOtherFilesAlone() {
        var root = TempDir();
        try {
            var dir = Path.Combine(root, "install");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Layout.LauncherFile), "the launcher");
            File.WriteAllText(Path.Combine(dir, "my notes.txt"), "the player's own file");

            var state = new InstallState();
            GameInstaller.Install(MakeZip(root, "v1", ("WarriorsAndWizards.exe", "game 1"), ("Content/a.txt", "a1"), ("oldonly.txt", "x")), dir, state, "0.3.7");
            Assert.Equal("0.3.7", state.GameVersion);
            Assert.Equal("game 1", File.ReadAllText(Path.Combine(dir, "WarriorsAndWizards.exe")));
            Assert.True(File.Exists(Path.Combine(dir, "oldonly.txt")));

            // the next version drops a file: it goes, while the launcher and the player's file stay
            GameInstaller.Install(MakeZip(root, "v2", ("WarriorsAndWizards.exe", "game 2"), ("Content/b.txt", "b2")), dir, state, "0.3.8");
            Assert.Equal("game 2", File.ReadAllText(Path.Combine(dir, "WarriorsAndWizards.exe")));
            Assert.False(File.Exists(Path.Combine(dir, "oldonly.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "Content", "a.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "Content", "b.txt")));
            Assert.Equal("the launcher", File.ReadAllText(Path.Combine(dir, Layout.LauncherFile)));
            Assert.True(File.Exists(Path.Combine(dir, "my notes.txt")));
            Assert.False(Directory.Exists(Path.Combine(dir, Layout.StagingFolder)));
            Assert.False(Directory.Exists(Path.Combine(dir, Layout.TrashFolder)));

            var loaded = InstallState.Load(dir)!;
            Assert.Equal("0.3.8", loaded.GameVersion);
            Assert.Contains("Content", loaded.GameFiles);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnArchiveCannotOverwriteTheLauncherOrItsState() {
        var root = TempDir();
        try {
            var dir = Path.Combine(root, "install");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Layout.LauncherFile), "the launcher");
            GameInstaller.Install(MakeZip(root, "v1", ("WarriorsAndWizards.exe", "g"), (Layout.LauncherFile, "evil")), dir, new InstallState(), "1");
            Assert.Equal("the launcher", File.ReadAllText(Path.Combine(dir, Layout.LauncherFile)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TheOldGameNameIsStillFoundWhileAnOldClientArchiveIsOnline() {
        var root = TempDir();
        try {
            Assert.Null(Layout.FindGame(root));
            File.WriteAllText(Path.Combine(root, Layout.Exe("AlloyClient")), "old");
            Assert.EndsWith(Layout.Exe("AlloyClient"), Layout.FindGame(root));
            File.WriteAllText(Path.Combine(root, Layout.Exe("WarriorsAndWizards")), "new");
            Assert.EndsWith(Layout.Exe("WarriorsAndWizards"), Layout.FindGame(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyCleanupRemovesTheOldLauncherAndGameFolderOnly() {
        var root = TempDir();
        try {
            var old = Path.Combine(root, Layout.Exe("WarriorsAndWizards"));
            File.WriteAllText(old, "launcher 1.0.0");
            Directory.CreateDirectory(Path.Combine(root, "game"));
            File.WriteAllText(Path.Combine(root, "game", "AlloyClient.exe"), "old game");
            File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");

            Assert.True(Legacy.IsOldName(old));
            Assert.False(Legacy.IsOldName(Path.Combine(root, Layout.LauncherFile)));
            Legacy.Cleanup(root, old, 0);

            Assert.False(File.Exists(old));
            Assert.False(Directory.Exists(Path.Combine(root, "game")));
            Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SelfUpdateFindsTheLauncherUnderEitherName() {
        var root = TempDir();
        try {
            Assert.Null(SelfUpdate.FindLauncherIn(root));
            File.WriteAllText(Path.Combine(root, Layout.Exe("WarriorsAndWizards")), "bridge name");
            Assert.NotNull(SelfUpdate.FindLauncherIn(root));
            File.WriteAllText(Path.Combine(root, Layout.LauncherFile), "new name");
            Assert.EndsWith(Layout.LauncherFile, SelfUpdate.FindLauncherIn(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ADamagedStateFileMeansAFreshInstall() {
        var root = TempDir();
        try {
            File.WriteAllText(Path.Combine(root, Layout.StateFile), "{ not json");
            Assert.Null(InstallState.Load(root));
            new InstallState { GameVersion = "1.2.3", DesktopShortcut = false }.Save(root);
            new InstallState { GameVersion = "1.2.4", DesktopShortcut = false }.Save(root);     // a hidden file can be saved over
            var s = InstallState.Load(root)!;
            Assert.Equal("1.2.4", s.GameVersion);
            Assert.False(s.DesktopShortcut);
            Assert.True(s.MenuShortcut);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempDir() {
        var root = Path.Combine(Path.GetTempPath(), "waw-launcher-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string MakeZip(string root, string name, params (string Path, string Text)[] files) {
        var src = Path.Combine(root, "src-" + name);
        foreach (var (path, text) in files) {
            var full = Path.Combine(src, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        var zip = Path.Combine(root, name + ".zip");
        ZipFile.CreateFromDirectory(src, zip);
        return zip;
    }

    [Fact]
    public void HumanSizesReadNaturally() {
        Assert.Equal("150.0 MB", Updater.Human(150L * 1024 * 1024));
        Assert.Equal("12 KB", Updater.Human(12 * 1024));
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "deploy.ps1")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
