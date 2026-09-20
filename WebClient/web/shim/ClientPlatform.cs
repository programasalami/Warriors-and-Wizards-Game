// Browser version of AlloyClient/Utils/ClientPlatform.cs (swapped in by tools/patch_rules_more.py).
using System;
using WarriorsWeb;

namespace AlloyClient.Utils;

public static class ClientPlatform {

    public const bool IsWeb = true;

    public static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static void OpenUrl(string url) {
        if (IsHttpUrl(url)) {
            WebHost.OpenUrl(url);
        }
    }
}
