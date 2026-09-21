using System.Text;

namespace IniMaster.Core;

/// Finds INI Master metadata inside a plugin binary by reading its bytes.
/// The plugin is never loaded, so none of its code runs.
///
/// Two ways in, both checked:
///   a resource of type "INIMETA"        one line in the plugin's .rc file
///   "@@INIMETA@@" ... "@@/INIMETA@@"    a marked string anywhere in the file,
///                                       for toolchains without resources
public static class AsiMetaReader
{
    public const string ResourceType = "INIMETA";
    public const string MarkerBegin = "@@INIMETA@@";
    public const string MarkerEnd = "@@/INIMETA@@";

    public sealed record Found(string Text, string Where);

    public static List<Found> Read(string path)
    {
        byte[] bytes;
        // The game holds a loaded plugin open, so share everything.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        return Read(bytes);
    }

    public static List<Found> Read(byte[] bytes)
    {
        var found = new List<Found>();
        try
        {
            foreach (var (name, data) in ReadResources(bytes, ResourceType))
                found.Add(new Found(DecodeText(data), $"resource {ResourceType}/{name}"));
        }
        catch (Exception)
        {
            // A malformed resource table is not our problem to report; the
            // marker scan below still runs.
        }
        foreach (var text in ScanMarkers(bytes)) found.Add(new Found(text, "embedded marker"));
        return found;
    }

    public static List<string> ScanMarkers(byte[] bytes)
    {
        var result = new List<string>();
        var begin = Encoding.ASCII.GetBytes(MarkerBegin);
        var end = Encoding.ASCII.GetBytes(MarkerEnd);
        var span = bytes.AsSpan();
        var at = 0;
        while (at < span.Length)
        {
            var b = span[at..].IndexOf(begin);
            if (b < 0) break;
            var start = at + b + begin.Length;
            var e = span[start..].IndexOf(end);
            if (e < 0) break;
            var body = span.Slice(start, e);
            // Anything that is not text is skipped rather than shown.
            if (body.IndexOf((byte)0) < 0) result.Add(Encoding.UTF8.GetString(body).Trim());
            at = start + e + end.Length;
        }
        return result;
    }

    private static string DecodeText(byte[] data)
    {
        // Resource compilers can pad the data with NULs.
        var text = TextCodec.Decode(data, out _);
        return text.TrimEnd('\0').Trim();
    }

    // ---------------------------------------------------------------- PE

    private static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    private static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);

    /// Every resource of the given string type, as (name, bytes).
    public static List<(string Name, byte[] Data)> ReadResources(byte[] b, string type)
    {
        var result = new List<(string, byte[])>();
        if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') return result;
        var pe = (int)U32(b, 0x3C);
        if (pe <= 0 || pe + 24 > b.Length || U32(b, pe) != 0x00004550) return result;

        var sectionCount = U16(b, pe + 6);
        var optSize = U16(b, pe + 20);
        var opt = pe + 24;
        var magic = U16(b, opt);
        var dirs = magic switch { 0x20B => opt + 112, 0x10B => opt + 96, _ => -1 };
        if (dirs < 0) return result;
        var rsrcRva = U32(b, dirs + 2 * 8);
        if (rsrcRva == 0) return result;

        var sections = opt + optSize;
        int RvaToOffset(uint rva)
        {
            for (var i = 0; i < sectionCount; i++)
            {
                var s = sections + i * 40;
                var va = U32(b, s + 12);
                var vsize = Math.Max(U32(b, s + 8), U32(b, s + 16));
                var raw = U32(b, s + 20);
                if (rva >= va && rva < va + vsize) return (int)(rva - va + raw);
            }
            return -1;
        }

        var root = RvaToOffset(rsrcRva);
        if (root < 0) return result;

        foreach (var (typeName, typeOff, typeIsDir) in Entries(b, root, root))
        {
            if (!typeIsDir || !string.Equals(typeName, type, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (name, nameOff, nameIsDir) in Entries(b, root, root + typeOff))
            {
                if (!nameIsDir) continue;
                foreach (var (_, langOff, langIsDir) in Entries(b, root, root + nameOff))
                {
                    if (langIsDir) continue;
                    var entry = root + langOff;
                    var dataOff = RvaToOffset(U32(b, entry));
                    var size = (int)U32(b, entry + 4);
                    if (dataOff < 0 || size <= 0 || dataOff + size > b.Length) continue;
                    result.Add((name, b.AsSpan(dataOff, size).ToArray()));
                    break;
                }
            }
        }
        return result;
    }

    private static IEnumerable<(string Name, int Offset, bool IsDir)> Entries(byte[] b, int root, int dir)
    {
        if (dir + 16 > b.Length) yield break;
        var count = U16(b, dir + 12) + U16(b, dir + 14);
        for (var i = 0; i < count; i++)
        {
            var e = dir + 16 + i * 8;
            if (e + 8 > b.Length) yield break;
            var nameField = U32(b, e);
            var dataField = U32(b, e + 4);
            string name;
            if ((nameField & 0x80000000) != 0)
            {
                var s = root + (int)(nameField & 0x7FFFFFFF);
                var len = U16(b, s);
                name = Encoding.Unicode.GetString(b, s + 2, len * 2);
            }
            else name = "#" + nameField;
            yield return (name, (int)(dataField & 0x7FFFFFFF), (dataField & 0x80000000) != 0);
        }
    }
}
