using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace IniMaster.Core;

public static class GameLocator
{
    public const string ExeName = "CrimsonDesert.exe";
    public const string ProcessName = "CrimsonDesert";
    private const string SteamFolder = "Crimson Desert";

    /// Accepts the install root, bin64, or the exe itself, and returns the
    /// install root. Null if CrimsonDesert.exe is not where it should be.
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
            if (File.Exists(Path.Combine(path, ExeName)))
                return Path.GetFileName(path).Equals("bin64", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : path;
            if (File.Exists(Path.Combine(path, "bin64", ExeName))) return path;
        }
        catch (Exception) { }
        return null;
    }

    /// The folder that holds the exe and the plugins.
    public static string BinFolder(string root)
    {
        var bin = Path.Combine(root, "bin64");
        return Directory.Exists(bin) ? bin : root;
    }

    public static string? Find()
    {
        foreach (var candidate in Candidates())
            if (Normalize(candidate) is { } found) return found;
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var lib in SteamLibraries())
            yield return Path.Combine(lib, "steamapps", "common", SteamFolder);
        foreach (var epic in EpicInstalls()) yield return epic;
        foreach (var drive in SafeDrives())
        {
            yield return Path.Combine(drive, "SteamLibrary", "steamapps", "common", SteamFolder);
            yield return Path.Combine(drive, "Steam", "steamapps", "common", SteamFolder);
            yield return Path.Combine(drive, "Games", SteamFolder);
            yield return Path.Combine(drive, "Program Files", "Epic Games", "CrimsonDesert");
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { yield break; }
        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        void TryKey(RegistryKey hive, string sub, string name)
        {
            try
            {
                using var k = hive.OpenSubKey(sub);
                if (k?.GetValue(name) is string s && s.Length > 0) roots.Add(s.Replace('/', '\\'));
            }
            catch { }
        }
        TryKey(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        TryKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        TryKey(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (seen.Add(root)) yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            string text;
            try { text = File.ReadAllText(vdf); } catch { continue; }
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(p)) yield return p;
            }
        }
    }

    private static IEnumerable<string> EpicInstalls()
    {
        var dir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        string[] files;
        try { files = Directory.GetFiles(dir, "*.item"); } catch { yield break; }
        foreach (var f in files)
        {
            string? location = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                var name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? "" : "";
                if (name.Contains("Crimson Desert", StringComparison.OrdinalIgnoreCase) && r.TryGetProperty("InstallLocation", out var l))
                    location = l.GetString();
            }
            catch { }
            if (location != null) yield return location;
        }
    }

    public static bool IsGameRunning()
    {
        var procs = Process.GetProcessesByName(ProcessName);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }
}
