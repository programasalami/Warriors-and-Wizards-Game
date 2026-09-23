using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarriorsAndWizards.Launcher;

// What the deploy script publishes at <DownloadBase>/manifest.json (see deploy.ps1 Update-Manifest). The launcher trusts only
// this file: which version is current, which archive to fetch for this platform, and its SHA-256 to verify the download.
public sealed class Manifest {
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("windows")] public ArchiveInfo? Windows { get; set; }
    [JsonPropertyName("linux")] public ArchiveInfo? Linux { get; set; }
    [JsonPropertyName("launcher")] public LauncherInfo? Launcher { get; set; }

    public ArchiveInfo? ForThisPlatform() => OperatingSystem.IsWindows() ? Windows : Linux;

    // Tolerates a UTF-8 byte-order mark: a manifest written by Windows PowerShell once carried one and the parser refused it.
    public static Manifest? Parse(string json) => JsonSerializer.Deserialize(json.TrimStart('\uFEFF'), ManifestJsonContext.Default.Manifest);
}

public sealed class ArchiveInfo {
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public sealed class LauncherInfo {
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("windows")] public ArchiveInfo? Windows { get; set; }
    [JsonPropertyName("linux")] public ArchiveInfo? Linux { get; set; }

    public ArchiveInfo? ForThisPlatform() => OperatingSystem.IsWindows() ? Windows : Linux;
}

// Source-generated JSON so the trimmed single-file build needs no reflection.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Manifest))]
internal partial class ManifestJsonContext : JsonSerializerContext { }

public static class Versions {
    // "0.3.5" vs "0.3.10": numeric per part; anything unparsable compares as text. Different means an update is due.
    public static bool Differ(string? installed, string? current) {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(current))
            return true;
        return !string.Equals(installed.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // "2.0.10" is newer than "2.0.9". Used for the launcher itself, which must never move DOWN to an older manifest entry
    // (launcher 1.0.0 compared with Differ and would have downgraded a newer launcher tested against an older manifest).
    public static bool IsNewer(string? candidate, string? current) {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;
        if (string.IsNullOrWhiteSpace(current))
            return true;
        if (Version.TryParse(candidate.Trim(), out var a) && Version.TryParse(current.Trim(), out var b))
            return a > b;
        return false;       // unparsable: do nothing rather than guess
    }
}
