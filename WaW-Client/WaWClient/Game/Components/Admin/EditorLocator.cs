using System;
using System.IO;

namespace WaWClient.Game.Components.Admin;

// Finds the developer editor (Tools/Editor/editor.html in the source folder) from where the client runs. In the everyday source folder the exe sits a few levels
// below the repo root (WaW-Client/WaWClient/bin/Debug/net10.0), so it walks up until it finds the tool. The zip that testers download does not contain the
// tool, so there the answer is "not found" - the editor is for the developer's own machine.
public static class EditorLocator {
    public const string RelativePath = "Tools/Editor/editor.html";

    public static string Find(string startDirectory, Func<string, bool> fileExists = null, int maxLevels = 10) {
        fileExists ??= File.Exists;
        var dir = startDirectory;
        for (var i = 0; i <= maxLevels && !string.IsNullOrEmpty(dir); i++) {
            var candidate = Path.Combine(dir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (fileExists(candidate)) {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }
}
