using System.Text;
using System.Text.RegularExpressions;
using IniMaster.Core;

// Some tests switch Loc's language, which is global.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IniMaster.Tests;

public partial class LocalizationTests
{
    // ---------------------------------------------------------------- template

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "publish.ps1"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// Every English string the app can show, pulled from Loc.T and
    /// Loc.Plural calls in C# and {l:T '...'} in XAML.
    public static SortedSet<string> ExtractStrings(out List<string> problems)
    {
        problems = new List<string>();
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(f);
            foreach (Match m in CsT().Matches(text)) set.Add(UnescapeCs(m.Groups["s"].Value));
            foreach (Match m in CsPlural().Matches(text)) { set.Add(UnescapeCs(m.Groups["a"].Value)); set.Add(UnescapeCs(m.Groups["b"].Value)); }
            // A call with anything but literals would show text no translator saw.
            foreach (Match m in CsAnyCall().Matches(text))
            {
                var literal = m.Value.EndsWith("T(")
                    ? CsT().Match(text, m.Index) is { Success: true } t && t.Index == m.Index
                    : CsPlural().Match(text, m.Index) is { Success: true } p && p.Index == m.Index;
                if (!literal) problems.Add($"{Path.GetFileName(f)}: {text[m.Index..].Split('\n')[0].Trim()}");
            }
        }
        foreach (var f in SourceFiles("*.xaml"))
            foreach (Match m in XamlT().Matches(File.ReadAllText(f)))
                set.Add(m.Groups["s"].Value.Replace("\\'", "'").Replace("\\\\", "\\"));
        return set;
    }

    private static string UnescapeCs(string s) =>
        Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", "r" => "\r", var c => c });

    private const string TemplateHeader = """
        # INI Master text for translation.
        #
        # Copy this file to INIMaster.<language>.txt, for example INIMaster.de.txt
        # or INIMaster.pt-br.txt, and write each translation after the tab at the
        # end of its line. \n is a line break. Lines starting with # are ignored.
        # A line left empty after the tab keeps the English.
        #
        # Keep every {0}, {1} and so on. Their order may change, but a line that
        # adds or drops one is skipped when the file loads, since it would show
        # the wrong values.
        #
        # Put the finished file in %LOCALAPPDATA%\INIMaster\lang, or in a lang
        # folder next to INIMaster.exe, then pick the language under More >
        # Language. docs/TRANSLATING.md has the rest.

        """;

    public static string BuildTemplate(IEnumerable<string> strings)
    {
        var sb = new StringBuilder(TemplateHeader.Replace("\r\n", "\n"));
        foreach (var s in strings) sb.Append(Loc.Escape(s)).Append('\t').Append('\n');
        return sb.ToString();
    }

    [Fact]
    public void TemplateListsEveryString()
    {
        var strings = ExtractStrings(out var problems);
        Assert.True(problems.Count == 0, "Loc calls need a string literal:\n" + string.Join("\n", problems));
        Assert.True(strings.Count > 100, $"only {strings.Count} strings found");
        var expected = BuildTemplate(strings);
        var path = Path.Combine(RepoRoot, "lang", Loc.TemplateName);
        if (Environment.GetEnvironmentVariable("INIMASTER_WRITE_TEMPLATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        var actual = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        Assert.True(actual == expected,
            "lang/INIMaster.template.txt is out of date. Run scripts/update-lang-template.ps1 and commit the result.");
    }

    [Fact]
    public void TemplateParsesToNothing()
    {
        // Every line of the template is untranslated, so loading it changes nothing.
        var t = Loc.Parse(File.ReadAllText(Path.Combine(RepoRoot, "lang", Loc.TemplateName)), out var ignored);
        Assert.Empty(t);
        Assert.Equal(0, ignored);
    }

    /// A shipped translation must load whole: no line skipped for its
    /// placeholders and no English that the app no longer shows.
    [Fact]
    public void ShippedTranslationsMatchTheTemplate()
    {
        var english = ExtractStrings(out _);
        var files = Directory.GetFiles(Path.Combine(RepoRoot, "lang"), "INIMaster.*.txt").Where(f => !f.EndsWith(Loc.TemplateName)).ToList();
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var table = Loc.Parse(File.ReadAllText(f), out var ignored);
            Assert.True(ignored == 0, $"{Path.GetFileName(f)}: {ignored} lines have the wrong placeholders");
            var stale = table.Keys.Where(k => !english.Contains(k)).ToList();
            Assert.True(stale.Count == 0, $"{Path.GetFileName(f)} translates text the app no longer has:\n" + string.Join("\n", stale));
        }
    }

    [Fact]
    public void TranslationsAreBuiltIn()
    {
        try
        {
            Assert.Equal("built in", Loc.Use("de-DE", Array.Empty<string>()));
            Assert.Equal("Speichern", Loc.T("Save"));
            Assert.Equal("3 ungespeicherte Änderungen", Loc.Plural(3, "1 unsaved change", "{0} unsaved changes"));
            Assert.Equal("built in", Loc.Use("fr-CA", Array.Empty<string>()));
            Assert.Equal("Enregistrer", Loc.T("Save"));
            Assert.Equal("Par défaut : 5", Loc.T("Default: {0}", "5"));
            Assert.Equal(new[] { "en", "de", "fr", "zh-cn", "zh-tw" }, Loc.Available(Array.Empty<string>()));

            Assert.Equal("built in", Loc.Use("zh-TW", Array.Empty<string>()));
            Assert.Equal("儲存", Loc.T("Save"));
            Assert.Equal("built in", Loc.Use("zh-CN", Array.Empty<string>()));
            Assert.Equal("保存", Loc.T("Save"));

            // Hong Kong and Macau have no file of their own. Both read
            // Traditional, so they take zh-TW rather than falling to English.
            Assert.Equal("built in", Loc.Use("zh-HK", Array.Empty<string>()));
            Assert.Equal("儲存", Loc.T("Save"));
            Assert.Equal("built in", Loc.Use("zh-MO", Array.Empty<string>()));
            Assert.Equal("儲存", Loc.T("Save"));
            // Singapore writes Simplified.
            Assert.Equal("built in", Loc.Use("zh-SG", Array.Empty<string>()));
            Assert.Equal("保存", Loc.T("Save"));
        }
        finally { Loc.Use("en", Array.Empty<string>()); }
    }

    [GeneratedRegex(@"\bLoc\.T\(\s*""(?<s>(?:[^""\\]|\\.)*)""")]
    private static partial Regex CsT();

    [GeneratedRegex(@"\bLoc\.Plural\([^,""]+,\s*""(?<a>(?:[^""\\]|\\.)*)""\s*,\s*""(?<b>(?:[^""\\]|\\.)*)""")]
    private static partial Regex CsPlural();

    [GeneratedRegex(@"\bLoc\.(?:T|Plural)\(")]
    private static partial Regex CsAnyCall();

    [GeneratedRegex(@"\{l:T\s+'(?<s>(?:[^'\\]|\\.)*)'")]
    private static partial Regex XamlT();

    // ---------------------------------------------------------------- files

    [Fact]
    public void ParsesTranslationLines()
    {
        var text = "# comment\nSave\tSpeichern\nLine one\\nLine two\tZeile eins\\nZeile zwei\nEmpty\t\nSaved {0} to {1}.\t{1}: {0} gespeichert.\nNeeds {0}.\tBraucht {1}.\n";
        var t = Loc.Parse(text, out var ignored);
        Assert.Equal("Speichern", t["Save"]);
        Assert.Equal("Zeile eins\nZeile zwei", t["Line one\nLine two"]);
        Assert.False(t.ContainsKey("Empty"));
        Assert.Equal("{1}: {0} gespeichert.", t["Saved {0} to {1}."]);
        Assert.False(t.ContainsKey("Needs {0}."));
        Assert.Equal(1, ignored);
    }

    [Fact]
    public void LoadsAFileAndFallsBack()
    {
        var dir = Directory.CreateTempSubdirectory("inimaster-lang").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "INIMaster.de.txt"), "Save\tSpeichern\nFound {0} and {1} in {2}.\t{2}: {0} und {1}.\n");
            Assert.NotNull(Loc.Use("de-AT", new[] { dir }));
            Assert.Equal("de-at", Loc.Language);
            Assert.Equal("Speichern", Loc.T("Save"));
            Assert.Equal("x: a und b.", Loc.T("Found {0} and {1} in {2}.", "a", "b", "x"));
            Assert.Equal("Not translated", Loc.T("Not translated"));
            Assert.Contains("de", Loc.Available(new[] { dir }));

            // A language with no file here and none built in.
            Assert.Null(Loc.Use("sw", new[] { dir }));
            Assert.Equal("Save", Loc.T("Save"));
        }
        finally
        {
            Loc.Use("en", Array.Empty<string>());
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("pt-BR", new[] { "pt-br", "pt" })]
    [InlineData("zh-HK", new[] { "zh-hk", "zh-hant", "zh" })]
    [InlineData("de", new[] { "de" })]
    [InlineData("xx-YY", new[] { "xx-yy", "xx" })]
    public void ChainGoesThroughTheLanguageParents(string tag, string[] expected) =>
        Assert.Equal(expected, Loc.ChainFor(tag));

    [Theory]
    [InlineData("zh-HK", new[] { "zh-cn", "zh-tw" }, new[] { "zh-tw" })]
    [InlineData("zh-SG", new[] { "zh-cn", "zh-tw" }, new[] { "zh-cn" })]
    [InlineData("zh-TW", new[] { "zh-cn", "zh-tw" }, new string[0])]
    // "de" is already in de-AT's chain, so naming it again costs nothing.
    [InlineData("de-AT", new[] { "de", "fr" }, new[] { "de" })]
    [InlineData("es-MX", new[] { "es-es" }, new[] { "es-es" })]
    public void RelativesShareTheWritingSystem(string tag, string[] available, string[] expected) =>
        Assert.Equal(expected, Loc.RelativesOf(tag, available.Append("en")));

    /// A tag that names the writing system and no region, which is what a
    /// language list can hold, must not land on the other script's file.
    [Theory]
    [InlineData("zh-Hant", new[] { "zh-cn", "zh-tw" }, new[] { "zh-tw" })]
    [InlineData("zh-Hans", new[] { "zh-cn", "zh-tw" }, new[] { "zh-cn" })]
    public void AScriptTagKeepsItsWritingSystem(string tag, string[] available, string[] expected)
    {
        Assert.Equal(expected, Loc.RelativesOf(tag, available.Append("en")));
        try
        {
            Assert.Equal("built in", Loc.Use(tag, Array.Empty<string>()));
            Assert.Equal(tag == "zh-Hant" ? "儲存" : "保存", Loc.T("Save"));
        }
        finally { Loc.Use("en", Array.Empty<string>()); }
    }

    /// The file that was read counts for mod metadata too, so a Hong Kong
    /// window reading the zh-TW file also reads zh-TW help.
    [Fact]
    public void ModHelpFollowsTheFileThatWasRead()
    {
        var texts = new Dictionary<string, string> { ["en"] = "Cost", ["zh-TW"] = "費用" };
        Assert.Equal("費用", InLanguage("zh-HK", () => Loc.Pick(texts)));
        Assert.Equal("費用", InLanguage("zh-MO", () => Loc.Pick(texts)));
        // Simplified regions still take the Simplified file and its help.
        Assert.Equal("Cost", InLanguage("zh-SG", () => Loc.Pick(texts)));
        // A language with no file at all leaves the chain alone.
        Assert.Equal("Cost", InLanguage("sw", () => Loc.Pick(texts)));
    }

    [Fact]
    public void PluralUsesTheCount()
    {
        Assert.Equal("1 change", Loc.Plural(1, "1 change", "{0} changes"));
        Assert.Equal("3 changes", Loc.Plural(3, "1 change", "{0} changes"));
        Assert.Equal("0 changes", Loc.Plural(0, "1 change", "{0} changes"));
    }

    // ---------------------------------------------------------------- metadata

    private static T InLanguage<T>(string tag, Func<T> f)
    {
        Loc.Use(tag, Array.Empty<string>());
        try { return f(); }
        finally { Loc.Use("en", Array.Empty<string>()); }
    }

    [Fact]
    public void PickFollowsTheChain()
    {
        var texts = new Dictionary<string, string> { ["en"] = "Cost", ["de"] = "Kosten", ["pt-BR"] = "Custo" };
        Assert.Equal("Kosten", InLanguage("de-DE", () => Loc.Pick(texts)));
        Assert.Equal("Custo", InLanguage("pt_br", () => Loc.Pick(texts)));
        Assert.Equal("Cost", InLanguage("ja", () => Loc.Pick(texts)));
        Assert.Equal("Plain", InLanguage("ja", () => Loc.Pick(texts, "Plain")));
        Assert.Equal("Kosten", InLanguage("de", () => Loc.Pick(texts, "Plain")));
    }

    private const string LocalizedJson = """
        {
          "name": { "en": "Loot Mod", "de": "Beute-Mod" },
          "sections": {
            "main": {
              "label": { "en": "General", "de": "Allgemein" },
              "keys": {
                "Mode": {
                  "type": "enum",
                  "label": { "en": "Mode", "de": "Modus" },
                  "help": { "en": ["Line one.", "Line two."], "de": ["Zeile eins.", "Zeile zwei."] },
                  "options": { "0": { "en": "Off", "de": "Aus" }, "1": "On" },
                  "group": { "de": "Beute" }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void JsonTextComesInLanguages()
    {
        var de = InLanguage("de", () => MetaLoader.Load(LocalizedJson, MetaSource.Sidecar, "t"));
        var k = de.FindKey("main", "Mode")!;
        Assert.Equal("Beute-Mod", de.Name);
        Assert.Equal("Allgemein", de.Sections["main"].Label);
        Assert.Equal("Modus", k.Label);
        Assert.Equal("Zeile eins.\nZeile zwei.", k.Help);
        Assert.Equal("Aus", k.Options![0].Label);
        Assert.Equal("On", k.Options[1].Label);
        Assert.Equal("Beute", k.Group);

        var fr = InLanguage("fr", () => MetaLoader.Load(LocalizedJson, MetaSource.Sidecar, "t"));
        var kf = fr.FindKey("main", "Mode")!;
        Assert.Equal("Mode", kf.Label);
        Assert.Equal("Line one.\nLine two.", kf.Help);
        Assert.Equal("Off", kf.Options![0].Label);
        // Only a German group, so a French reader gets it too rather than none.
        Assert.Equal("Beute", kf.Group);
    }

    [Fact]
    public void DirectivesTakeLanguageSuffixes()
    {
        const string ini = """
            [main]
            ;@ int min=0 max=10 label="Speed" label.de="Tempo" label.pt-br="Velocidade" help.de="Wie schnell."
            Speed=5
            """;
        var de = InLanguage("de", () => IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini)));
        var s = de.Settings.Single().Setting;
        Assert.Equal("Tempo", s.Label);
        Assert.Equal("Wie schnell.", s.Help);
        Assert.Equal(10, s.Max);

        var pt = InLanguage("pt-BR", () => IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini)));
        Assert.Equal("Velocidade", pt.Settings.Single().Setting.Label);

        var en = IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini));
        Assert.Equal("Speed", en.Settings.Single().Setting.Label);
    }

    // ---------------------------------------------------------------- ini text

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    public void NonEnglishTextRoundTrips(string kind)
    {
        Encoding enc = kind switch
        {
            "utf-8" => new UTF8Encoding(false),
            "utf-8-bom" => new UTF8Encoding(true),
            _ => new UnicodeEncoding(false, true),
        };
        var text = "; Настройки мода\r\n[общие]\r\nИмя=Воин ; комментарий\r\n名前=侍\r\nThai=ภาษาไทย\r\n";
        var bytes = enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray();
        var doc = IniDocument.Load(bytes);
        Assert.Equal("Воин", doc.Get("ОБЩИЕ", "имя"));
        Assert.Equal("侍", doc.Get("общие", "名前"));
        Assert.Equal(bytes, doc.ToBytes());
        doc.Set("общие", "名前", "忍者");
        var again = IniDocument.Load(doc.ToBytes());
        Assert.Equal("忍者", again.Get("общие", "名前"));
        Assert.Equal("комментарий", again.Find("общие", "Имя")!.InlineCommentBody);
    }

    [Fact]
    public void AnsiFilesKeepEveryByte()
    {
        // Not valid UTF-8, so it reads as the ANSI code page. Unedited lines
        // must come back byte for byte.
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("[a]\r\nName="));
        for (var b = 0xC0; b <= 0xFF; b++) bytes.Add((byte)b);
        bytes.AddRange(Encoding.ASCII.GetBytes("\r\nOther=1\r\n"));
        var doc = IniDocument.Load(bytes.ToArray());
        Assert.Equal(bytes.ToArray(), doc.ToBytes());
        doc.Set("a", "Other", "2");
        var saved = doc.ToBytes();
        Assert.Equal(bytes.Count, saved.Length);
        Assert.Equal(bytes.Take(bytes.Count - 3), saved.Take(bytes.Count - 3));
    }

    [Fact]
    public void RefusesCharactersTheFileCannotHold()
    {
        var latin = Encoding.Latin1;
        Assert.True(TextCodec.CanEncode("café", latin));
        Assert.False(TextCodec.CanEncode("侍", latin));
        Assert.Equal("侍", TextCodec.FirstUnencodable("ok 侍 x", latin));
        Assert.True(TextCodec.CanEncode("侍", new UTF8Encoding(false)));

        var path = Path.Combine(Path.GetTempPath(), $"inimaster-enc-{Guid.NewGuid():N}.ini");
        try
        {
            // 0xE9 alone is not UTF-8, so the file reads as ANSI.
            File.WriteAllBytes(path, new byte[] { (byte)'[', (byte)'a', (byte)']', 13, 10, (byte)'N', (byte)'=', 0xE9, 13, 10 });
            var before = File.ReadAllBytes(path);
            Assert.Throws<UnencodableTextException>(() =>
                IniStore.Save(path, new Dictionary<(string, string), string> { [("a", "N")] = "侍" }, backup: false));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Throws<UnencodableTextException>(() => IniStore.SaveText(path, "[a]\r\nN=侍\r\n", backup: false));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("ВидимостьМеню", "Видимость меню")]
    [InlineData("名前", "名前")]
    public void HumanizesNonLatinKeys(string key, string label) => Assert.Equal(label, SettingResolver.Humanize(key));
}
