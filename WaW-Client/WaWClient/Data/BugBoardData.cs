using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace WaWClient.Data;

public sealed record BugPost(int Id, string Author, string Status, long CreatedAt, string Message);

// What the account server's /board/list says: the newest bug reports, and whether the signed-in player may moderate them (admins only).
public sealed record BugBoardData(bool CanModerate, IReadOnlyList<BugPost> Posts) {

    public const string StatusNew = "new";
    public const string StatusConfirmed = "confirmed";
    public const string StatusFixed = "fixed";

    // <Board canModerate="true"><Post id="1" author="x" status="new" created="1700000000">text</Post>...</Board>
    // Anything else (an <Error>, an empty answer from an older server, broken XML) is "not a board".
    public static bool TryParse(string response, out BugBoardData data) {
        data = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName != "Board") {
                return false;
            }

            var posts = new List<BugPost>();
            foreach (var element in root.Elements("Post")) {
                if (!int.TryParse((string)element.Attribute("id"), out var id) || !long.TryParse((string)element.Attribute("created"), out var created)) {
                    continue;     // one damaged entry must not hide the rest
                }

                posts.Add(new BugPost(id, (string)element.Attribute("author") ?? string.Empty, (string)element.Attribute("status") ?? StatusNew, created, element.Value));
            }

            data = new BugBoardData((string)root.Attribute("canModerate") == "true", posts);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    // "just now", "5 min ago", "3 h ago", "2 d ago"
    public static string Ago(long createdUnix, long nowUnix) {
        var seconds = nowUnix - createdUnix;
        if (seconds < 60) {
            return "just now";
        }

        if (seconds < 3600) {
            return $"{seconds / 60} min ago";
        }

        if (seconds < 86400) {
            return $"{seconds / 3600} h ago";
        }

        return $"{seconds / 86400} d ago";
    }

    public static string StatusLabel(string status) => status switch {
        StatusConfirmed => "CONFIRMED",
        StatusFixed => "FIXED",
        _ => "NEW"
    };
}
