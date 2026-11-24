using IniMaster.Core;

namespace IniMaster.Tests;

public class ModUpdateTests
{
    [Theory]
    [InlineData("shin2344234/master-looter", "shin2344234/master-looter")]
    [InlineData("  shin2344234/master-looter  ", "shin2344234/master-looter")]
    [InlineData("https://github.com/shin2344234/master-looter", "shin2344234/master-looter")]
    [InlineData("https://github.com/shin2344234/master-looter.git", "shin2344234/master-looter")]
    [InlineData("https://github.com/shin2344234/master-looter/releases/latest", "shin2344234/master-looter")]
    [InlineData("github.com/Owner/Repo.Name", "Owner/Repo.Name")]
    [InlineData("http://www.github.com/a/b?tab=readme", "a/b")]
    public void ReadsARepository(string text, string expected) => Assert.Equal(expected, ModUpdates.ParseRepo(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("master-looter")]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("https://example.com/github.com/a/b")]
    [InlineData("a/..")]
    [InlineData("a/.git")]
    [InlineData("-bad/name")]
    [InlineData("a b/c")]
    public void RefusesAnythingElse(string? text) => Assert.Null(ModUpdates.ParseRepo(text));

    private const string Latest = """
        { "tag_name": "v1.6.70", "draft": false, "prerelease": false,
          "html_url": "https://github.com/shin2344234/master-looter/releases/tag/v1.6.70" }
        """;

    [Fact]
    public void ReadsTheNewestRelease()
    {
        var r = ModUpdates.Parse(Latest, "shin2344234/master-looter")!;
        Assert.Equal(new Version(1, 6, 70), r.Version);
        Assert.Equal("v1.6.70", r.Tag);
        Assert.Equal("https://github.com/shin2344234/master-looter/releases/tag/v1.6.70", r.PageUrl);
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    [InlineData("v1.6.70", "nightly")]
    public void SkipsReleasesItCannotCompare(string from, string to) =>
        Assert.Null(ModUpdates.Parse(Latest.Replace(from, to), "shin2344234/master-looter"));

    [Fact]
    public void APageOffGitHubFallsBackToTheReleasesPage()
    {
        var r = ModUpdates.Parse(Latest.Replace("https://github.com/shin2344234/master-looter/releases/tag/v1.6.70", "https://example.com/x"), "a/b")!;
        Assert.Equal("https://github.com/a/b/releases/latest", r.PageUrl);
    }

    [Theory]
    [InlineData("1.6.69", true)]
    [InlineData("1.6.70", false)]
    [InlineData("v1.6.70", false)]
    [InlineData("1.7", false)]
    [InlineData("2.0.0", false)]
    public void ComparesAgainstTheInstalledVersion(string installed, bool newer)
    {
        var r = ModUpdates.Parse(Latest, "a/b");
        Assert.Equal(newer, ModUpdates.NewerThan(r, Updates.ParseVersion(installed)) != null);
    }

    [Fact]
    public void NoInstalledVersionMeansNoBadge() =>
        Assert.Null(ModUpdates.NewerThan(ModUpdates.Parse(Latest, "a/b"), null));

    private static ModInfo Mod(params ModMeta[] metas)
    {
        var mod = new ModInfo { Id = "x", BaseName = "X" };
        var i = 0;
        foreach (var m in metas) mod.Files.Add(new IniTarget { IniPath = $"X{i++}.ini", Meta = m });
        return mod;
    }

    [Fact]
    public void TakesTheRepositoryAndVersionFromTheSameMetadata()
    {
        var s = ModUpdates.SourceOf(Mod(new ModMeta { Version = "9.9.9" }, new ModMeta { Version = "1.2.0", Github = "a/b" }))!;
        Assert.Equal("a/b", s.Repo);
        Assert.Equal(new Version(1, 2, 0), s.Installed);
        Assert.Equal("1.2.0", s.InstalledText);
    }

    [Fact]
    public void AModWithoutARepositoryIsNotChecked()
    {
        Assert.Null(ModUpdates.SourceOf(Mod(new ModMeta { Version = "1.0.0" })));
        Assert.Null(ModUpdates.SourceOf(Mod(new ModMeta { Version = "1.0.0", Github = "not a repo" })));
        Assert.Equal("not a repo", ModUpdates.UnreadableRepo(Mod(new ModMeta { Github = "not a repo" })));
        Assert.Null(ModUpdates.UnreadableRepo(Mod(new ModMeta { Github = "a/b" })));
    }

    [Fact]
    public void ReadsGithubFromJsonAndFromAnAnnotatedIni()
    {
        var json = MetaLoader.Load("""{ "version": "1.0.1", "github": "a/b" }""", MetaSource.Sidecar, "test");
        Assert.Equal("a/b", json.Github);
        var ini = MetaLoader.Load(";@mod version=1.0.1 github=https://github.com/a/b\n[settings]\nEnabled=1\n", MetaSource.Sidecar, "test");
        Assert.Equal("https://github.com/a/b", ini.Github);
        Assert.Equal("1.0.1", ini.Version);
    }

    [Fact]
    public void AHigherLayerReplacesTheRepository()
    {
        var low = new ModMeta { Github = "a/old" };
        low.OverlayWith(new ModMeta { Github = "a/new" });
        Assert.Equal("a/new", low.Github);
        low.OverlayWith(new ModMeta());
        Assert.Equal("a/new", low.Github);
    }
}
