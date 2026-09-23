using System.Collections.Generic;
using System.Threading.Tasks;
using AlloyClient.Data;

namespace AlloyClient.AppEngine;

// The in-client Portal's calls to the account server's public API (/public/...): the same read-only answers the website gets. No login is sent
// or needed. A failure is only reported to the Portal page itself - it never leaves the "server offline" flag behind (see SendInGameRequest in
// AppRequests for why), so a dead Portal call cannot pop a Retry dialog on the next title screen.
public static class PortalRequests {

    public const string Offline = "Could not reach the server.";
    public const string Older = "The Portal is not available on this server yet.";

    public struct Result<T> where T : class {
        public T Data;
        public string Error;
    }

    public static async Task<Result<PortalReleases>> GetReleases() {
        var response = await Send("/public/releases", null);
        return response != null && PortalData.TryParseReleases(response, out var data) ? new Result<PortalReleases> { Data = data } : Fail<PortalReleases>(response);
    }

    public static async Task<Result<PortalOnline>> GetOnline() {
        var response = await Send("/public/online", null);
        return response != null && PortalData.TryParseOnline(response, out var data) ? new Result<PortalOnline> { Data = data } : Fail<PortalOnline>(response);
    }

    public static async Task<Result<PortalProfile>> GetPlayer(string name) {
        var response = await Send("/public/player", new() { { "name", name } });
        return response != null && PortalData.TryParseProfile(response, out var data) ? new Result<PortalProfile> { Data = data } : Fail<PortalProfile>(response);
    }

    public static async Task<Result<List<string>>> Search(string prefix) {
        var response = await Send("/public/search", new() { { "q", prefix } });
        return response != null && PortalData.TryParseNames(response, out var data) ? new Result<List<string>> { Data = data } : Fail<List<string>>(response);
    }

    // kind: fame | chars | level | guilds
    public static async Task<Result<List<PortalRow>>> GetLeaderboard(string kind) {
        var response = await Send("/public/leaderboard", new() { { "kind", kind } });
        return response != null && PortalData.TryParseRows(response, out var data) ? new Result<List<PortalRow>> { Data = data } : Fail<List<PortalRow>>(response);
    }

    public static async Task<Result<PortalGuild>> GetGuild(string name) {
        var response = await Send("/public/guild", new() { { "name", name } });
        return response != null && PortalData.TryParseGuild(response, out var data) ? new Result<PortalGuild> { Data = data } : Fail<PortalGuild>(response);
    }

    private static Result<T> Fail<T>(string response) where T : class =>
        new() { Error = response == null ? Offline : PortalData.ErrorOf(response) ?? Older };

    private static async Task<string> Send(string endpoint, Dictionary<string, string> data) {
        GlobalData.TryRemove<AppRequestFailedFlag>(out _);
        var response = await AppEngineClient.SendRequest(endpoint, data ?? new Dictionary<string, string>(), 1);
        if (response == null) {
            GlobalData.TryRemove<AppRequestFailedFlag>(out _);
        }

        return response;
    }
}
