#region

using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Xml.Linq;
using Common.Database;
using Common.Utilities;

#endregion

namespace AccountServer.Systems.Board;

// The Bug Board in the Nexus. Reading is open to every client (the response only says whether the caller may moderate); posting needs a real, signed-in
// account; deleting and marking posts needs an admin account. Everything written is plain text, cleaned and rate limited by BugBoardRules.
internal static class BoardAuth {
    // The signed-in (non-guest) account that made the request, or null.
    public static async Task<Common.Database.Models.Account> VerifyAsync(NameValueCollection query) {
        var username = query["username"];
        var password = query["password"];
        if (string.IsNullOrWhiteSpace(username) || password == null) {
            return null;
        }

        var verify = await DbClient.VerifyAccount(username, password, Guid.Empty);
        return verify.Acc;
    }
}

public class BoardList : RequestHandler {
    public override string Path => "/board/list";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        var posts = await BugBoardDb.ListAsync();

        var root = new XElement("Board", new XAttribute("canModerate", acc != null && Ranks.IsModerator(acc) ? "true" : "false"));
        foreach (var post in posts) {
            root.Add(new XElement("Post",
                new XAttribute("id", post.Id),
                new XAttribute("author", post.Author ?? string.Empty),
                new XAttribute("status", post.Status ?? BugBoardRules.StatusNew),
                new XAttribute("created", post.CreatedAt),
                post.Message));
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }
}

public class BoardPost : RequestHandler {
    private static readonly Logger Log = new(typeof(BoardPost));

    public override string Path => "/board/post";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null) {
            return WriteError("Sign in to post on the Bug Board.");
        }

        var message = BugBoardRules.Clean(query["message"]);
        if (message.Length == 0) {
            return WriteError("Write something first.");
        }

        var problem = BugBoardRules.CheckRate(await BugBoardDb.RecentTimesAsync(acc.Id), await BugBoardDb.NowUnixAsync());
        if (problem != null) {
            return WriteError(problem);
        }

        var id = await BugBoardDb.AddAsync(acc.Id, acc.Name, message);
        Log.Info($"Bug Board: {acc.Name} posted #{id}");
        return WriteSuccess();
    }
}

public class BoardDelete : RequestHandler {
    private static readonly Logger Log = new(typeof(BoardDelete));

    public override string Path => "/board/delete";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null || !Ranks.IsModerator(acc)) {
            return WriteError("Only admins can remove posts.");
        }

        if (!int.TryParse(query["id"], out var id) || !await BugBoardDb.DeleteAsync(id)) {
            return WriteError("That post no longer exists.");
        }

        Log.Info($"Bug Board: {acc.Name} deleted #{id}");
        return WriteSuccess();
    }
}

public class BoardStatus : RequestHandler {
    private static readonly Logger Log = new(typeof(BoardStatus));

    public override string Path => "/board/status";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null || !Ranks.IsModerator(acc)) {
            return WriteError("Only admins can mark posts.");
        }

        var status = query["status"];
        if (!BugBoardRules.IsValidStatus(status)) {
            return WriteError("Unknown status.");
        }

        if (!int.TryParse(query["id"], out var id) || !await BugBoardDb.SetStatusAsync(id, status)) {
            return WriteError("That post no longer exists.");
        }

        Log.Info($"Bug Board: {acc.Name} marked #{id} as {status}");
        return WriteSuccess();
    }
}
