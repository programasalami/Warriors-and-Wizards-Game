using System;
using System.Collections.Generic;
using System.IO;

namespace Common.News;

// The News Board's content (2026-09-21): Resources/News/PatchNotes.txt, a plain text file written at the end of every work session and shipped with
// the server. Format: "## <date> - <title>" starts an entry, "- <line>" adds a line to it, "#" lines are comments, blank lines are ignored.
// Entries stay in file order (newest first, by convention). The file is re-read when it changes on disk, so an edit needs no restart.
// 2026-09-22: an entry may name the game version it shipped in - "## <date> - v<version> - <title>" - for the release history on The Portal
// (website + in game). Entries without one stay valid.
public sealed record PatchNote(string Date, string Title, IReadOnlyList<string> Lines, string Version = "");

public static class PatchNotes {
    public const string DefaultPath = "Resources/News/PatchNotes.txt";

    private static readonly object Lock = new();
    private static string _loadedPath;
    private static DateTime _loadedWrite;
    private static IReadOnlyList<PatchNote> _loaded = [];

    public static IReadOnlyList<PatchNote> Parse(string text) {
        var notes = new List<PatchNote>();
        string date = null, title = null, version = "";
        var lines = new List<string>();

        void Flush() {
            if (title != null) {
                notes.Add(new PatchNote(date ?? "", title, lines.ToArray(), version));
            }

            date = title = null;
            version = "";
            lines = [];
        }

        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') && !line.StartsWith("##")) {
                continue;
            }

            if (line.StartsWith("##")) {
                Flush();
                var head = line.TrimStart('#').Trim();
                var dash = head.IndexOf(" - ", StringComparison.Ordinal);
                if (dash > 0) {
                    date = head[..dash].Trim();
                    title = head[(dash + 3)..].Trim();
                    (version, title) = SplitVersion(title);
                } else {
                    date = "";
                    title = head;
                }

                continue;
            }

            if (title == null) {
                continue;               // text before the first entry: ignored
            }

            lines.Add(line.StartsWith('-') ? line[1..].Trim() : line);
        }

        Flush();
        return notes;
    }

    // "v0.3.6 - Title" -> ("0.3.6", "Title"); anything else is a plain title with no version.
    public static (string Version, string Title) SplitVersion(string title) {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^v(\d+(?:\.\d+)+)\s+-\s+(.+)$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value.Trim()) : ("", title);
    }

    // The shipped file, cached until its timestamp changes. Missing or unreadable file = no entries (the board says so), never an exception.
    public static IReadOnlyList<PatchNote> Load(string path = DefaultPath) {
        lock (Lock) {
            try {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full)) {
                    return [];
                }

                var write = File.GetLastWriteTimeUtc(full);
                if (full != _loadedPath || write != _loadedWrite) {
                    _loaded = Parse(File.ReadAllText(full));
                    _loadedPath = full;
                    _loadedWrite = write;
                }

                return _loaded;
            } catch (Exception) {
                return _loaded;
            }
        }
    }
}
