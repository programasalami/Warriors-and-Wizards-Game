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
    
    // Delete
    
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
}