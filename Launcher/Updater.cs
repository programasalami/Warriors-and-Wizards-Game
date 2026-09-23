using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace WarriorsAndWizards.Launcher;

// Download an archive with progress, verify its SHA-256, unpack it into a fresh folder, then swap it in. The game folder is never
// half-updated: everything lands in <target>.new first and the swap is two renames. Player settings live outside the game folder
// (the client keeps them in the local app-data folder), so they survive every update.
public static class Updater {
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static async Task DownloadAsync(string url, string destination, long expectedSize, Action<long, long> progress) {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? expectedSize;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var buffer = new byte[1 << 16];
        long done = 0;
        var lastReport = DateTime.MinValue;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0) {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 150) {
                progress(done, total);
                lastReport = DateTime.UtcNow;
            }
        }
        progress(done, total);
    }

    public static string Sha256Of(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool HashMatches(string path, string expectedSha256) =>
        !string.IsNullOrWhiteSpace(expectedSha256) &&
        string.Equals(Sha256Of(path), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);

    // .zip (Windows client) or .tar.gz (Linux client) into an empty folder.
    public static void Extract(string archive, string intoFolder) {
        if (Directory.Exists(intoFolder))
            Directory.Delete(intoFolder, true);
        Directory.CreateDirectory(intoFolder);

        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            ZipFile.ExtractToDirectory(archive, intoFolder, overwriteFiles: true);
        }
        else if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, intoFolder, overwriteFiles: true);
        }
        else {
            throw new InvalidDataException($"Unknown archive type: {Path.GetFileName(archive)}");
        }
    }

    // Replace <target> with <fresh> using renames only, keeping the old folder until the new one is in place.
    public static void Swap(string fresh, string target) {
        var old = target + ".old";
        if (Directory.Exists(old))
            Directory.Delete(old, true);
        if (Directory.Exists(target))
            Directory.Move(target, old);
        Directory.Move(fresh, target);
        if (Directory.Exists(old)) {
            try { Directory.Delete(old, true); } catch (IOException) { /* a file still open: the next update removes it */ }
        }
    }

    public static void MakeExecutable(string path) {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    public static string Human(long bytes) => bytes switch {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F0} KB",
        _ => $"{bytes} B"
    };
}
