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
