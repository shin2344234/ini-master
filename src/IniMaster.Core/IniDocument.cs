using System.Text;

namespace IniMaster.Core;

public enum IniLineKind { Blank, Comment, Section, Key, Other }

/// One physical line of an ini file. The raw text is kept verbatim so a save
/// rewrites only the value characters of the keys that changed.
public sealed class IniLine
{
    public string Text { get; private set; } = "";
    public IniLineKind Kind { get; private set; }

    /// Section header name, or for a key line the section it sits in.
    public string Section { get; internal set; } = "";
    public string Key { get; private set; } = "";

    /// Text after the ';' or '#' of a comment line, one leading space removed.
    public string CommentBody { get; private set; } = "";

    /// Where the raw value sits inside Text. Excludes an inline comment and
    /// the whitespace before it.
    public int ValueStart { get; private set; }
    public int ValueLength { get; private set; }

    public string RawValue => Kind == IniLineKind.Key ? Text.Substring(ValueStart, ValueLength) : "";

    /// The value the way GetPrivateProfileString returns it: one pair of
    /// surrounding double quotes removed.
    public string Value => IsQuoted ? RawValue[1..^1] : RawValue;

    public bool IsQuoted => RawValue.Length >= 2 && RawValue[0] == '"' && RawValue[^1] == '"';

    /// "; comment" after the value, including its leading whitespace. Empty
    /// when there is none.
    public string InlineComment { get; private set; } = "";

    public IniLine(string text) => SetText(text);

    internal void SetText(string text)
    {
        Text = text;
        Key = "";
        CommentBody = "";
        InlineComment = "";
        ValueStart = ValueLength = 0;

        var t = text.TrimStart();
        if (t.Length == 0) { Kind = IniLineKind.Blank; return; }
        if (t[0] == ';' || t[0] == '#')
        {
            Kind = IniLineKind.Comment;
            var body = t[1..];
            if (body.StartsWith(' ')) body = body[1..];
            CommentBody = body.TrimEnd();
            return;
        }
        if (t[0] == '[')
        {
            var close = t.IndexOf(']');
            if (close > 0)
            {
                Kind = IniLineKind.Section;
                Section = t[1..close].Trim();
                return;
            }
        }
        var eq = text.IndexOf('=');
        if (eq > 0 && text[..eq].Trim().Length > 0)
        {
            Kind = IniLineKind.Key;
            Key = text[..eq].Trim();
            var start = eq + 1;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\t')) start++;
            var end = text.Length;
            // An inline comment needs whitespace before its ';' or '#', so a
            // value such as #FF0000 or a;b stays whole.
            for (var i = start + 1; i < text.Length; i++)
            {
                if ((text[i] == ';' || text[i] == '#') && (text[i - 1] == ' ' || text[i - 1] == '\t') && !InsideQuotes(text, start, i))
                {
                    end = i;
                    break;
                }
            }
            var valueEnd = end;
            while (valueEnd > start && (text[valueEnd - 1] == ' ' || text[valueEnd - 1] == '\t')) valueEnd--;
            ValueStart = start;
            ValueLength = valueEnd - start;
            InlineComment = text[valueEnd..];
            return;
        }
        Kind = IniLineKind.Other;
    }

    private static bool InsideQuotes(string s, int from, int to)
    {
        if (from >= s.Length || s[from] != '"') return false;
        var close = s.IndexOf('"', from + 1);
        return close < 0 || close > to;
    }

    public string InlineCommentBody
    {
        get
        {
            var t = InlineComment.TrimStart();
            if (t.Length == 0) return "";
            t = t[1..];
            return t.Trim();
        }
    }

    internal void ReplaceValue(string newValue)
    {
        var raw = IsQuoted || NeedsQuotes(newValue) ? "\"" + newValue + "\"" : newValue;
        SetText(Text[..ValueStart] + raw + Text[(ValueStart + ValueLength)..]);
    }

    // A value with leading or trailing spaces would lose them on read, so
    // quote it the way GetPrivateProfileString expects.
    private static bool NeedsQuotes(string v) => v.Length > 0 && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1]));
}

/// A format-preserving ini file. Lookups follow the Windows profile API:
/// section and key names ignore case and the first occurrence wins.
public sealed class IniDocument
{
    public List<IniLine> Lines { get; } = new();
    public string NewLine { get; private set; } = "\r\n";
    public bool EndsWithNewLine { get; private set; } = true;
    public Encoding Encoding { get; private set; } = new UTF8Encoding(false);

    private Dictionary<(string, string), IniLine> _keys = new(KeyComparer.Instance);

    public static IniDocument Parse(string text, Encoding? encoding = null)
    {
        var doc = new IniDocument();
        if (encoding != null) doc.Encoding = encoding;
        var nl = text.IndexOf('\n');
        doc.NewLine = nl > 0 && text[nl - 1] == '\r' ? "\r\n" : nl >= 0 ? "\n" : "\r\n";
        doc.EndsWithNewLine = text.Length == 0 || text.EndsWith('\n');
        var parts = text.Replace("\r\n", "\n").Split('\n');
        var count = doc.EndsWithNewLine && parts.Length > 0 ? parts.Length - 1 : parts.Length;
        for (var i = 0; i < count; i++) doc.Lines.Add(new IniLine(parts[i].TrimEnd('\r')));
        doc.Reindex();
        return doc;
    }

    public static IniDocument Load(byte[] bytes)
    {
        var text = TextCodec.Decode(bytes, out var enc);
        return Parse(text, enc);
    }

    public byte[] ToBytes() => TextCodec.Encode(ToString(), Encoding);

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Lines.Count; i++)
        {
            sb.Append(Lines[i].Text);
            if (i < Lines.Count - 1 || EndsWithNewLine) sb.Append(NewLine);
        }
        return sb.ToString();
    }

    private void Reindex()
    {
        _keys = new Dictionary<(string, string), IniLine>(KeyComparer.Instance);
        var section = "";
        foreach (var line in Lines)
        {
            if (line.Kind == IniLineKind.Section) section = line.Section;
            else
            {
                line.Section = section;
                if (line.Kind == IniLineKind.Key) _keys.TryAdd((section, line.Key), line);
            }
        }
    }

    public IEnumerable<string> SectionNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Lines.Any(l => l.Kind == IniLineKind.Key && l.Section.Length == 0)) { seen.Add(""); yield return ""; }
        foreach (var l in Lines)
            if (l.Kind == IniLineKind.Section && seen.Add(l.Section)) yield return l.Section;
    }

    public IniLine? Find(string section, string key) => _keys.GetValueOrDefault((section, key));

    public string? Get(string section, string key) => Find(section, key)?.Value;

    /// Sets a value, adding the key (and the section) if the file lacks it.
    /// Returns true if the text changed.
    public bool Set(string section, string key, string value)
    {
        var line = Find(section, key);
        if (line != null)
        {
            if (line.Value == value) return false;
            line.ReplaceValue(value);
            return true;
        }
        var sep = Lines.Any(l => l.Kind == IniLineKind.Key && l.Text.Contains(" = ")) ? " = " : "=";
        var newLine = new IniLine(key + sep + value) ;
        if (newLine.Value != value) newLine.ReplaceValue(value);

        var header = Lines.FindIndex(l => l.Kind == IniLineKind.Section && string.Equals(l.Section, section, StringComparison.OrdinalIgnoreCase));
        if (header < 0 && section.Length > 0)
        {
            if (Lines.Count > 0 && Lines[^1].Kind != IniLineKind.Blank) Lines.Add(new IniLine(""));
            Lines.Add(new IniLine("[" + section + "]"));
            Lines.Add(newLine);
        }
        else
        {
            // After the last key of the section, so the new line lands next
            // to its siblings and not after a trailing comment block.
            var at = header < 0 ? 0 : header + 1;
            var lastKey = -1;
            for (var i = at; i < Lines.Count && Lines[i].Kind != IniLineKind.Section; i++)
                if (Lines[i].Kind == IniLineKind.Key) lastKey = i;
            Lines.Insert(lastKey >= 0 ? lastKey + 1 : at, newLine);
        }
        EndsWithNewLine = true;
        Reindex();
        return true;
    }

    private sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public static readonly KeyComparer Instance = new();
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item2));
    }
}

public static class TextCodec
{
    static TextCodec() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// Decodes with the file's own BOM if it has one, strict UTF-8 if the
    /// bytes are valid UTF-8, and the ANSI code page otherwise, which is what
    /// GetPrivateProfileStringA would read.
    public static string Decode(byte[] bytes, out Encoding encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        try
        {
            var strict = new UTF8Encoding(false, true);
            var s = strict.GetString(bytes);
            encoding = new UTF8Encoding(false);
            return s;
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.GetEncoding(0);
            return encoding.GetString(bytes);
        }
    }

    public static byte[] Encode(string text, Encoding encoding)
    {
        var body = encoding.GetBytes(text);
        var bom = encoding.GetPreamble();
        if (bom.Length == 0) return body;
        var all = new byte[bom.Length + body.Length];
        bom.CopyTo(all, 0);
        body.CopyTo(all, bom.Length);
        return all;
    }
}
