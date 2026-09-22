using System.Diagnostics;
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
    public void VersionComesFromTheExeNotThisLibrary()
    {
        // IniMaster.Core carries no version of its own, so reading it would
        // say 1.0.0 and make every release look like an update.
        Assert.Equal(new Version(1, 0, 0), typeof(Updates).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : null);
        if (SignedExe is not { } exe) return;
        var published = Updates.VersionOf(null, exe);
        Assert.NotNull(published);
        Assert.True(published > new Version(1, 0, 0), $"read {published} from the exe");
        Assert.Equal(FileVersionInfo.GetVersionInfo(exe).FileVersion, $"{published}.0");
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

    /// The certificate stays readable on a file that was edited after signing,
    /// so reading it is not a check. Windows has to say the signature still
    /// covers the bytes.
    [Fact]
    public void AnEditedCopyIsNotTheSameSigner()
    {
        if (SignedExe is not { } exe) return;
        var dir = Directory.CreateTempSubdirectory("inimaster-tamper").FullName;
        try
        {
            var good = Path.Combine(dir, "good.exe");
            var edited = Path.Combine(dir, "edited.exe");
            File.Copy(exe, good);
            var bytes = File.ReadAllBytes(exe);
            bytes[0x10000] ^= 0xFF;
            File.WriteAllBytes(edited, bytes);

            Assert.True(Updates.HasValidSignature(good));
            Assert.False(Updates.HasValidSignature(edited));
            // The certificate is still there and still reads as the same one.
            Assert.Equal(Updates.Signer(good)!.Value.Thumbprint, Updates.Signer(edited)!.Value.Thumbprint);
            Assert.False(Updates.SameSigner(good, edited));
            Assert.Throws<InvalidOperationException>(() => Updates.Apply(edited, good));
            Assert.Equal(new FileInfo(exe).Length, new FileInfo(good).Length);
            Assert.False(File.Exists(good + ".old"));
            Assert.False(File.Exists(good + ".new"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RefusesADownloadWithNoChecksumToCheckItAgainst()
    {
        var withDigest = Updates.Parse(ReleaseJson)!;
        var none = Updates.Parse(ReleaseJson.Replace("\"digest\": \"sha256:BFF19D283F991C20B98F17DAA5B3D6BF8F89EB5E1564A6BCE5067969DF7B2447\",", ""))!;
        Assert.Null(none.Sha256);
        Assert.NotNull(withDigest.Sha256);
        // A digest in some other algorithm is no better than none.
        Assert.Null(Updates.Parse(ReleaseJson.Replace("sha256:BFF19D28", "md5:BFF19D28"))!.Sha256);

        using var http = new HttpClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Updates.DownloadAsync(none, http, null));
        Assert.Contains("SHA-256", ex.Message);
    }

    /// A failed install can leave part of a file where the exe belongs, and
    /// putting the working copy back has to win over it.
    [Fact]
    public void PutsTheWorkingCopyBackOverAPartialInstall()
    {
        var dir = Directory.CreateTempSubdirectory("inimaster-restore").FullName;
        try
        {
            var target = Path.Combine(dir, "INIMaster.exe");
            var old = target + ".old";
            File.WriteAllText(old, "the copy that was running");
            File.WriteAllText(target, "half a file");

            Updates.Restore(old, target);

            Assert.Equal("the copy that was running", File.ReadAllText(target));
            Assert.False(File.Exists(old));
        }
        finally { Directory.Delete(dir, true); }
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
            Assert.False(File.Exists(target + ".new"), "the staged copy is moved, not left behind");
            Assert.False(File.Exists(downloaded), "the download is cleaned up");
            Assert.True(Updates.CanReplaceExe(target));

            Updates.CleanUp(target);
            Assert.False(File.Exists(target + ".old"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
