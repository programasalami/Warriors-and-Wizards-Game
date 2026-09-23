using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace WaWClient.Data;

// One entry of the News Board (the patch notes the server ships): a date, a title and its lines.
public sealed record NewsEntry(string Date, string Title, IReadOnlyList<string> Lines, string Version = "");

// <News><Entry date title>line\nline</Entry>...</News>, newest first. Anything else is "no news".
public sealed record NewsBoardData(IReadOnlyList<NewsEntry> Entries) {
    public static bool TryParse(string response, out NewsBoardData data) {
        data = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName != "News") {
                return false;
            }

            var entries = new List<NewsEntry>();
            foreach (var e in root.Elements("Entry")) {
                var lines = new List<string>();
                foreach (var line in e.Value.Replace("\r\n", "\n").Split('\n')) {
                    if (!string.IsNullOrWhiteSpace(line)) {
                        lines.Add(line.Trim());
                    }
                }

                entries.Add(new NewsEntry((string)e.Attribute("date") ?? string.Empty, (string)e.Attribute("title") ?? string.Empty, lines,
                    (string)e.Attribute("version") ?? string.Empty));
            }

            data = new NewsBoardData(entries);
            return true;
        } catch (Exception) {
            return false;
        }
    }
}
