using AlloyClient.Game.Components.Admin;

namespace AlloyClient.Tests.Admin;

public class EditorLocatorTests {
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ww_repo");
    private static string Editor => Path.Combine(Root, "Tools", "Editor", "editor.html");

    [Fact]
    public void WalksUpFromTheExeFolderToTheRepoRoot() {
        var exe = Path.Combine(Root, "AlloyClient", "AlloyClient", "bin", "Debug", "net10.0");
        Assert.Equal(Editor, EditorLocator.Find(exe, p => p == Editor));
    }

    [Fact]
    public void FindsItRightNextToTheStartFolderToo() => Assert.Equal(Editor, EditorLocator.Find(Root, p => p == Editor));

    [Fact]
    public void ANormalDownloadedClientHasNoEditor() {
        var exe = Path.Combine(Path.GetTempPath(), "WarriorsAndWizards", "client");
        Assert.Null(EditorLocator.Find(exe, _ => false));
    }

    [Fact]
    public void GivesUpAfterTheGivenNumberOfLevels() {
        var deep = Path.Combine(Root, "a", "b", "c", "d");
        Assert.Null(EditorLocator.Find(deep, p => p == Editor, maxLevels: 2));
        Assert.Equal(Editor, EditorLocator.Find(deep, p => p == Editor, maxLevels: 4));
    }

    [Fact]
    public void EmptyStartIsHandled() => Assert.Null(EditorLocator.Find(string.Empty, _ => true));
}
