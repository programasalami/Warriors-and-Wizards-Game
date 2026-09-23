using Common.News;

namespace Common.Tests;

// The News Board's file format, and the shipped file itself.
public class PatchNotesTests {

    [Fact]
    public void AnEntryMayNameItsGameVersion() {
        var notes = PatchNotes.Parse("""
            ## 2026-09-23 - v0.3.6 - A release
            - a line
            ## 2026-09-22 - No version here
            - another
            ## 2026-09-21 - v1.10.2 - Two-digit parts
            ## 2026-09-20 - v is not a version - title
            """);
        Assert.Equal("0.3.6", notes[0].Version);
        Assert.Equal("A release", notes[0].Title);
        Assert.Equal("", notes[1].Version);
        Assert.Equal("No version here", notes[1].Title);
        Assert.Equal("1.10.2", notes[2].Version);
        Assert.Equal("", notes[3].Version);
        Assert.Equal("v is not a version - title", notes[3].Title);
    }

    [Fact]
    public void ParsesEntriesInFileOrderAndSkipsCommentsAndBlanks() {
        var notes = PatchNotes.Parse("""
            # a comment
            stray text before the first entry is ignored

            ## 2026-09-21 - Newest thing
            - first line
            - second line

            ## 2026-09-20 - Older thing
            plain line without a dash
            # comment inside an entry
            """);
        Assert.Equal(2, notes.Count);
        Assert.Equal("2026-09-21", notes[0].Date);
        Assert.Equal("Newest thing", notes[0].Title);
        Assert.Equal(["first line", "second line"], notes[0].Lines);
        Assert.Equal("Older thing", notes[1].Title);
        Assert.Equal(["plain line without a dash"], notes[1].Lines);
    }

    [Fact]
    public void AHeadingWithoutADateStillWorks() {
        var notes = PatchNotes.Parse("## Just a title\n- x");
        var note = Assert.Single(notes);
        Assert.Equal("", note.Date);
        Assert.Equal("Just a title", note.Title);
    }

    [Fact]
    public void EmptyOrMissingTextGivesNoEntries() {
        Assert.Empty(PatchNotes.Parse(""));
        Assert.Empty(PatchNotes.Parse(null));
        Assert.Empty(PatchNotes.Load("Resources/News/does-not-exist.txt"));
    }

    // The real file ships with the server (Common.csproj copies it next to the binaries): it must parse and have something to say.
    [Fact]
    public void TheShippedFileHasEntriesNewestFirst() {
        var notes = PatchNotes.Load();
        Assert.True(notes.Count >= 5, "the shipped patch notes should carry the last sessions");
        Assert.All(notes, n => Assert.False(string.IsNullOrWhiteSpace(n.Title)));
        Assert.All(notes, n => Assert.NotEmpty(n.Lines));
        Assert.True(string.CompareOrdinal(notes[0].Date, notes[^1].Date) >= 0, "newest entry first");
    }
}
