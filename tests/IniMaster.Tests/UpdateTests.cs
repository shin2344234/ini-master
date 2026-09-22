using IniMaster.Core;

namespace IniMaster.Tests;

public class UpdateTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v1.3.0",
          "name": "INI Master 1.3.0",
          "draft": false,
          "prerelease": false,
          "html_url": "https://github.com/shin2344234/ini-master/releases/tag/v1.3.0",
          "body": "First paragraph of the notes.\n\nSecond paragraph.",
          "assets": [
            { "name": "INIMaster-1.3.0.zip", "size": 100, "browser_download_url": "https://github.com/shin2344234/ini-master/releases/download/v1.3.0/INIMaster-1.3.0.zip" },
            { "name": "INIMaster.exe", "size": 61515528, "digest": "sha256:BFF19D283F991C20B98F17DAA5B3D6BF8F89EB5E1564A6BCE5067969DF7B2447",
              "browser_download_url": "https://github.com/shin2344234/ini-master/releases/download/v1.3.0/INIMaster.exe" }
          ]
        }
        """;

    [Fact]
    public void ReadsTheExeOutOfTheRelease()
    {
        var r = Updates.Parse(ReleaseJson)!;
        Assert.Equal(new Version(1, 3, 0), r.Version);
        Assert.Equal("v1.3.0", r.Tag);
        Assert.EndsWith("/INIMaster.exe", r.ExeUrl);
        Assert.Equal(61515528, r.Size);
        Assert.Equal("BFF19D283F991C20B98F17DAA5B3D6BF8F89EB5E1564A6BCE5067969DF7B2447", r.Sha256);
        Assert.StartsWith("First paragraph", r.Notes);
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    [InlineData("\"name\": \"INIMaster.exe\"", "\"name\": \"Something.exe\"")]
    [InlineData("https://github.com/shin2344234/ini-master/releases/download/v1.3.0/INIMaster.exe", "https://example.com/INIMaster.exe")]
    [InlineData("\"tag_name\": \"v1.3.0\"", "\"tag_name\": \"nightly\"")]
    public void OffersNothingWhenTheReleaseIsWrong(string from, string to) =>
        Assert.Null(Updates.Parse(ReleaseJson.Replace(from, to)));

    [Theory]
    [InlineData("v1.2.2", 1, 2, 2)]
    [InlineData("1.3", 1, 3, 0)]
    [InlineData("V2.0.1", 2, 0, 1)]
    public void ReadsTagsAsVersions(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), Updates.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void RejectsTagsThatAreNotVersions(string tag) => Assert.Null(Updates.ParseVersion(tag));

    [Fact]
    public void ComparesVersionsTheUsualWay()
    {
        Assert.True(new Version(1, 3, 0) > new Version(1, 2, 2));
        Assert.True(new Version(1, 10, 0) > new Version(1, 9, 9));
        Assert.False(new Version(1, 2, 2) > new Version(1, 2, 2));
    }

    [Theory]
    [InlineData("https://github.com/a/b/releases/download/v1/INIMaster.exe", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://api.github.com/x", true)]
    [InlineData("http://github.com/a/b", false)]
    [InlineData("https://github.com.example.com/x", false)]
    [InlineData("https://githubusercontent.com.evil.net/x", false)]
    [InlineData("https://example.com/INIMaster.exe", false)]
    [InlineData("file:///C:/INIMaster.exe", false)]
    [InlineData(null, false)]
    public void OnlyTakesDownloadsFromGitHub(string? url, bool ok) => Assert.Equal(ok, Updates.IsGitHubUrl(url));

    [Fact]
    public void UnsignedFilesNeverCountAsTheSameSigner()
    {
        var dir = Directory.CreateTempSubdirectory("inimaster-sig").FullName;
        try
        {
            var a = Path.Combine(dir, "a.exe");
            var b = Path.Combine(dir, "b.exe");
            File.WriteAllBytes(a, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
            File.Copy(a, b);
            Assert.Null(Updates.Signer(a));
            Assert.False(Updates.SameSigner(a, b));
            // Apply refuses rather than replacing a copy it cannot vouch for.
            Assert.Throws<InvalidOperationException>(() => Updates.Apply(b, a));
            Assert.Equal(4, new FileInfo(a).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// The signed build, when one has been published from this checkout.
    private static string? SignedExe
    {
        get
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(UpdateTests).Assembly.Location)!,
                "..", "..", "..", "..", "..", "dist", "INIMaster.exe");
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
    }

    [Fact]
    public void ReadsTheSignerOfASignedFile()
    {
        if (SignedExe is not { } exe) return;
        var signer = Updates.Signer(exe);
        Assert.NotNull(signer);
        Assert.Contains("Seth Walker", signer!.Value.Subject);
        Assert.True(Updates.SameSigner(exe, exe));
    }

    [Fact]
    public void ReplacesTheExeAndKeepsTheOldOneAside()
    {
        if (SignedExe is not { } exe) return;
        var dir = Directory.CreateTempSubdirectory("inimaster-apply").FullName;
        try
        {
            var target = Path.Combine(dir, "INIMaster.exe");
            var downloaded = Path.Combine(dir, "INIMaster-new.exe");
            File.Copy(exe, target);
            File.Copy(exe, downloaded);
            var size = new FileInfo(exe).Length;

            Updates.Apply(downloaded, target, restart: false);

            Assert.Equal(size, new FileInfo(target).Length);
            Assert.True(File.Exists(target + ".old"), "the copy it replaced is kept until the next start");
            Assert.False(File.Exists(downloaded), "the download is cleaned up");
            Assert.True(Updates.CanReplaceExe(target));

            Updates.CleanUp(target);
            Assert.False(File.Exists(target + ".old"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
