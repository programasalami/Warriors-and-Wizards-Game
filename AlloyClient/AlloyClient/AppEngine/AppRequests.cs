using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using AlloyClient.Data;
using AlloyClient.Logging;
using Microsoft.Extensions.Logging;

namespace AlloyClient.AppEngine;

public struct AppResponse {
    public bool Success;
    public string Message;

    // The server answered and said no (wrong / unknown username or password), as opposed to not being reachable at all.
    public bool Rejected;
}

public static class AppRequests {

    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(AppRequests));

    // Whose character list is currently in GlobalData - set only when a /char/list fetch actually succeeded. The startup
    // check leaves the GUEST list behind when nobody is signed in, so "a list exists" doesn't mean "this login's list is
    // ready"; this does (see HasCharListFor).
    private static volatile string _charListUser;

    public static bool HasCharListFor(string username) => _charListUser == username;

    public static async Task Startup() {
        // First: whether this build is still the one the server accepts (the title screen shows a prompt if not).
        await VersionCheck.FetchAsync();

        var verify = await VerifyAsync();

        // A saved login the server does not know (typically credentials saved by another build of the client, e.g. a local
        // test server's account, now pointed at a different server) must not count as being signed in: the server answers
        // /char/list for an unverified login with its built-in "Guest" placeholder, which used to let PLAY walk straight
        // into the character book as a fake Guest account. Forgetting the login makes PLAY ask for a real sign-in instead.
        // (Only in memory - the saved file is left alone, and an unreachable server does not count as a rejection.)
        if (!verify.Success && verify.Rejected) {
            GlobalData.TryRemove<LoginData>(out _);
        }

        // A successful sign-in already fetched the list (see VerifyAsync); this covers the guest and failed-fetch cases.
        var login = GlobalData.Get<LoginData>() ?? LoginData.Default;
        if (!HasCharListFor(login.Username)) {
            await GetCharList();
        }
    }
    
    public static async Task<AppResponse> VerifyAsync() {
        var login = GlobalData.Get<LoginData>() ?? LoginData.Default;
        return await VerifyAsync(login.Username, login.Password);
    }

    public static async Task<AppResponse> VerifyAsync(string username, string password, bool saveInfo = false) {
        var response = await AppEngineClient.SendRequest("/account/verify", BuildAccountRequestData(username, password), 3);
        
        if (response == null) {
            return new AppResponse { Success = false, Message = "Failed to contact server." };
        }

        var xml = XElement.Parse(response);
        if (xml.Name.LocalName == "Error") {
            return new AppResponse { Success = false, Message = xml.Value, Rejected = true };
        }

        GlobalData.Add(new AccountData(xml));
        GlobalData.Add(new LoginData(username, password));
        
        if (saveInfo) {
            Settings.SaveLocalAccount();
        }

        // The character list rides along with the sign-in, so the book is ready by the time PLAY is pressed. If this
        // fails the sign-in still counts; PLAY then falls back to the loader (see LoaderFlows.FromTitleToCharacterList).
        try {
            await GetCharList();
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, e, "Character list fetch after sign-in failed");
        }

        return new AppResponse { Success = true };
    }

    public static async Task<AppResponse> Register(string username, string password) {
        var data = new Dictionary<string, string> {{"newUsername", username}, {"newPassword", password}};
        var request = await AppEngineClient.SendRequest("/account/register", data, 3);
        
        if (request == null) {
            return new AppResponse { Success = false, Message = "Failed to contact server." };
        }

        var result = XElement.Parse(request).Value;

        if (result != string.Empty) {
            return new AppResponse { Success = false, Message = result };
        }

        return await VerifyAsync(username, password, true);
    }
    
    public static async Task<AppResponse> PurchaseCharSlot() {
        var login = GlobalData.Get<LoginData>();

        if (login is null) {
            return new AppResponse { Success = false, Message = "Not logged in" };
        }
        var response = await AppEngineClient.SendRequest("/account/purchaseCharSlot", BuildAccountRequestData(login.Username, login.Password), 3);
        
        if (response == null) {
            return new AppResponse { Success = false, Message = "Failed to contact server." };
        }

        var result = XElement.Parse(response).Value;
        
        return result == string.Empty ? new AppResponse { Success = true } : new AppResponse { Success = false, Message = result };
    }
    
    // The book's DELETE button (2026-09-21): the server soft-deletes the character (it stays in the account, marked deleted, never listed again).
    public static Task<AppResponse> DeleteCharacter(int charId) => InGameAction("/char/delete", new() { { "charId", charId.ToString() } });

    //Fame

    public static async Task<AppResponse> GetCharList() {
        var login = GlobalData.Get<LoginData>() ?? LoginData.Default;
        var response = await AppEngineClient.SendRequest("/char/list", BuildAccountRequestData(login.Username, login.Password), 3);
        
        if (response == null) {
            return new AppResponse { Success = false, Message = "Failed to contact server." };
        }
        
        var xml = XElement.Parse(response);
        
        GlobalData.Add(new AccountData(xml.Element("Account")));
        GlobalData.Add(new CharacterListData(xml));
        GlobalData.Add(new NewsData(xml.Elements("NewsItem")));
        GlobalData.Add(new ServerListData(xml.Element("Servers")));
        _charListUser = login.Username;

        return new AppResponse{ Success = true };
    }
    
    private static Dictionary<string, string> BuildAccountRequestData(string username, string password) => new() {{"username", username}, {"password", password}};

    // ---- in-game features that talk to the account server: the Bug Board and the Jukebox in the Nexus --------------------------------------------------------------------------------------------------------------

    public struct BugBoardResult {
        public BugBoardData Data;
        public string Error;
    }

    public static async Task<BugBoardResult> GetBugBoard() {
        var response = await SendInGameRequest("/board/list", null);
        if (response == null) {
            return new BugBoardResult { Error = "Could not reach the server." };
        }

        return BugBoardData.TryParse(response, out var data)
            ? new BugBoardResult { Data = data }
            : new BugBoardResult { Error = "The Bug Board is not available on this server yet." };
    }

    public static Task<AppResponse> PostToBugBoard(string message) => InGameAction("/board/post", new() { { "message", message } });

    public static Task<AppResponse> DeleteBugPost(int id) => InGameAction("/board/delete", new() { { "id", id.ToString() } });

    public static Task<AppResponse> SetBugPostStatus(int id, string status) => InGameAction("/board/status", new() { { "id", id.ToString() }, { "status", status } });

    private static async Task<AppResponse> InGameAction(string endpoint, Dictionary<string, string> extra) {
        var response = await SendInGameRequest(endpoint, extra);
        if (response == null) {
            return new AppResponse { Success = false, Message = "Could not reach the server." };
        }

        try {
            var xml = XElement.Parse(response);
            if (xml.Name.LocalName == "Success") {
                return new AppResponse { Success = true };
            }

            return new AppResponse { Success = false, Message = string.IsNullOrWhiteSpace(xml.Value) ? "Something went wrong." : xml.Value };
        } catch (Exception) {
            return new AppResponse { Success = false, Message = "The Bug Board is not available on this server yet." };
        }
    }

    // Signed-in players send their login (guests send nothing, and can only read). Unlike the start-up requests, a failure here is only reported to the
    // board itself: it must not leave the "server offline" flag behind (that flag would pop a Retry dialog on the next title screen, and while it is set every
    // other request is refused).
    private static async Task<string> SendInGameRequest(string endpoint, Dictionary<string, string> extra) {
        var login = GlobalData.Get<LoginData>();
        var data = BuildAccountRequestData(login?.Username ?? string.Empty, login?.Password ?? string.Empty);
        if (extra != null) {
            foreach (var pair in extra) {
                data[pair.Key] = pair.Value;
            }
        }

        GlobalData.TryRemove<AppRequestFailedFlag>(out _);
        var response = await AppEngineClient.SendRequest(endpoint, data, 1);
        if (response == null) {
            GlobalData.TryRemove<AppRequestFailedFlag>(out _);
        }

        return response;
    }

    // ---- the Character Book: Inbox, Daily Gift, Daily Spin ------------------------------------------------------------------------------------------

    public struct InboxResult {
        public InboxData Data;
        public string Error;
    }

    public struct DailyResult {
        public DailyData Data;
        public string Error;
    }

    public struct RewardCall {
        public RewardResult Result;
        public string Error;
    }

    public static async Task<InboxResult> GetInbox() {
        var response = await SendInGameRequest("/inbox/list", null);
        if (response == null) {
            return new InboxResult { Error = "Could not reach the server." };
        }

        return InboxData.TryParse(response, out var data) ? new InboxResult { Data = data } : new InboxResult { Error = ErrorText(response, "The Inbox is not available on this server yet.") };
    }

    public static Task<AppResponse> MarkInboxRead(int id) => InGameAction("/inbox/read", new() { { "id", id.ToString() } });

    public static Task<AppResponse> DeleteInboxMessage(int id) => InGameAction("/inbox/delete", new() { { "id", id.ToString() } });

    public static Task<RewardCall> ClaimInboxMessage(int id) => RewardRequest("/inbox/claim", new() { { "id", id.ToString() } });

    public static async Task<DailyResult> GetDaily() {
        var response = await SendInGameRequest("/daily/status", null);
        if (response == null) {
            return new DailyResult { Error = "Could not reach the server." };
        }

        return DailyData.TryParse(response, out var data) ? new DailyResult { Data = data } : new DailyResult { Error = ErrorText(response, "The daily rewards are not available on this server yet.") };
    }

    public static Task<RewardCall> ClaimDailyGift() => RewardRequest("/daily/claim", null);

    public static Task<RewardCall> SpinDailyWheel() => RewardRequest("/daily/spin", null);

    private static async Task<RewardCall> RewardRequest(string endpoint, Dictionary<string, string> extra) {
        var response = await SendInGameRequest(endpoint, extra);
        if (response == null) {
            return new RewardCall { Error = "Could not reach the server." };
        }

        return RewardResult.TryParse(response, out var result) ? new RewardCall { Result = result } : new RewardCall { Error = ErrorText(response, "That is not available on this server yet.") };
    }

    // The sentence inside an <Error>...</Error> answer, or the fallback for anything else (an older server answers unknown paths with an empty page).
    public static string ErrorText(string response, string fallback) {
        try {
            var xml = XElement.Parse(response);
            if (xml.Name.LocalName == "Error" && !string.IsNullOrWhiteSpace(xml.Value)) {
                return xml.Value;
            }
        } catch (Exception) {
            // not xml: use the fallback
        }

        return fallback;
    }

    // ---- the News Board: the patch notes the server ships (Resources/News/PatchNotes.txt) ------------------------------------------------------------

    public struct NewsBoardResult {
        public NewsBoardData Data;
        public string Error;
    }

    public static async Task<NewsBoardResult> GetNewsBoard() {
        var response = await SendInGameRequest("/news/board", null);
        if (response == null) {
            return new NewsBoardResult { Error = "Could not reach the server." };
        }

        return NewsBoardData.TryParse(response, out var data)
            ? new NewsBoardResult { Data = data }
            : new NewsBoardResult { Error = "The News Board is not available on this server yet." };
    }

    // ---- the shared in-game music (the Jukebox) ----------------------------------------------------------------------------------------------------

    public struct MusicNowResult {
        public MusicNowData Data;
        public string Error;
    }

    public static async Task<MusicNowResult> GetMusicNow() {
        var response = await SendInGameRequest("/music/now", null);
        if (response == null) {
            return new MusicNowResult { Error = "Could not reach the server." };
        }

        return MusicNowData.TryParse(response, out var data)
            ? new MusicNowResult { Data = data }
            : new MusicNowResult { Error = "The shared music is not available on this server yet." };
    }

    public static Task<AppResponse> SkipMusic(bool forward) => InGameAction("/music/skip", new() { { "dir", forward ? "next" : "prev" } });

    public static Task<AppResponse> SetMusicTrack(string trackId) => InGameAction("/music/set", new() { { "track", trackId } });
}
