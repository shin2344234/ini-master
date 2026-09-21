using System.IO;
using System.Text.Json;
using IniMaster.Core;

namespace IniMaster.Services;

/// The tool's own preferences, in %LOCALAPPDATA%\INIMaster\settings.json.
public sealed class AppSettings
{
    public string? GameRoot { get; set; }
    public bool ApplyInstantly { get; set; } = true;
    public bool ShowAdvanced { get; set; }
    public bool ShowIniOnly { get; set; } = true;
    public bool RawValues { get; set; }
    public string? LastMod { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 820;
    public double ListWidth { get; set; } = 280;
    public bool Maximized { get; set; }
    /// A language tag such as "de", or null to follow Windows.
    public string? Language { get; set; }

    private static string FilePath => Path.Combine(IniStore.AppDataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
