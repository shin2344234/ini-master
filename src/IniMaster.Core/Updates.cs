using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace IniMaster.Core;

/// One release on GitHub.
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string ExeUrl, long Size, string? Sha256, string? Notes);

/// Checks GitHub for a newer INI Master and installs it after the person
/// says yes. Nothing is downloaded before that, and nothing is installed
/// unless the file GitHub serves is signed by the same certificate as the
/// copy already running.
public static class Updates
{
    public const string Repo = "shin2344234/ini-master";
    public const string AssetName = "INIMaster.exe";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    private static string LatestApi => $"https://api.github.com/repos/{Repo}/releases/latest";

    /// The running program's version.
    public static Version? Current => VersionOf(Assembly.GetEntryAssembly(), ExePath);

    /// The exe's own version first. This library carries none of its own, so
    /// reading the version here would report 1.0.0 and offer every release as
    /// an update.
    public static Version? VersionOf(Assembly? entry, string? exePath)
    {
        if (exePath != null && File.Exists(exePath))
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (Version.TryParse(info.FileVersion, out var fromFile) && fromFile.Major + fromFile.Minor + fromFile.Build > 0)
                return new Version(fromFile.Major, fromFile.Minor, fromFile.Build);
        }
        var v = entry?.GetName().Version;
        return v == null ? null : new Version(v.Major, v.Minor, v.Build);
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AssetName);

    /// Only these hosts, so a rewritten release cannot point the download
    /// somewhere else.
    public static bool IsGitHubUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// Reads the release JSON. Null when it has no usable exe to offer.
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (ParseVersion(tag) is not { } version) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var a in assets.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(a.TryGetProperty("name", out var n) ? n.GetString() : null, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!IsGitHubUrl(url)) continue;
            var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
            var sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new ReleaseInfo(version, tag!, IsGitHubUrl(page) ? page! : ReleasesPage, url!, size, sha, notes);
        }
        return null;
    }

    /// "v1.2.2" or "1.2.2" as a three part version.
    public static Version? ParseVersion(string? tag)
    {
        var t = (tag ?? "").Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        if (!Version.TryParse(t, out var v)) return null;
        return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }

    public static async Task<ReleaseInfo?> LatestAsync(HttpClient http, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"INIMaster/{Current?.ToString() ?? "0"}");
        using var response = await http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    /// Downloads the exe to the app data folder, checking the size and the
    /// SHA-256 GitHub reports for it. Returns the file.
    public static async Task<string> DownloadAsync(ReleaseInfo release, HttpClient http, IProgress<double>? progress, CancellationToken cancel = default)
    {
        if (!IsGitHubUrl(release.ExeUrl)) throw new InvalidOperationException("The download is not on GitHub.");
        var dir = Path.Combine(IniStore.AppDataFolder, "update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"INIMaster-{release.Version}.exe");

        using (var response = await http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;
            await using var from = await response.Content.ReadAsStreamAsync(cancel);
            await using var to = File.Create(path);
            var buffer = new byte[128 * 1024];
            long done = 0;
            int read;
            while ((read = await from.ReadAsync(buffer, cancel)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        var length = new FileInfo(path).Length;
        if (release.Size > 0 && length != release.Size)
            throw new InvalidOperationException($"The download is {length} bytes, not the {release.Size} the release lists.");
        if (release.Sha256 != null)
        {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The download does not match the checksum the release lists.");
        }
        return path;
    }

    /// Who signed an exe, or null when nobody did. CreateFromSignedFile is
    /// the only way to read an Authenticode signer, and its replacement does
    /// not cover signed files, so the obsolete warning stands.
    public static (string Thumbprint, string Subject)? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return (cert.GetCertHashString(HashAlgorithmName.SHA256), cert.Subject);
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// True when both files carry the same signing certificate. A copy of the
    /// program that is not signed, such as a local build, never updates
    /// itself, so nothing can replace it with a signed file from anywhere.
    public static bool SameSigner(string current, string downloaded)
    {
        var a = Signer(current);
        var b = Signer(downloaded);
        return a != null && b != null && a.Value.Thumbprint == b.Value.Thumbprint &&
               string.Equals(a.Value.Subject, b.Value.Subject, StringComparison.Ordinal);
    }

    /// Renames the running exe aside, puts the new one in its place and starts
    /// it. Windows allows renaming a running program, so no helper script and
    /// no second process are needed. The caller exits straight after.
    public static void Apply(string downloaded, string? exePath = null, bool restart = true)
    {
        var target = exePath ?? ExePath;
        if (!SameSigner(target, downloaded))
            throw new InvalidOperationException("The download is not signed by the same certificate as this copy.");
        var old = target + ".old";
        File.Delete(old);
        File.Move(target, old);
        try { File.Copy(downloaded, target); }
        catch
        {
            // Put the working copy back rather than leaving nothing behind.
            File.Move(old, target);
            throw;
        }
        File.Delete(downloaded);
        if (restart) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// Removes what the last update left behind. Called at startup, when the
    /// old file is no longer in use.
    public static void CleanUp(string? exePath = null)
    {
        try
        {
            File.Delete((exePath ?? ExePath) + ".old");
            var dir = Path.Combine(IniStore.AppDataFolder, "update");
            if (Directory.Exists(dir)) foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// False when the folder holding the exe cannot be written, as under
    /// Program Files without administrator rights.
    public static bool CanReplaceExe(string? exePath = null)
    {
        var target = exePath ?? ExePath;
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(target)!, $".inimaster-update-{Environment.ProcessId}.tmp");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
