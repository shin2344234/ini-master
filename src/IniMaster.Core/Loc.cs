using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IniMaster.Core;

/// The app's own text in the reader's language. Every string is looked up by
/// its English wording, so English needs no file and a missing translation
/// shows the English.
///
/// A translation is a text file named INIMaster.{lang}.txt, the same format
/// Master Looter uses: one line per string, the English, a tab, then the
/// translation. \n is a line break and lines starting with # are comments. A
/// line whose {0} placeholders differ from the English is dropped, since it
/// would show the wrong values. docs/TRANSLATING.md has the details.
public static partial class Loc
{
    public const string FilePrefix = "INIMaster.";
    public const string FileSuffix = ".txt";
    public const string TemplateName = "INIMaster.template.txt";

    private static Dictionary<string, string> _table = new(StringComparer.Ordinal);

    /// The language in use, lowercase with a hyphen: "en", "de", "pt-br".
    public static string Language { get; private set; } = "en";

    /// Language, then its base language when it has a region: "pt-br", "pt".
    public static IReadOnlyList<string> Chain { get; private set; } = new[] { "en" };

    /// Where the loaded table came from, or null for English or a missing file.
    public static string? SourcePath { get; private set; }

    /// Lines dropped from the loaded file because their placeholders did not
    /// match the English.
    public static int IgnoredLines { get; private set; }

    public static event Action? Changed;

    public static string T(string english) => _table.TryGetValue(english, out var t) ? t : english;

    /// T for a string held in a variable. Only for text the template script
    /// finds some other way, such as {l:T '...'} in XAML; everywhere else pass
    /// a literal to T so the script sees it.
    public static string Lookup(string english) => T(english);

    public static string T(string english, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(english), args);

    /// English has two plural forms, and so does this: the first string for
    /// exactly one, the second for any other count, with {0} as the count.
    public static string Plural(long n, string one, string other) =>
        string.Format(CultureInfo.InvariantCulture, T(n == 1 ? one : other), n);

    public static string Normalize(string? tag) => (tag ?? "").Trim().Replace('_', '-').ToLowerInvariant();

    /// The language, then what Windows considers its parents, so zh-HK looks
    /// for zh-hant and then zh, and pt-BR for pt.
    public static IReadOnlyList<string> ChainFor(string tag)
    {
        tag = Normalize(tag);
        if (tag.Length == 0) return new[] { "en" };
        var chain = new List<string> { tag };
        try
        {
            for (var c = CultureInfo.GetCultureInfo(tag).Parent; c != null && c.Name.Length > 0; c = c.Parent)
            {
                var name = Normalize(c.Name);
                if (!chain.Contains(name)) chain.Add(name);
            }
        }
        catch (CultureNotFoundException) { }
        var dash = tag.IndexOf('-');
        if (dash > 0 && !chain.Contains(tag[..dash])) chain.Add(tag[..dash]);
        return chain;
    }

    /// The writing system Windows gives a language, such as zh-hant for
    /// zh-TW. Null when it has only one.
    private static string? Script(string tag)
    {
        try
        {
            for (var c = CultureInfo.GetCultureInfo(tag).Parent; c != null && c.Name.Length > 0; c = c.Parent)
                if (c.Name.Contains('-')) return Normalize(c.Name);
        }
        catch (CultureNotFoundException) { }
        return null;
    }

    /// Translations for the same language in another region, closest first:
    /// zh-HK takes the zh-TW file because both are Traditional, and never the
    /// Simplified one.
    public static List<string> RelativesOf(string tag, IEnumerable<string> available)
    {
        tag = Normalize(tag);
        var baseTag = tag.Split('-')[0];
        if (baseTag.Length == 0) return new List<string>();
        var script = Script(tag);
        var cousins = available.Select(Normalize)
            .Where(a => a != tag && a != "en" && a.Split('-')[0] == baseTag)
            .ToList();
        var sameScript = cousins.Where(a => Script(a) == script).ToList();
        // With no script to go by, any region of the language beats English.
        return sameScript.Count > 0 ? sameScript : script == null ? cousins : new List<string>();
    }

    /// Switches to a language. The first of its chain with a table wins; with
    /// none, the app shows English. Returns the file used, if any.
    public static string? Use(string tag, IEnumerable<string> folders)
    {
        var chain = ChainFor(tag);
        Dictionary<string, string>? table = null;
        string? source = null;
        var ignored = 0;
        var folderList = folders as IList<string> ?? folders.ToList();
        // The language and its parents first, then another region of the same
        // language, which still reads better than English.
        foreach (var lang in chain.Concat(RelativesOf(tag, Available(folderList))))
        {
            if (lang == "en") break;
            foreach (var folder in folderList)
            {
                var path = Path.Combine(folder, FilePrefix + lang + FileSuffix);
                if (!File.Exists(path)) continue;
                try
                {
                    table = Parse(File.ReadAllText(path, Encoding.UTF8), out ignored);
                    source = path;
                    break;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (table != null) break;
            if (Embedded(lang) is { } text) { table = Parse(text, out ignored); source = "built in"; break; }
        }
        _table = table ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Language = chain[0];
        Chain = chain;
        SourcePath = source;
        IgnoredLines = ignored;
        Changed?.Invoke();
        return source;
    }

    /// Languages with a file in any of the folders or built in, English first.
    public static List<string> Available(IEnumerable<string> folders)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.EnumerateFiles(folder, FilePrefix + "*" + FileSuffix))
            {
                var name = Path.GetFileName(f);
                var lang = Normalize(name[FilePrefix.Length..^FileSuffix.Length]);
                if (lang.Length > 0 && lang != "template" && lang != "en") found.Add(lang);
            }
        }
        foreach (var lang in EmbeddedLanguages()) found.Add(lang);
        var list = new List<string> { "en" };
        list.AddRange(found);
        return list;
    }

    /// "Deutsch (de)" for a menu, or the tag itself for one Windows does not know.
    public static string DisplayName(string tag)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(tag);
            if (c.CultureTypes.HasFlag(CultureTypes.UserCustomCulture) || c.NativeName.Length == 0) return tag;
            return $"{char.ToUpper(c.NativeName[0], c)}{c.NativeName[1..]} ({tag})";
        }
        catch (CultureNotFoundException) { return tag; }
    }

    // ---------------------------------------------------------------- files

    public static Dictionary<string, string> Parse(string text, out int ignored)
    {
        ignored = 0;
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimStart('\uFEFF');
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var english = Unescape(line[..tab]);
            var translated = Unescape(line[(tab + 1)..]);
            if (translated.Trim().Length == 0) continue;
            if (!SamePlaceholders(english, translated)) { ignored++; continue; }
            table[english] = translated;
        }
        return table;
    }

    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");

    public static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            // \n, \t and \\ are escapes; any other backslash is kept as typed.
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (s[i] == '\\' && next is 'n' or 't' or '\\')
            {
                sb.Append(next switch { 'n' => '\n', 't' => '\t', _ => '\\' });
                i++;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// The same {n} placeholders, each as many times, in any order, since a
    /// translation may need to move them.
    public static bool SamePlaceholders(string a, string b)
    {
        static List<string> Of(string s) => Placeholder().Matches(s).Select(m => m.Value).Order(StringComparer.Ordinal).ToList();
        return Of(a).SequenceEqual(Of(b));
    }

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    // Translations shipped inside the exe, as embedded resources named
    // lang/INIMaster.{lang}.txt. None ship yet; the hook is here so a
    // finished translation can be built in without changing the loader.
    private static string? Embedded(string lang)
    {
        var asm = typeof(Loc).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + FilePrefix + lang + FileSuffix, StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static IEnumerable<string> EmbeddedLanguages()
    {
        foreach (var n in typeof(Loc).Assembly.GetManifestResourceNames())
        {
            var at = n.IndexOf(FilePrefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || !n.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var lang = Normalize(n[(at + FilePrefix.Length)..^FileSuffix.Length]);
            if (lang.Length > 0 && lang != "template") yield return lang;
        }
    }

    // ---------------------------------------------------------------- metadata

    /// Picks one text from a mod's translations, by language tag. The order is
    /// the reader's language, its base language, the untagged text, English,
    /// then whatever comes first.
    public static string? Pick(IEnumerable<KeyValuePair<string, string>> byLanguage, string? untagged = null)
    {
        var list = byLanguage.Select(p => (Lang: Normalize(p.Key), p.Value)).ToList();
        foreach (var lang in Chain)
            foreach (var (l, v) in list)
                if (l == lang) return v;
        if (untagged != null) return untagged;
        foreach (var (l, v) in list)
            if (l == "en" || l.StartsWith("en-", StringComparison.Ordinal)) return v;
        return list.Count > 0 ? list[0].Value : null;
    }

    /// Folds "label.de=..." style pairs into plain ones for the current
    /// language: for each name, the best tagged variant or the untagged one.
    /// Pairs without a language suffix pass through in their original order.
    public static List<KeyValuePair<string, string>> PickPairs(List<KeyValuePair<string, string>> pairs)
    {
        if (!pairs.Any(p => LangSuffix().IsMatch(p.Key))) return pairs;
        var result = new List<KeyValuePair<string, string>>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in pairs)
        {
            var m = LangSuffix().Match(key);
            var name = m.Success ? m.Groups["name"].Value : key;
            if (!done.Add(name)) continue;
            string? untagged = null;
            var tagged = new List<KeyValuePair<string, string>>();
            foreach (var (k2, v2) in pairs)
            {
                var m2 = LangSuffix().Match(k2);
                if (m2.Success && m2.Groups["name"].Value == name) tagged.Add(new(m2.Groups["lang"].Value, v2));
                else if (!m2.Success && k2 == name) untagged = v2;
            }
            if (Pick(tagged, untagged) is { } chosen) result.Add(new(name, chosen));
        }
        return result;
    }

    [GeneratedRegex(@"^(?<name>[a-z]+)\.(?<lang>[a-z]{2,3}(?:[-_][a-z0-9]{2,8})?)$")]
    private static partial Regex LangSuffix();
}
