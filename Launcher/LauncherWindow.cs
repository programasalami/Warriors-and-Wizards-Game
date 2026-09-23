using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WarriorsAndWizards.Launcher;

// The launcher window, built in code (no XAML, so the trimmed single-file build has nothing to lose). Two pages on the right of the
// key art: the first-run page (install folder + shortcuts) and the main page (status, progress, PLAY, options, links).
public sealed class LauncherWindow : Window {
    private const string ReleasesUrl = "https://portal.warriorsandwizards.com/releases.html";
    private const string WebsiteUrl = "https://warriorsandwizards.com";

    private readonly ContentControl _page = new();
    private InstallState? _state;
    private LauncherEngine? _engine;

    // main page controls
    private readonly TextBlock _status = Label("", 17, Palette.Text, FontWeight.SemiBold);
    private readonly TextBlock _detail = Label("", 12, Palette.Dim);
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1, IsIndeterminate = true };
    private readonly Button _play = BigButton("PLAY");
    private readonly TextBlock _versions = Label("", 12, Palette.Dim);
    private bool _busy;
    private bool _justUpdated = Environment.GetCommandLineArgs().Contains("--just-updated");
    private DateTime _speedAt = DateTime.UtcNow;
    private long _speedBytes;
    private double _speed;

    public LauncherWindow() {
        Title = "Warriors & Wizards";
        Width = 820;
        Height = 480;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Palette.Background);
        using (var icon = Assets.Open("icon.png"))
            Icon = new WindowIcon(icon);

        Bitmap art;
        using (var s = Assets.Open("art.png"))
            art = new Bitmap(s);

        // The whole window is one background (black since 2.1.0) with the embers drifting over it, so the art column is see-through now.
        var left = new Border {
            Width = 340,
            Background = Brushes.Transparent,
            Child = new Image { Source = art, Width = 300, Height = 300, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
        };

        // The title in the game's own lettering (Not Jam Signature 21, embedded - see Launcher.csproj); a pixel font, so no synthetic bold.
        var header = new StackPanel {
            Spacing = 2,
            Children = {
                new TextBlock {
                    FontSize = 36, FontWeight = FontWeight.Normal, FontFamily = TitleFont,
                    Inlines = [
                        new Avalonia.Controls.Documents.Run("Warriors ") { Foreground = new SolidColorBrush(Palette.Orange) },
                        new Avalonia.Controls.Documents.Run("& ") { Foreground = new SolidColorBrush(Palette.Text) },
                        new Avalonia.Controls.Documents.Run("Wizards") { Foreground = new SolidColorBrush(Palette.Purple) }
                    ]
                }
            }
        };

        var right = new DockPanel { Margin = new Thickness(32, 26, 32, 22) };
        DockPanel.SetDock(header, Dock.Top);
        right.Children.Add(header);
        right.Children.Add(_page);

        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = new Panel { Children = { new EmberField(), root } };      // embers behind everything

        _play.Click += (_, _) => Play();

        _state = InstallState.Load(Program.InstallDir);
        if (_state == null)
            ShowFirstRun();
        else
            ShowMain();
    }

    // ---------------------------------------------------------------- first run

    private void ShowFirstRun() {
        var state = new InstallState();
        var folder = Program.Migrated || !Layout.IsClutteredPlace(Program.InstallDir) ? Program.InstallDir : Layout.StandardInstallDir;

        var folderBox = new TextBox { Text = folder, IsReadOnly = true, FontSize = 13 };
        var change = SmallButton("Change...");
        change.Click += async (_, _) => {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Install Warriors & Wizards into...", AllowMultiple = false });
            var path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null)
                return;
            // a folder with other things in it gets its own subfolder, like any installer does
            var empty = !Directory.EnumerateFileSystemEntries(path).Any();
            var ours = File.Exists(Path.Combine(path, Layout.LauncherFile)) || File.Exists(Path.Combine(path, Layout.StateFile));
            folderBox.Text = empty || ours ? path : Path.Combine(path, "Warriors & Wizards");
        };

        var folderRow = new DockPanel { Children = { change, folderBox } };
        DockPanel.SetDock(change, Dock.Right);
        change.Margin = new Thickness(8, 0, 0, 0);

        var desktop = Check("Desktop shortcut", state.DesktopShortcut);
        var menu = Check(Shortcuts.MenuLabel, state.MenuShortcut);
        var close = Check("Close the launcher when the game starts", state.CloseOnPlay);
        var error = Label("", 12, Palette.Bad);

        var install = BigButton("INSTALL");
        install.Click += (_, _) => {
            state.DesktopShortcut = desktop.IsChecked == true;
            state.MenuShortcut = menu.IsChecked == true;
            state.CloseOnPlay = close.IsChecked == true;
            var dir = folderBox.Text ?? Program.InstallDir;
            try {
                if (SamePath(dir, Program.InstallDir)) {
                    state.Save(dir);
                    _state = state;
                    var problem = Shortcuts.Apply(state, Program.LauncherPath);
                    ShowMain(problem);
                } else {
                    // copy the launcher into the chosen folder and carry on from there; the downloaded copy can be deleted
                    Directory.CreateDirectory(dir);
                    var target = Path.Combine(dir, Layout.LauncherFile);
                    File.Copy(Program.LauncherPath, target, overwrite: true);
                    Updater.MakeExecutable(target);
                    state.Save(dir);
                    Shortcuts.Apply(state, target);
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = dir });
                    Close();
                }
            }
            catch (Exception e) {
                error.Text = "Could not install there: " + e.Message;
            }
        };

        _page.Content = new StackPanel {
            Spacing = 10, Margin = new Thickness(0, 22, 0, 0),
            Children = {
                Label("Welcome! Where should the game go?", 17, Palette.Text, FontWeight.SemiBold),
                folderRow,
                Label("Size: 150 MB", 12, Palette.Dim),
                new Border { Height = 4 },
                desktop, menu, close,
                new Border { Height = 4 },
                install,
                error
            }
        };
    }

    // ---------------------------------------------------------------- main page

    private void ShowMain(string? note = null) {
        var state = _state!;
        _engine = new LauncherEngine(Program.InstallDir, Program.LauncherPath, state);
        _engine.Status += text => Dispatcher.UIThread.Post(() => _status.Text = text);
        _engine.Progress += (done, total) => Dispatcher.UIThread.Post(() => ShowProgress(done, total));

        var desktop = Check("Desktop shortcut", state.DesktopShortcut);
        var menu = Check(Shortcuts.MenuLabel, state.MenuShortcut);
        var close = Check("Close the launcher when the game starts", state.CloseOnPlay);
        void Changed() {
            state.DesktopShortcut = desktop.IsChecked == true;
            state.MenuShortcut = menu.IsChecked == true;
            state.CloseOnPlay = close.IsChecked == true;
            try { state.Save(Program.InstallDir); } catch (Exception) { }
            var problem = Shortcuts.Apply(state, Program.LauncherPath);
            if (problem != null)
                _detail.Text = problem;
        }
        desktop.IsCheckedChanged += (_, _) => Changed();
        menu.IsCheckedChanged += (_, _) => Changed();
        close.IsCheckedChanged += (_, _) => Changed();

        var links = new StackPanel {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Children = {
                Link("Open game folder", OpenFolder),
                Link("Release notes", () => OpenUrl(ReleasesUrl)),
                Link("Website", () => OpenUrl(WebsiteUrl)),
                Link("Repair", Repair)
            }
        };

        var bottom = new StackPanel { Spacing = 4, Children = { links, _versions } };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var body = new StackPanel {
            Spacing = 10, Margin = new Thickness(0, 22, 0, 0),
            Children = { _status, _progress, _detail, new Border { Height = 2 }, _play, new Border { Height = 4 }, desktop, menu, close }
        };

        var page = new DockPanel { Children = { bottom, body } };
        _page.Content = page;

        _detail.Text = note ?? "";
        UpdateVersions();
        _ = RunUpdates();
    }

    private async Task RunUpdates() {
        if (_busy || _engine == null)
            return;
        _busy = true;
        _play.IsEnabled = false;
        _play.Content = "PLEASE WAIT";
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;

        var outcome = await _engine.RunAsync(skipLauncherUpdate: _justUpdated);
        _justUpdated = false;
        _busy = false;
        UpdateVersions();

        switch (outcome) {
            case Outcome.Restarting:
                _status.Text = "Launcher updated. Restarting...";
                SelfUpdate.Restart("--just-updated");
                Close();
                return;
            case Outcome.Failed:
                _play.Content = "RETRY";
                _play.IsEnabled = true;
                _progress.IsVisible = false;
                _status.Foreground = new SolidColorBrush(Palette.Bad);
                return;
            default:
                _status.Foreground = new SolidColorBrush(Palette.Text);
                _play.Content = "PLAY";
                _play.IsEnabled = true;
                _progress.IsIndeterminate = false;
                _progress.Value = 1;
                return;
        }
    }

    private void Play() {
        if (_engine == null || _busy)
            return;
        if (!_engine.GameInstalled || (string?) _play.Content == "RETRY") {
            _status.Foreground = new SolidColorBrush(Palette.Text);
            _ = RunUpdates();
            return;
        }
        var problem = _engine.Launch();
        if (problem != null) {
            _status.Text = problem;
            return;
        }
        _status.Text = "Starting the game...";
        if (_state!.CloseOnPlay)
            Close();
    }

    private void Repair() {
        if (_engine == null || _busy)
            return;
        _engine.ForgetInstalledVersion();
        _ = RunUpdates();
    }

    private void ShowProgress(long done, long total) {
        if (!_busy)
            return;         // a late report after the work finished must not empty the full bar
        if (total <= 0) {
            _progress.IsIndeterminate = true;
            _detail.Text = "";
            _speedBytes = 0;
            return;
        }
        _progress.IsIndeterminate = false;
        _progress.Value = Math.Clamp(done / (double) total, 0, 1);

        var now = DateTime.UtcNow;
        var seconds = (now - _speedAt).TotalSeconds;
        if (seconds >= 0.5) {
            var rate = (done - _speedBytes) / seconds;
            _speed = _speed <= 0 ? rate : _speed * 0.7 + rate * 0.3;
            _speedAt = now;
            _speedBytes = done;
        }
        var speed = _speed > 0 ? $"  -  {Updater.Human((long) _speed)}/s" : "";
        _detail.Text = $"{Updater.Human(done)} of {Updater.Human(total)}  ({done * 100 / total}%){speed}";
    }

    private void UpdateVersions() {
        var game = _state?.GameVersion ?? "not installed";
        var latest = _engine?.Manifest?.Version;
        var gameText = latest != null && latest != _state?.GameVersion && _state?.GameVersion != null ? $"{game} (new: {latest})" : game;
        _versions.Text = $"Game {gameText}  |  Launcher {SelfUpdate.Version}";
    }

    private void OpenFolder() {
        try {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Program.InstallDir}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("xdg-open", Program.InstallDir) { UseShellExecute = false });
        }
        catch (Exception e) {
            _detail.Text = "Could not open the folder: " + e.Message;
        }
    }

    private async void OpenUrl(string url) {
        try {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception e) {
            _detail.Text = "Could not open the browser: " + e.Message;
        }
    }

    // ---------------------------------------------------------------- building blocks

    private static readonly FontFamily TitleFont = new("avares://WaWLauncher/Assets/Fonts#Not Jam Signature 21");

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static TextBlock Label(string text, double size, Color color, FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = weight, TextWrapping = TextWrapping.Wrap };

    private static CheckBox Check(string text, bool value) =>
        new() { Content = text, IsChecked = value, FontSize = 14, Foreground = new SolidColorBrush(Palette.Text) };

    private static Button BigButton(string text) => new() {
        Content = text, Height = 54, FontSize = 22, FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Foreground = Brushes.White,
        Background = new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Palette.Orange, 0), new GradientStop(Palette.Purple, 1) }
        },
        CornerRadius = new CornerRadius(6)
    };

    private static Button SmallButton(string text) => new() { Content = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Stretch };

    private static Button Link(string text, Action onClick) {
        var b = new Button {
            Content = text, FontSize = 12, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Palette.Purple), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        b.Click += (_, _) => onClick();
        return b;
    }
}
