using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using AlloyClient.Display;
using AlloyClient.Logging;
using AlloyClient.Ui.Components.Dialogs;
using AlloyClient.Utils;
using Microsoft.Extensions.Logging;

namespace AlloyClient.AppEngine;

// Which build the server currently accepts, versus the build this client is. The game server rejects a Hello whose BuildVersion differs from
// its own (Failure.INCORRECT_VERSION), so a mismatched client can never play - this finds out up front (the account server's /app/version,
// asked once at start-up) so the title screen can say so, instead of the player finding out at connect time.
// Deliberately not in GlobalData: signing out clears that, and this has nothing to do with who is signed in.
public static class VersionCheck {

    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(VersionCheck));

    // GameServer's Failure.INCORRECT_VERSION; its description is the server's version.
    public const int IncorrectVersionFailureId = 1;

    // Null until the server has told us (an older server without /app/version, or one we couldn't reach, leaves it null - not a mismatch).
    public static string ServerVersion { get; private set; }
    public static string DownloadUrl { get; private set; }

    public static bool IsOutdated => IsOutdatedFor(Settings.BuildVersion, ServerVersion);

    // Same exact-match rule the game server applies to Hello.
    public static bool IsOutdatedFor(string clientVersion, string serverVersion) =>
        !string.IsNullOrWhiteSpace(serverVersion) && serverVersion != clientVersion;

    // This client is NEWER than the server (a new build went out before the server was updated): nothing for the player to download, the server just has to catch up.
    // Unparseable versions never count as "newer" (they fall back to the plain out-of-date message).
    public static bool IsClientNewerThanServer => IsClientNewerFor(Settings.BuildVersion, ServerVersion);

    public static bool IsClientNewerFor(string clientVersion, string serverVersion) =>
        Version.TryParse(clientVersion, out var client) && Version.TryParse(serverVersion, out var server) && client > server;

    public static async Task FetchAsync() {
        var response = await AppEngineClient.SendRequest("/app/version", null, 2);
        if (TryParse(response, out var version, out var downloadUrl)) {
            ServerVersion = version;
            DownloadUrl = downloadUrl;
            Logger.Log(LogLevel.Information, $"Server build {version}, this client {Settings.BuildVersion}{(IsOutdated ? " - OUT OF DATE" : "")}");
        } else if (response != null) {
            Logger.Log(LogLevel.Information, "The server did not report a build version (older server?) - not treating this client as outdated.");
        }
    }

    // The game server itself turned this client away at Hello: its failure text is the version it wants.
    public static void ReportServerVersion(string serverVersion) {
        if (!string.IsNullOrWhiteSpace(serverVersion)) {
            ServerVersion = serverVersion;
        }
    }

    // <Version downloadUrl="...">0.3.3</Version>
    public static bool TryParse(string response, out string version, out string downloadUrl) {
        version = null;
        downloadUrl = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var xml = XElement.Parse(response);
            if (xml.Name.LocalName != "Version" || string.IsNullOrWhiteSpace(xml.Value)) {
                return false;
            }

            version = xml.Value.Trim();
            downloadUrl = (string)xml.Attribute("downloadUrl");
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public static string BuildMessage(string clientVersion, string serverVersion, bool isWeb, bool canDownload) {
        var versions = $"(yours: {clientVersion}, current: {serverVersion})";
        var download = canDownload ? "download the newest desktop client" : "get the newest desktop client";

        return isWeb
            ? $"This web version is out of date {versions}, so it can't connect until it is updated. Try refreshing the page, or {download} to play right now."
            : $"Your client is out of date {versions}, so it can't connect. Please {download}.";
    }

    public static string BuildNewerThanServerMessage(string clientVersion, string serverVersion) =>
        $"This build ({clientVersion}) is newer than the server ({serverVersion}) - the server has not been updated to it yet. Please try again in a few minutes.";

    // The "you need to update" prompt, or null when this client is up to date.
    public static Dialog CreateDialog() {
        if (!IsOutdated) {
            return null;
        }

        if (IsClientNewerThanServer) {
            return new Dialog("Server not updated yet", BuildNewerThanServerMessage(Settings.BuildVersion, ServerVersion), new DialogOption("OK"));
        }

        var url = DownloadUrl;
        var canDownload = ClientPlatform.IsHttpUrl(url);
        var message = BuildMessage(Settings.BuildVersion, ServerVersion, ClientPlatform.IsWeb, canDownload);

        // In the browser a reload IS the update (the page loads the new build): offer it as the first button.
        if (ClientPlatform.IsWeb) {
            return new Dialog("Update required", $"A new version of the game is out (yours: {Settings.BuildVersion}, current: {ServerVersion}). Refresh the page to play it.",
                new DialogOption("Refresh", ClientPlatform.ReloadPage), new DialogOption("Later"));
        }

        return canDownload
            ? new Dialog("Update required", message, new DialogOption("Download", () => ClientPlatform.OpenUrl(url)), new DialogOption("Close"))
            : new Dialog("Update required", message, new DialogOption("OK"));
    }

    // Puts the prompt up (once at a time). Returns true when the client is outdated, so callers can stop what they were about to do.
    public static bool ShowDialogIfOutdated() {
        if (!IsOutdated) {
            return false;
        }

        if (!DialogManager.Busy) {
            DialogManager.Enqueue(CreateDialog());
        }

        return true;
    }
}
