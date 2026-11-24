using System.IO;
using IniMaster.Core;

namespace IniMaster.Services;

/// %LOCALAPPDATA%\INIMaster\INIMaster.log. Only the update check for other
/// mods writes to it, so a person who leaves that off never gets the file.
/// The first line of a session moves the last session's log to
/// INIMaster.01.log. Lines come a handful per check, so each is appended
/// straight away.
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _started;

    public static string FilePath => Path.Combine(IniStore.AppDataFolder, "INIMaster.log");
    public static string PreviousPath => Path.Combine(IniStore.AppDataFolder, "INIMaster.01.log");

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                if (!_started)
                {
                    _started = true;
                    if (File.Exists(FilePath)) File.Move(FilePath, PreviousPath, overwrite: true);
                    File.AppendAllText(FilePath, $"{Stamp()} INI Master {Updates.Current?.ToString() ?? "?"} log started\r\n");
                }
                File.AppendAllText(FilePath, $"{Stamp()} {line}\r\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
}
