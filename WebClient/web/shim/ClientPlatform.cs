// Browser version of AlloyClient/Utils/ClientPlatform.cs (swapped in by tools/patch_rules_more.py).
using System;
using WarriorsWeb;

namespace AlloyClient.Utils;

public static class ClientPlatform {

    public const bool IsWeb = true;

    public static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // A browser page cannot open files on the player's PC: there is no editor here.
    public static bool OpenLocalPage(string path) => false;

    // Reload the page = load the newest build (the update path for browser players).
    public static void ReloadPage() => WebHost.ReloadPage();

    public static void OpenUrl(string url) {
        if (IsHttpUrl(url)) {
            WebHost.OpenUrl(url);
        }
    }
}
