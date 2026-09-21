using IniMaster.Core;

namespace IniMaster.Tests;

public class AnalyzerTests
{
    private static IniView View(string ini, ModMeta? meta = null)
    {
        var target = new IniTarget { IniPath = "x.ini", Meta = meta ?? new ModMeta() };
        return IniView.Build(target, IniDocument.Parse(ini));
    }

    private static ResolvedSetting S(IniView v, string key) => v.Settings.Single(s => s.Setting.Key == key).Setting;

    private const string Sample = """
        ; My Mod 1.0 for Crimson Desert.
        ;
        ; Hand edits are picked up within a second while the game runs.

        [settings]

        ; ------------------------------------------------ marking

        ; The flying ceiling.
        ;   0        leave the game's 1350.0
        ;   -1       no ceiling
        ;   <number> that height, e.g. 5000
        Ceiling=-1

        ; Regions carry a block list.
        ;   0  regions keep their block lists
        ;   1  no region blocks any mount
        NoFlyZones=1

        ; 1 buzzes the controller when a pin lands.
        Rumble=1

        ; SSRTGI quality: 0 = ULTRA, 1 = HIGH, 2 = MEDIUM, 3 = LOW. Ignored while Overdrive is active.
        RayTracingQuality=1

        ; Select Upscaler for Dx12 games
        ; xess, fsr21, fsr22, fsr31 (also for FSR4), dlss - Default (auto) is xess
        Dx12Upscaler = auto

        ; A percentage of the game's own rate, 100 to 10000, where 100 leaves it alone.
        MountRegenPercent=1000

        ; Takes effect next start.
        Enabled=true

        ;@ type=int min=0 max=100 unit=% label=Use cost
        UsePercent=0

        ; Private Storage, the camp storage box
        PrivateStorageKey=Ctrl+F1
        PrivateStoragePad=LB+LS

        MenuKey=45
        OreBonus=3
        Scale=1.5
        """;

    [Fact]
    public void ReadsHeaderAsModDescriptionAndLiveHint()
    {
        var v = View(Sample);
        Assert.StartsWith("My Mod 1.0", v.Meta.Description);
        Assert.True(v.Meta.Live);
    }

    [Fact]
    public void DividerBecomesGroup()
    {
        var v = View(Sample);
        Assert.Contains(v.Sections[0].Items, i => i is ViewGroup { Title: "marking" });
    }

    [Fact]
    public void IndentedTableWithPlaceholderIsEditableEnum()
    {
        var c = S(View(Sample), "Ceiling");
        Assert.Equal(SettingTypes.Enum, c.Type);
        Assert.True(c.AllowCustom);
        Assert.Equal(new[] { "0", "-1" }, c.Options.Select(o => o.Value));
        Assert.Equal("The flying ceiling.\n  0        leave the game's 1350.0\n  -1       no ceiling\n  <number> that height, e.g. 5000", c.Help);
    }

    [Fact]
    public void ZeroOneTableIsBoolOnlyWithShortLabels()
    {
        Assert.Equal(SettingTypes.Enum, S(View(Sample), "NoFlyZones").Type);
        var k = S(View("[s]\n; Keep it.\n;   0  off\n;   1  on\nKeepCatch=1\n"), "KeepCatch");
        Assert.Equal(SettingTypes.Bool, k.Type);
        Assert.Equal("1", k.TrueValue);
        Assert.Equal(SettingTypes.Bool, S(View(Sample), "Rumble").Type);
    }

    [Fact]
    public void NumericNamesAreNotBool()
    {
        var v = View("[s]\nUsePercent=0\nHoldLines=0\nDebugMode=0\nShowHud=1\n");
        Assert.Equal(SettingTypes.Int, S(v, "UsePercent").Type);
        Assert.Equal(SettingTypes.Int, S(v, "HoldLines").Type);
        Assert.Equal(SettingTypes.Int, S(v, "DebugMode").Type);
        Assert.Equal(SettingTypes.Bool, S(v, "ShowHud").Type);
    }

    [Fact]
    public void SectionRuleGivesChoices()
    {
        var v = View("[a]\nX=1\n\n; class -> 1 loot, 0 skip (classes not listed are looted)\n[Classes]\nore=1\nwood=0\n");
        Assert.Equal(SettingTypes.Bool, S(v, "ore").Type);
        Assert.Equal("ore", S(v, "ore").Label);
    }

    [Fact]
    public void ParenthesesKeepTheirCommas()
    {
        var v = View("[u]\n; fsr22 (native DX11), xess (native DX11, Arc only), dlss - Default (auto) is fsr22\nDx11Upscaler = auto\n");
        Assert.Equal(new[] { "auto", "fsr22", "xess", "dlss" }, S(v, "Dx11Upscaler").Options.Select(o => o.Value));
    }

    [Fact]
    public void InlinePairsAndOptiScalerLists()
    {
        var v = View(Sample);
        var rt = S(v, "RayTracingQuality");
        Assert.Equal(SettingTypes.Enum, rt.Type);
        Assert.Equal(new[] { "ULTRA", "HIGH", "MEDIUM", "LOW" }, rt.Options.Select(o => o.Label));

        var up = S(v, "Dx12Upscaler");
        Assert.Equal(SettingTypes.Enum, up.Type);
        Assert.Equal(new[] { "auto", "xess", "fsr21", "fsr22", "fsr31", "dlss" }, up.Options.Select(o => o.Value));
    }

    [Fact]
    public void GuessesRangeAndTypes()
    {
        var v = View(Sample);
        var m = S(v, "MountRegenPercent");
        Assert.Equal(SettingTypes.Int, m.Type);
        Assert.Equal(100, m.Min);
        Assert.Equal(10000, m.Max);
        Assert.True(m.RangeGuessed);
        Assert.Null(m.Validate("50"));
        Assert.NotNull(m.Warn("50"));

        var e = S(v, "Enabled");
        Assert.Equal(SettingTypes.Bool, e.Type);
        Assert.Equal("true", e.TrueValue);
        Assert.Equal("false", e.FalseValue);
        Assert.False(e.Live);

        Assert.Equal(SettingTypes.Key, S(v, "MenuKey").Type);
        Assert.Equal("vk", S(v, "MenuKey").KeyFormat);
        Assert.Equal(SettingTypes.Key, S(v, "PrivateStorageKey").Type);
        Assert.Equal("name", S(v, "PrivateStorageKey").KeyFormat);
        Assert.Equal(SettingTypes.Int, S(v, "OreBonus").Type);
        Assert.Equal(SettingTypes.Float, S(v, "Scale").Type);
        Assert.Equal(SettingTypes.String, S(v, "PrivateStoragePad").Type);
    }

    [Fact]
    public void DirectivesOverrideAndValidate()
    {
        var u = S(View(Sample), "UsePercent");
        Assert.Equal("Use cost", u.Label);
        Assert.Equal("%", u.Unit);
        Assert.Equal(SettingTypes.Int, u.Type);
        Assert.False(u.TypeGuessed);
        Assert.NotNull(u.Validate("101"));
        Assert.Null(u.Validate("100"));
    }

    [Fact]
    public void KeyWithoutCommentSharesPreviousHelp()
    {
        var v = View(Sample);
        Assert.Equal("Private Storage, the camp storage box", S(v, "PrivateStoragePad").Help);
    }

    [Fact]
    public void DirectiveParsing()
    {
        var (words, pairs) = CommentAnalyzer.ParseDirective("@ advanced type=enum options=0:Off|1:On label=Two words here unit=\"m s\"");
        Assert.Equal(new[] { "advanced" }, words);
        Assert.Equal("Two words here", pairs.Single(p => p.Key == "label").Value);
        Assert.Equal("m s", pairs.Single(p => p.Key == "unit").Value);
        Assert.Equal("0:Off|1:On", pairs.Single(p => p.Key == "options").Value);
    }

    [Fact]
    public void JsonMetadataWinsOverComments()
    {
        var json = """
            {
              // comments and trailing commas are fine
              "name": "My Mod",
              "live": false,
              "sections": {
                "settings": {
                  "label": "General",
                  "keys": {
                    "OreBonus": { "type": "int", "min": 0, "max": 10, "help": ["Extra ore.", "Per node."], "group": "Loot" },
                    "Scale": "Just a help string.",
                    "NewKey": { "type": "enum", "options": { "a": "Alpha", "b": "Beta" }, "default": "b" },
                  },
                },
              },
            }
            """;
        var meta = MetaLoader.Load(json, MetaSource.Embedded, "test");
        var v = View(Sample, meta);
        Assert.Equal("My Mod", v.Meta.Name);
        Assert.False(v.Meta.Live);
        Assert.Equal("General", v.Sections[0].Title);
        var ore = S(v, "OreBonus");
        Assert.Equal("Extra ore.\nPer node.", ore.Help);
        Assert.Equal(10, ore.Max);
        Assert.Equal("Just a help string.", S(v, "Scale").Help);
        var nk = v.Settings.Single(s => s.Setting.Key == "NewKey");
        Assert.Null(nk.FileValue);
        Assert.Equal("b", nk.Setting.Default);
        Assert.Equal("Alpha", nk.Setting.Options[0].Label);
        Assert.Contains(v.Sections[0].Items, i => i is ViewGroup { Title: "Loot" });
    }

    [Fact]
    public void JsonMayOpenWithComments()
    {
        Assert.True(MetaLoader.LooksLikeJson("// note\n/* more */\n  {\"name\":\"x\"}"));
        Assert.False(MetaLoader.LooksLikeJson("; a comment\n[s]\nA=1\n"));
        Assert.Equal("x", MetaLoader.Load("// header\n{ \"name\": \"x\" }", MetaSource.Embedded, "t").Name);
    }

    [Fact]
    public void AnnotatedIniMetadataSuppliesDefaults()
    {
        var meta = MetaLoader.Load("""
            ;@mod name="Annotated" live=1
            [settings]
            ; Costs charged once per use.
            ;@ min=0 max=100
            UsePercent=100
            """, MetaSource.Sidecar, "Mod.inimeta");
        Assert.Equal("Annotated", meta.Name);
        Assert.True(meta.Live);
        var k = meta.FindKey("settings", "UsePercent")!;
        Assert.Equal("100", k.Default);
        Assert.Equal("Costs charged once per use.", k.Help);
        Assert.Equal(100, k.Max);
        Assert.NotNull(meta.DefaultIniText);
    }

    [Theory]
    [InlineData("MountRegenPercent", "Mount regen percent")]
    [InlineData("HookDX12", "Hook DX12")]
    [InlineData("NoFlyZones", "No fly zones")]
    [InlineData("Dx12Upscaler", "Dx12 upscaler")]
    [InlineData("LETMESLEEP", "LETMESLEEP")]
    public void Humanizes(string key, string label) => Assert.Equal(label, SettingResolver.Humanize(key));
}
