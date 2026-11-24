using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IniMaster.Core;

/// The newest release of another mod on GitHub.
public sealed record ModRelease(Version Version, string Tag, string PageUrl);

/// What a mod's metadata says to compare: its GitHub repository and the
/// version installed. Installed is null when neither the metadata nor the
/// plugin's file version gives one.
public sealed record ModUpdateSource(string Repo, Version? Installed, string? InstalledText);

/// Update checks for other mods. A mod takes part only when its maker puts a
/// "github" field in its metadata. Nothing is downloaded: INI Master reads the
/// newest release's tag and shows a badge when it is newer than the
/// installed version.
public static partial class ModUpdates
{
    /// "owner/name", or a github.com link to the repository or anything in
    /// it, as "owner/name". Null for anything else.
    public static string? ParseRepo(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        var m = RepoPattern().Match(t);
        if (!m.Success) return null;
        var owner = m.Groups["owner"].Value;
        var name = m.Groups["name"].Value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || name.Trim('.').Length == 0) return null;
        return $"{owner}/{name}";
    }

    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.)?github\.com/)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]{1,100})(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RepoPattern();

    /// The first of the mod's ini files whose metadata names a repository.
    /// The version comes from the same metadata, or from the plugin's file
    /// version when the metadata has none.
    public static ModUpdateSource? SourceOf(ModInfo mod)
    {
        foreach (var f in mod.Files)
        {
            if (ParseRepo(f.Meta.Github) is not { } repo) continue;
            var fromMeta = Updates.ParseVersion(f.Meta.Version);
            if (fromMeta != null) return new ModUpdateSource(repo, fromMeta, f.Meta.Version!.Trim());
            var fromFile = mod.AsiPath != null ? Updates.VersionOf(null, mod.AsiPath) : null;
            return new ModUpdateSource(repo, fromFile, fromFile?.ToString());
        }
        return null;
    }

    /// The raw "github" text of a mod that names one INI Master cannot read,
    /// for the log.
    public static string? UnreadableRepo(ModInfo mod) =>
        mod.Files.Select(f => f.Meta.Github).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g) && ParseRepo(g) == null);

    /// Reads GitHub's latest-release JSON. Null for a draft, a pre-release or
    /// a tag that is not a version.
    public static ModRelease? Parse(string json, string repo)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (Updates.ParseVersion(tag) is not { } version) return null;
        var page = root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
        return new ModRelease(version, tag!, Updates.IsGitHubUrl(page) ? page! : ReleasesPage(repo));
    }

    public static string ReleasesPage(string repo) => $"https://github.com/{repo}/releases/latest";

    /// The repository's newest release, or null when it has none.
    public static async Task<ModRelease?> LatestAsync(HttpClient http, string repo, CancellationToken cancel = default)
    {
        if (ParseRepo(repo) is not { } clean) throw new ArgumentException($"Not a GitHub repository: {repo}", nameof(repo));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{clean}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"INIMaster/{Updates.Current?.ToString() ?? "0"}");
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel), clean);
    }

    /// The release when it is newer than what is installed, otherwise null.
    public static ModRelease? NewerThan(ModRelease? release, Version? installed) =>
        release != null && installed != null && release.Version > installed ? release : null;
}
