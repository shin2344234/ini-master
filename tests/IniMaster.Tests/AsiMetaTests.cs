using IniMaster.Core;

namespace IniMaster.Tests;

public class AsiMetaTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ReadsResourceAndMarkerFromBuiltPlugin()
    {
        var found = AsiMetaReader.Read(Fixture("ExampleMod.asi"));
        Assert.Equal(2, found.Count);
        Assert.StartsWith("resource INIMETA/", found[0].Where);
        Assert.Contains("\"Example Mod\"", found[0].Text);
        Assert.Equal("embedded marker", found[1].Where);
        Assert.StartsWith("{", found[1].Text);
    }

    [Fact]
    public void ScannerMergesEmbeddedLayersOverTheIni()
    {
        var root = Directory.CreateTempSubdirectory("inimaster-game").FullName;
        var bin = Directory.CreateDirectory(Path.Combine(root, "bin64")).FullName;
        File.WriteAllText(Path.Combine(bin, GameLocator.ExeName), "");
        File.Copy(Fixture("ExampleMod.asi"), Path.Combine(bin, "ExampleMod.asi"));
        File.Copy(Fixture("ExampleMod.asi"), Path.Combine(bin, "Disabled.asi_off"));
        File.WriteAllText(Path.Combine(bin, "ExampleMod.ini"), "[settings]\n; from the ini\nSpeed=2.0\nEnabled=1\n");
        File.WriteAllText(Path.Combine(bin, "winmm.ini"), "[Globals]\nLoadPlugins=1\n");

        Assert.Equal(root, GameLocator.Normalize(bin));
        var mods = ModScanner.Scan(root, null);
        Assert.Equal(2, mods.Count);
        var mod = mods.Single(m => m.HasPlugin);
        Assert.Equal("Example Mod", mod.DisplayName);
        Assert.Equal(MetaSource.Embedded, mod.BestSource);

        var target = mod.Files.Single();
        var view = IniView.Build(target, IniDocument.Load(File.ReadAllBytes(target.IniPath)));
        Assert.True(view.Meta.Live);
        var speed = view.Settings.Single(s => s.Setting.Key == "Speed");
        Assert.Equal("Travel speed", speed.Setting.Label);
        Assert.Equal("x", speed.Setting.Unit);
        Assert.Equal(0.25, speed.Setting.Step);
        Assert.Equal(4, speed.Setting.Max);
        Assert.StartsWith("How fast you move", speed.Setting.Help);
        Assert.Equal("2.0", speed.FileValue);
        // Keys only the metadata knows appear with their defaults.
        var key = view.Settings.Single(s => s.Setting.Key == "MenuKey");
        Assert.Null(key.FileValue);
        Assert.Equal(SettingTypes.Key, key.Setting.Type);
        Assert.Contains(view.Sections[0].Items, i => i is ViewGroup { Title: "Movement" });
        Directory.Delete(root, true);
    }
}
