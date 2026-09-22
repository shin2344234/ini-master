using System.Text;
using IniMaster.Core;

namespace IniMaster.Tests;

/// One test per finding from the 1.0.0 review.
public class RegressionTests
{
    [Theory]
    [InlineData("hello ; world")]
    [InlineData("tag #2")]
    [InlineData("a\t;b")]
    public void ValuesWithCommentCharactersRoundTrip(string value)
    {
        var doc = IniDocument.Parse("[a]\nNote=old ; help\n");
        doc.Set("a", "Note", value);
        var again = IniDocument.Parse(doc.ToString());
        Assert.Equal(value, again.Get("a", "Note"));
        Assert.Equal("help", again.Find("a", "Note")!.InlineCommentBody);

        doc.Set("a", "Added", value);
        Assert.Equal(value, IniDocument.Parse(doc.ToString()).Get("a", "Added"));
    }

    [Fact]
    public void NewKeyIsWrittenOnce()
    {
        var doc = IniDocument.Parse("[a]\n");
        doc.Set("a", "Note", "a ;b");
        Assert.Equal("[a]\nNote=\"a ;b\"\n", doc.ToString());
    }

    [Fact]
    public void ClearingAValueKeepsTheCommentOutOfIt()
    {
        var doc = IniDocument.Parse("[a]\nName=bob ; help\nColor = #FF0000\n");
        doc.Set("a", "Name", "");
        var again = IniDocument.Parse(doc.ToString());
        Assert.Equal("", again.Get("a", "Name"));
        Assert.Equal("help", again.Find("a", "Name")!.InlineCommentBody);
        Assert.Equal("#FF0000", again.Get("a", "Color"));
    }

    [Fact]
    public void MixedLineEndingsSurviveASave()
    {
        var text = "[a]\r\nA=1\nB=2\r\n; note\nC=3";
        var doc = IniDocument.Parse(text);
        Assert.Equal(text, doc.ToString());
        doc.Set("a", "B", "5");
        Assert.Equal("[a]\r\nA=1\nB=5\r\n; note\nC=3", doc.ToString());
        doc.Set("a", "D", "4");
        Assert.Equal("[a]\r\nA=1\nB=5\r\n; note\nC=3\r\nD=4\r\n", doc.ToString());
    }

    [Fact]
    public void MixedLineEndingsSurviveIniStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"inimaster-eol-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("[a]\r\nA=1\nB=2\r\n"));
            IniStore.Save(path, new Dictionary<(string, string), string> { [("a", "A")] = "9" }, backup: false);
            Assert.Equal("[a]\r\nA=9\nB=2\r\n", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------------- comments

    private const string CommentedIni = """
        ; My Mod, a mod.
        [settings]

        ; ---- Movement
        ; This paragraph is a note.

        ; How fast you go.
        ;   0  slow
        ;   1  fast
        ;@ max=3
        Speed=1
        ; Comment help for a key the metadata says nothing about.
        Other=2
        """;

    private static IniView View(string ini, ModMeta meta) =>
        IniView.Build(new IniTarget { IniPath = "x.ini", Meta = meta }, IniDocument.Parse(ini));

    private static ModMeta Meta(MetaSource source, string json) => MetaLoader.Load(json, source, "test");

    private const string SpeedJson = """{ "name": "Plugin Name", "sections": { "settings": { "keys": { "Speed": { "type": "int", "help": "From the plugin." } } } } }""";

    [Fact]
    public void EmbeddedMetadataReplacesTheIniComments()
    {
        var v = View(CommentedIni, Meta(MetaSource.Embedded, SpeedJson));
        Assert.False(v.UsesComments);
        Assert.Equal("Plugin Name", v.Meta.Name);
        var speed = v.Settings.Single(s => s.Setting.Key == "Speed").Setting;
        Assert.Equal("From the plugin.", speed.Help);
        Assert.Empty(speed.Options);
        // A ";@" directive is an instruction, not prose, so it still counts.
        Assert.Equal(3, speed.Max);
        // Keys the metadata skips still show, without comment help.
        var other = v.Settings.Single(s => s.Setting.Key == "Other").Setting;
        Assert.Null(other.Help);
        Assert.DoesNotContain(v.Sections[0].Items, i => i is ViewNote or ViewGroup);
    }

    [Fact]
    public void ASidecarStillLeavesTheCommentsInPlace()
    {
        var v = View(CommentedIni, Meta(MetaSource.Sidecar, SpeedJson));
        Assert.True(v.UsesComments);
        Assert.Equal("From the plugin.", v.Settings.Single(s => s.Setting.Key == "Speed").Setting.Help);
        Assert.Equal("Comment help for a key the metadata says nothing about.",
            v.Settings.Single(s => s.Setting.Key == "Other").Setting.Help);
        Assert.Contains(v.Sections[0].Items, i => i is ViewGroup { Title: "Movement" });
        Assert.Contains(v.Sections[0].Items, i => i is ViewNote);
    }

    [Theory]
    [InlineData(MetaSource.Embedded, "true", true)]
    [InlineData(MetaSource.Embedded, "1", true)]
    [InlineData(MetaSource.Sidecar, "false", false)]
    public void MetadataCanSayWhetherToReadComments(MetaSource source, string value, bool expected)
    {
        var json = $$"""{ "comments": {{value}}, "sections": { "settings": { "keys": { "Speed": { "type": "int" } } } } }""";
        var v = View(CommentedIni, Meta(source, json));
        Assert.Equal(expected, v.UsesComments);
        Assert.Equal(expected, v.Settings.Single(s => s.Setting.Key == "Other").Setting.Help != null);
    }

    [Fact]
    public void WithoutMetadataTheCommentsAreAllThereIs()
    {
        var v = View(CommentedIni, new ModMeta());
        Assert.True(v.UsesComments);
        var speed = v.Settings.Single(s => s.Setting.Key == "Speed").Setting;
        Assert.StartsWith("How fast you go.", speed.Help);
        Assert.Equal(new[] { "slow", "fast" }, speed.Options.Select(o => o.Label));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void BlankGroupNamesAreIgnored(string group)
    {
        var json = $$"""{ "sections": { "a": { "keys": { "Speed": { "type": "int", "group": {{group}} } } } } }""";
        var meta = MetaLoader.Load(json, MetaSource.Embedded, "test");
        var target = new IniTarget { IniPath = "x.ini", Meta = meta };
        var view = IniView.Build(target, IniDocument.Parse("[a]\nSpeed=1\n"));
        Assert.Null(view.Settings.Single().Setting.Group);
        Assert.DoesNotContain(view.Sections[0].Items, i => i is ViewGroup);
    }
}
