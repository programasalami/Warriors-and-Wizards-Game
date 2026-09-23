using System;
using System.Diagnostics;
using WaWClient.Logging;
using Microsoft.Extensions.Logging;

namespace WaWClient.Utils;

// The few things that differ between the desktop client and the browser build (which swaps this file for its own - see WebClient/tools/patch_rules_more.py).
public static class ClientPlatform {

    private static readonly ILogger Logger = ILogger.CreateLogger(nameof(ClientPlatform));

    public const bool IsWeb = false;

    // Only ever opens web links: the address can come from the server's config, so it is not launched as anything else.
    public static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Opens a page from this PC's own disk (the developer editor in Tools/Editor) in the default browser. Only .html files that exist are opened.
    public static bool OpenLocalPage(string path) {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) {
            return false;
        }

        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        } catch (Exception e) {
            Logger.Log(LogLevel.Warning, e, $"Could not open {path}");
            return false;
        }
    }

    // Only meaningful in the browser build (its shim reloads the page); the desktop client has nothing to reload.
    public static void ReloadPage() { }

    public static void OpenUrl(string url) {
        if (!IsHttpUrl(url)) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        } catch (Exception e) {
            Logger.Log(LogLevel.Warning, e, $"Could not open {url}");
        }
    }
}
