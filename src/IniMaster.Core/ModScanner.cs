namespace IniMaster.Core;

/// One entry in the mod list: a plugin and its ini, or an ini on its own.
public sealed class ModInfo
{
    public required string Id { get; init; }
    public string? AsiPath { get; init; }
    public required string BaseName { get; init; }
    public List<IniTarget> Files { get; } = new();
    public string DisplayName => Files.Select(f => f.Meta.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? BaseName;
    public bool HasPlugin => AsiPath != null;
    public MetaSource BestSource => Files.Count == 0 ? MetaSource.Guessed : Files.Max(f => f.Meta.Source);
}

/// One ini file with the metadata layers that apply to it, before the ini's
/// own comments are read. Those are read each time the file is loaded.
public sealed class IniTarget
{
    public required string IniPath { get; init; }
    public ModMeta Meta { get; set; } = new();
    public List<string> MetaSources { get; } = new();
    public List<string> MetaErrors { get; } = new();
    public bool Exists => File.Exists(IniPath);
    public string FileName => Path.GetFileName(IniPath);
}

public static class ModScanner
{
    public const string SidecarExtension = ".inimeta";

    /// Ini files that are not settings a person would edit.
    private static readonly HashSet<string> IgnoredInis = new(StringComparer.OrdinalIgnoreCase)
    {
        "imgui.ini", "desktop.ini",
    };

    /// bin64 plus the two subfolders Ultimate ASI Loader also loads from.
    public static List<string> PluginFolders(string gameRoot)
    {
        var bin = GameLocator.BinFolder(gameRoot);
        var list = new List<string> { bin };
        foreach (var sub in new[] { "scripts", "plugins" })
        {
            var p = Path.Combine(bin, sub);
            if (Directory.Exists(p)) list.Add(p);
        }
        return list;
    }

    /// <param name="communityFolder">Extra folder of .inimeta files shipped with
    /// the tool, for mods whose authors have not written one.</param>
    public static List<ModInfo> Scan(string gameRoot, string? communityFolder)
    {
        var folders = PluginFolders(gameRoot);
        var bin = folders[0];
        var inis = new List<string>();
        var asis = new List<string>();
        foreach (var f in folders)
        {
            inis.AddRange(SafeFiles(f, "*.ini").Where(p => !IgnoredInis.Contains(Path.GetFileName(p))));
            asis.AddRange(SafeFiles(f, "*.asi"));
        }
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<ModInfo>();

        foreach (var asi in asis.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileNameWithoutExtension(asi);
            var mod = new ModInfo { Id = asi, AsiPath = asi, BaseName = baseName };
            var embedded = new List<ModMeta>();
            var errors = new List<string>();
            try
            {
                foreach (var found in AsiMetaReader.Read(asi))
                {
                    try { embedded.Add(MetaLoader.Load(found.Text, MetaSource.Embedded, $"{Path.GetFileName(asi)} ({found.Where})")); }
                    catch (Exception ex) { errors.Add($"{Path.GetFileName(asi)} ({found.Where}): {ex.Message}"); }
                }
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(asi)}: {ex.Message}"); }

            var asiDir = Path.GetDirectoryName(asi)!;
            var targets = new Dictionary<string, IniTarget>(StringComparer.OrdinalIgnoreCase);
            IniTarget TargetFor(string? iniName)
            {
                var name = string.IsNullOrWhiteSpace(iniName) ? baseName + ".ini" : iniName!;
                var path = ResolveIni(name, asiDir, bin);
                if (!targets.TryGetValue(path, out var t))
                {
                    t = new IniTarget { IniPath = path };
                    targets[path] = t;
                }
                return t;
            }

            // The default target always exists so a plugin with no ini still
            // shows, and its metadata can create one.
            var main = TargetFor(null);
            var perTarget = new Dictionary<IniTarget, List<ModMeta>> { [main] = new() };
            foreach (var m in embedded)
            {
                var t = TargetFor(m.Ini);
                t.Meta.OverlayWith(m);
                t.MetaSources.Add(m.SourceDescription!);
                if (!perTarget.TryGetValue(t, out var list)) perTarget[t] = list = new();
                list.Add(m);
            }
            main.MetaErrors.AddRange(errors);

            foreach (var t in targets.Values)
            {
                if (t != main && !File.Exists(t.IniPath) && t.Meta.DefaultIniText == null && t.Meta.Sections.Count == 0) continue;
                // Sidecars sit under the embedded layer, so apply them first
                // and put the plugin's own metadata back on top.
                ApplySidecars(t, new[] { asiDir, bin }, Path.GetFileNameWithoutExtension(t.IniPath), baseName, communityFolder, perTarget.GetValueOrDefault(t) ?? new());
                mod.Files.Add(t);
                claimed.Add(t.IniPath);
            }
            mods.Add(mod);
        }

        foreach (var ini in inis.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (claimed.Contains(ini)) continue;
            var baseName = Path.GetFileNameWithoutExtension(ini);
            var mod = new ModInfo { Id = ini, BaseName = baseName };
            var t = new IniTarget { IniPath = ini };
            ApplySidecars(t, new[] { Path.GetDirectoryName(ini)! }, baseName, null, communityFolder, new List<ModMeta>());
            mod.Files.Add(t);
            mods.Add(mod);
        }
        return mods;
    }

    private static void ApplySidecars(IniTarget t, IEnumerable<string> dirs, string iniBase, string? asiBase,
        string? communityFolder, List<ModMeta> embedded)
    {
        var candidates = new List<string>();
        foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(d, iniBase + SidecarExtension));
            if (asiBase != null) candidates.Add(Path.Combine(d, asiBase + SidecarExtension));
        }
        if (communityFolder != null)
        {
            candidates.Add(Path.Combine(communityFolder, iniBase + SidecarExtension));
            if (asiBase != null) candidates.Add(Path.Combine(communityFolder, asiBase + SidecarExtension));
        }

        var sidecar = new ModMeta();
        var used = new List<string>();
        // First hit wins: a sidecar next to the ini beats the community copy.
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = TextCodec.Decode(File.ReadAllBytes(path), out _);
                var m = MetaLoader.Load(text, MetaSource.Sidecar, Path.GetFileName(path) + (communityFolder != null && path.StartsWith(communityFolder, StringComparison.OrdinalIgnoreCase) ? " (bundled with INI Master)" : ""));
                if (m.Ini != null && !m.Ini.Equals(Path.GetFileName(t.IniPath), StringComparison.OrdinalIgnoreCase)) continue;
                sidecar = m;
                used.Add(m.SourceDescription!);
                break;
            }
            catch (Exception ex) { t.MetaErrors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (used.Count == 0) return;

        var merged = new ModMeta();
        merged.OverlayWith(sidecar);
        foreach (var e in embedded) merged.OverlayWith(e);
        t.Meta = merged;
        t.MetaSources.Insert(0, used[0]);
    }

    private static string ResolveIni(string name, string asiDir, string bin)
    {
        if (Path.IsPathRooted(name)) return name;
        var beside = Path.GetFullPath(Path.Combine(asiDir, name));
        if (File.Exists(beside)) return beside;
        var inBin = Path.GetFullPath(Path.Combine(bin, name));
        return File.Exists(inBin) ? inBin : beside;
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try
        {
            // "*.asi" alone would also match Foo.asi_off, a disabled plugin.
            var ext = pattern.TrimStart('*');
            return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly)
                .Where(p => Path.GetExtension(p).Equals(ext, StringComparison.OrdinalIgnoreCase));
        }
        catch { return Array.Empty<string>(); }
    }
}
