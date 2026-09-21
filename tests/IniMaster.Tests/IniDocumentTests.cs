using System.Text;
using IniMaster.Core;

namespace IniMaster.Tests;

public class IniDocumentTests
{
    [Fact]
    public void RoundTripsExactly()
    {
        var text = "; header\r\n\r\n[settings]\r\nA=1\r\n  B = two words  ; note\r\nweird line\r\n[empty]\r\n";
        Assert.Equal(text, IniDocument.Parse(text).ToString());
        var lf = "[s]\nA=1\nB=2";
        Assert.Equal(lf, IniDocument.Parse(lf).ToString());
    }

    [Fact]
    public void SetKeepsSpacingAndInlineComment()
    {
        var doc = IniDocument.Parse("[FrameGen]\nEnabled = auto ; hi\nColor=#FF0000\n");
        Assert.Equal("#FF0000", doc.Get("framegen", "color"));
        Assert.True(doc.Set("FrameGen", "Enabled", "true"));
        Assert.Equal("[FrameGen]\nEnabled = true ; hi\nColor=#FF0000\n", doc.ToString());
    }

    [Fact]
    public void SetEmptyValue()
    {
        var doc = IniDocument.Parse("[a]\nLanguage=\nNext=1\n");
        doc.Set("a", "Language", "en");
        Assert.Equal("[a]\nLanguage=en\nNext=1\n", doc.ToString());
    }

    [Fact]
    public void QuotedValuesStayQuoted()
    {
        var doc = IniDocument.Parse("[a]\nName=\"hello\"\n");
        Assert.Equal("hello", doc.Get("a", "Name"));
        doc.Set("a", "Name", "bye");
        Assert.Equal("[a]\nName=\"bye\"\n", doc.ToString());
    }

    [Fact]
    public void AddsMissingKeyAfterSiblings()
    {
        var doc = IniDocument.Parse("[a]\nX=1\n\n; about b\n[b]\nY=2\n");
        doc.Set("a", "Z", "3");
        doc.Set("c", "W", "4");
        Assert.Equal("[a]\nX=1\nZ=3\n\n; about b\n[b]\nY=2\n\n[c]\nW=4\n", doc.ToString());
    }

    [Fact]
    public void FirstDuplicateWins()
    {
        var doc = IniDocument.Parse("[a]\nX=1\nX=2\n");
        Assert.Equal("1", doc.Get("a", "X"));
        doc.Set("a", "X", "9");
        Assert.Equal("[a]\nX=9\nX=2\n", doc.ToString());
    }

    [Fact]
    public void KeepsBomAndUtf16()
    {
        var utf8 = TextCodec.Encode("[a]\r\nX=1\r\n", new UTF8Encoding(true));
        var doc = IniDocument.Load(utf8);
        doc.Set("a", "X", "2");
        var bytes = doc.ToBytes();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        var utf16 = TextCodec.Encode("[a]\r\nX=1\r\n", new UnicodeEncoding(false, true));
        var doc16 = IniDocument.Load(utf16);
        Assert.Equal("1", doc16.Get("a", "X"));
        doc16.Set("a", "X", "2");
        Assert.Equal(new byte[] { 0xFF, 0xFE }, doc16.ToBytes()[..2]);
    }

    [Fact]
    public void SaveOnlyTouchesEditedKeys()
    {
        var dir = Directory.CreateTempSubdirectory("inimaster");
        var path = Path.Combine(dir.FullName, "Mod.ini");
        File.WriteAllText(path, "[s]\nA=1\nB=2\n");
        // The plugin rewrote B after we loaded; our edit to A must not undo it.
        File.WriteAllText(path, "[s]\nA=1\nB=5\n");
        var r = IniStore.Save(path, new Dictionary<(string, string), string> { [("s", "A")] = "7" }, backup: false);
        Assert.True(r.Changed);
        Assert.Equal("[s]\nA=7\nB=5\n", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.FullName, "*.tmp"));
        dir.Delete(true);
    }
}
