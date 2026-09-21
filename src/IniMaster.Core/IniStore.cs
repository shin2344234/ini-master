using System.Security.Cryptography;

namespace IniMaster.Core;

/// Reads and writes ini files next to a running game. A plugin may read the
/// file at any moment and may write it too, so every save starts from the
/// bytes on disk and changes only the keys the person edited.
public static class IniStore
{
    public static string AppDataFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "INIMaster");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static byte[]? ReadBytes(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
                return bytes;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 5) { Thread.Sleep(40); }
        }
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public sealed record SaveResult(bool Changed, byte[] Bytes, IniDocument Document, string? BackupPath);

    /// Applies edits to the current file and writes it back. Keys missing from
    /// the file are added to their section.
    public static SaveResult Save(string path, IReadOnlyDictionary<(string Section, string Key), string> edits, bool backup)
    {
        var current = ReadBytes(path);
        var doc = current == null ? IniDocument.Parse("") : IniDocument.Load(current);
        foreach (var ((section, key), value) in edits) doc.Set(section, key, value);
        var bytes = doc.ToBytes();
        if (current != null && bytes.AsSpan().SequenceEqual(current)) return new SaveResult(false, bytes, doc, null);
        string? backupPath = null;
        if (backup && current != null) backupPath = Backup(path, current);
        WriteAtomic(path, bytes);
        return new SaveResult(true, bytes, doc, backupPath);
    }

    public static SaveResult SaveText(string path, string text, bool backup)
    {
        var current = ReadBytes(path);
        var encoding = current != null ? IniDocument.Load(current).Encoding : new System.Text.UTF8Encoding(false);
        var doc = IniDocument.Parse(text, encoding);
        var bytes = doc.ToBytes();
        if (current != null && bytes.AsSpan().SequenceEqual(current)) return new SaveResult(false, bytes, doc, null);
        string? backupPath = null;
        if (backup && current != null) backupPath = Backup(path, current);
        WriteAtomic(path, bytes);
        return new SaveResult(true, bytes, doc, backupPath);
    }

    /// Writes beside the target and renames over it, so a plugin that reads
    /// mid-save sees the old file or the new one and never half of each. If
    /// the rename is refused (the plugin holds the file without delete
    /// sharing) the bytes are written in place instead.
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".inimaster-" + Environment.ProcessId + ".tmp");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 8) { Thread.Sleep(30); }
                catch (UnauthorizedAccessException) when (attempt < 8 && !IsReadOnly(path)) { Thread.Sleep(30); }
            }
        }
        catch (Exception) when (File.Exists(tmp))
        {
            try { File.Delete(tmp); } catch { }
            WriteInPlace(path, bytes);
        }
    }

    private static bool IsReadOnly(string path) =>
        File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);

    private static void WriteInPlace(string path, byte[] bytes)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                fs.Write(bytes);
                fs.SetLength(bytes.Length);
                return;
            }
            catch (IOException) when (attempt < 8) { Thread.Sleep(40); }
        }
    }

    public static string BackupFolder(string iniPath) =>
        Path.Combine(AppDataFolder, "backups", Path.GetFileNameWithoutExtension(iniPath));

    /// Copies the file's current bytes to the backup folder and keeps the
    /// newest 30 per file.
    public static string Backup(string iniPath, byte[] bytes)
    {
        var dir = BackupFolder(iniPath);
        Directory.CreateDirectory(dir);
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(iniPath)}";
        var path = Path.Combine(dir, name);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{i}{Path.GetExtension(iniPath)}");
        File.WriteAllBytes(path, bytes);
        foreach (var old in Directory.GetFiles(dir).OrderByDescending(File.GetCreationTimeUtc).Skip(30))
            try { File.Delete(old); } catch { }
        return path;
    }
}
