using System.Globalization;
using System.IO;
using IniMaster.Core;

namespace IniMaster.Localization;

/// Where translations live and which one to use.
public static class Languages
{
    /// Searched in this order: a folder in the user's app data, which survives
    /// updates, then a lang folder beside the exe, then the exe's own folder.
    public static IReadOnlyList<string> Folders { get; } = new[]
    {
        UserFolder,
        Path.Combine(AppContext.BaseDirectory, "lang"),
        AppContext.BaseDirectory,
    };

    public static string UserFolder => Path.Combine(IniStore.AppDataFolder, "lang");

    public static string WindowsTag => Loc.Normalize(CultureInfo.CurrentUICulture.Name);

    /// Null or empty follows Windows.
    public static void Apply(string? setting) =>
        Loc.Use(string.IsNullOrWhiteSpace(setting) ? WindowsTag : setting, Folders);

    /// True for a right-to-left language that has a translation loaded, so an
    /// English fallback keeps its left-to-right layout.
    public static bool IsRightToLeft
    {
        get
        {
            if (Loc.SourcePath == null) return false;
            try { return CultureInfo.GetCultureInfo(Loc.Language).TextInfo.IsRightToLeft; }
            catch (CultureNotFoundException) { return false; }
        }
    }

    /// Puts INIMaster.template.txt in the user's language folder, refreshed
    /// from the copy built into the exe, and returns the folder.
    public static string PrepareUserFolder()
    {
        Directory.CreateDirectory(UserFolder);
        var asm = typeof(Languages).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Loc.TemplateName, StringComparison.OrdinalIgnoreCase));
        if (name != null)
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var f = File.Create(Path.Combine(UserFolder, Loc.TemplateName));
            s.CopyTo(f);
        }
        return UserFolder;
    }
}
