using IniMaster.Core;

namespace IniMaster.Tests;

public class KeyPickerTests
{
    [Theory]
    [InlineData("vk", "45", 0x2D, false, false, false)]
    [InlineData("hex", "0x2D", 0x2D, false, false, false)]
    [InlineData("name", "F11", 0x7A, false, false, false)]
    [InlineData("name", "Ctrl+F1", 0x70, true, false, false)]
    [InlineData("name", "ctrl+shift+alt+i", 0x49, true, true, true)]
    [InlineData("name", "Control+Insert", 0x2D, true, false, false)]
    public void ReadsAStoredValueBack(string format, string value, int vk, bool ctrl, bool shift, bool alt)
    {
        Assert.True(KeyNames.TryParse(value, format, out var gotVk, out var gotCtrl, out var gotShift, out var gotAlt));
        Assert.Equal((vk, ctrl, shift, alt), (gotVk, gotCtrl, gotShift, gotAlt));
    }

    [Theory]
    [InlineData("vk", "")]
    [InlineData("vk", "0")]
    [InlineData("vk", "300")]
    [InlineData("hex", "auto")]
    [InlineData("name", "None")]
    [InlineData("name", "")]
    [InlineData("name", "Ctrl+")]
    [InlineData("name", "Hyper+F1")]
    public void NoKeyAndNonsenseReadAsNoKey(string format, string value) =>
        Assert.False(KeyNames.TryParse(value, format, out _, out _, out _, out _));

    [Fact]
    public void EveryKeyOnThePickerHasAName()
    {
        foreach (var cap in KeyboardLayout.Keys)
            Assert.False(KeyNames.Name(cap.Vk).StartsWith("0x"), $"{cap.Label} (0x{cap.Vk:X2}) has no key name");
    }

    [Theory]
    [InlineData("vk")]
    [InlineData("hex")]
    [InlineData("name")]
    public void WhatThePickerWritesReadsBackAsTheSameKey(string format)
    {
        foreach (var cap in KeyboardLayout.Keys)
        {
            var text = KeyNames.Format(cap.Vk, ctrl: false, shift: false, alt: false, format);
            Assert.True(KeyNames.TryParse(text, format, out var vk, out _, out _, out _), $"{cap.Label}: {text}");
            Assert.Equal(cap.Vk, vk);
        }
    }

    [Fact]
    public void KeysStayInsideTheDrawingAndDoNotOverlap()
    {
        var keys = KeyboardLayout.Keys;
        foreach (var k in keys)
        {
            Assert.True(k.X >= 0 && k.X + k.W <= KeyboardLayout.Width + 1e-9, $"{k.Label} sticks out sideways");
            Assert.True(k.Y >= 0 && k.Y + k.H <= KeyboardLayout.Height + 1e-9, $"{k.Label} sticks out downwards");
        }
        for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
            {
                var a = keys[i];
                var b = keys[j];
                var apart = a.X + a.W <= b.X + 1e-9 || b.X + b.W <= a.X + 1e-9 || a.Y + a.H <= b.Y + 1e-9 || b.Y + b.H <= a.Y + 1e-9;
                Assert.True(apart, $"{a.Label} at {a.X},{a.Y} overlaps {b.Label} at {b.X},{b.Y}");
            }
    }

    [Fact]
    public void EveryKeyTheBoxCapturesCanBePickedToo()
    {
        // The key box writes F1 to F24, A to Z, 0 to 9, the numpad, the mouse
        // buttons and the named keys. Anything missing here is a key that can
        // be typed but not picked.
        var onPicker = KeyboardLayout.Keys.Select(k => k.Vk).ToHashSet();
        for (var vk = 0x70; vk <= 0x87; vk++) Assert.Contains(vk, onPicker);
        for (var vk = 'A'; vk <= 'Z'; vk++) Assert.Contains((int)vk, onPicker);
        for (var vk = '0'; vk <= '9'; vk++) Assert.Contains((int)vk, onPicker);
        for (var vk = 0x60; vk <= 0x6F; vk++) if (vk != 0x6C) Assert.Contains(vk, onPicker);
        foreach (var vk in new[] { 0x01, 0x02, 0x04, 0x05, 0x06, 0x08, 0x09, 0x0D, 0x10, 0x11, 0x12, 0x13, 0x14, 0x1B, 0x20,
                                   0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D,
                                   0x90, 0x91, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE })
            Assert.Contains(vk, onPicker);
    }

    [Fact]
    public void ADirectiveMarksAnyNumberAsAKeyCode()
    {
        const string ini = """
            [settings]
            ;@ type=key format=vk label="Open the menu"
            Toggle=45
            """;
        var view = IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini));
        var s = view.Settings.Single(x => x.Setting.Key == "Toggle").Setting;
        Assert.Equal(SettingTypes.Key, s.Type);
        Assert.Equal("vk", s.KeyFormat);
    }

    // A blank value and 0 read the same in every format, so neither can say
    // how the mod stores its keys. Reported on Discord on 2026-11-08.
    [Theory]
    [InlineData("", "0x2E", "hex")]
    [InlineData("", "46", "vk")]
    [InlineData("0", "0x2E", "hex")]
    [InlineData("0", "46", "vk")]
    [InlineData("46", "0x2E", "vk")]
    [InlineData("0x2E", "46", "hex")]
    [InlineData("0", null, "vk")]
    [InlineData("", null, "name")]
    public void AnEmptyKeyTakesItsFormatFromTheDefault(string value, string? def, string format)
    {
        var s = SettingResolver.Resolve("s", "Hotkey", value, new KeyMeta { Type = SettingTypes.Key, Default = def });
        Assert.Equal(format, s.KeyFormat);
    }

    // An empty key with no default shows no format, but help that calls it a
    // key code says the mod reads a number, so a picked key is not a name.
    [Theory]
    [InlineData("Windows virtual-key code. 35 = END.", "vk")]
    [InlineData("Virtual key code, such as 0x2D for Insert.", "hex")]
    [InlineData("The key that opens the menu.", "name")]
    public void AnEmptyKeyWithNoDefaultTakesItsFormatFromTheHelp(string help, string format)
    {
        var s = SettingResolver.Resolve("s", "Hotkey", "", new KeyMeta { Type = SettingTypes.Key, Help = help });
        Assert.Equal(format, s.KeyFormat);
    }

    [Fact]
    public void AGuessedKeyClearedToZeroTakesItsFormatFromTheDefault()
    {
        var s = SettingResolver.Resolve("s", "ToggleKey", "0", new KeyMeta { Help = "Windows virtual-key code.", Default = "0x2E" });
        Assert.Equal(SettingTypes.Key, s.Type);
        Assert.Equal("hex", s.KeyFormat);
    }

    [Theory]
    [InlineData("hex", "vk", "", "vk")]
    [InlineData("name", "vk", "", "vk")]
    [InlineData("vk", "hex", "0", "hex")]
    [InlineData("hex", "vk", "0x2E", "hex")]
    [InlineData("vk", "hex", "46", "vk")]
    [InlineData("hex", null, "", "hex")]
    public void AReloadKeepsTheFormatWhenTheValueCannotShowOne(string resolved, string? previous, string value, string format) =>
        Assert.Equal(format, SettingResolver.KeepKeyFormat(resolved, previous, value));

    [Fact]
    public void ADecimalKeyEmptiedInTheTextTabIsPickedAsDecimal()
    {
        var reloaded = SettingResolver.Resolve("s", "Hotkey", "", new KeyMeta { Type = SettingTypes.Key, Default = "0x2E" });
        var format = SettingResolver.KeepKeyFormat(reloaded.KeyFormat, "vk", "");
        Assert.Equal("46", KeyNames.Format(0x2E, false, false, false, format));
    }

    // blinding_flash_vision.ini, reported on Discord on 2026-10-27: after the
    // clear button wrote 0, ToggleKey came back as an on/off switch.
    [Theory]
    [InlineData("35")]
    [InlineData("0")]
    [InlineData("")]
    public void AClearedKeyCodeStaysAKey(string value)
    {
        var ini = $"""
            [Settings]
            ; Enable automatically at startup. 0 = start disabled.
            StartEnabled=1
            ; Windows virtual-key code. 35 = END. Read only with the game focused.
            ToggleKey={value}
            """;
        var view = IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini));
        var s = view.Settings.Single(x => x.Setting.Key == "ToggleKey").Setting;
        Assert.Equal(SettingTypes.Key, s.Type);
        Assert.Equal("vk", s.KeyFormat);
        Assert.Equal(SettingTypes.Bool, view.Settings.Single(x => x.Setting.Key == "StartEnabled").Setting.Type);
    }

    // Glint Spotter reads Key=Ctrl+F11 as the hex code C and Key=Down as 0D,
    // so a mod has to be able to say which keys it reads.
    private const string GlintMeta = """
        { "sections": { "GlintSpotter": { "keys": {
            "Key": { "type": "key", "format": "name", "keys": ["F1-F24", "A-Z", "0-9", "Insert", "Numpad0-Numpad9"], "modifiers": false }
        } } } }
        """;

    [Theory]
    [InlineData("F11", true)]
    [InlineData("F24", true)]
    [InlineData("Q", true)]
    [InlineData("Numpad7", true)]
    [InlineData("None", true)]
    [InlineData("", true)]
    [InlineData("Down", false)]
    [InlineData("Escape", false)]
    [InlineData("Mouse4", false)]
    [InlineData("Ctrl+F11", false)]
    public void AKeysListRefusesKeysTheModCannotRead(string value, bool ok)
    {
        var meta = MetaLoader.LoadJson(GlintMeta);
        var s = SettingResolver.Resolve("GlintSpotter", "Key", value, meta.Sections["GlintSpotter"].Keys["Key"]);
        Assert.False(s.KeyModifiers);
        Assert.Equal(ok, s.Validate(value) == null);
    }

    [Fact]
    public void ADirectiveCarriesTheKeysListAndModifiers()
    {
        const string ini = """
            [settings]
            ;@ type=key format=name keys=F1-F12,Insert modifiers=0
            Hotkey=F5
            """;
        var view = IniView.Build(new IniTarget { IniPath = "x.ini", Meta = new ModMeta() }, IniDocument.Parse(ini));
        var s = view.Settings.Single(x => x.Setting.Key == "Hotkey").Setting;
        Assert.False(s.KeyModifiers);
        Assert.True(s.AllowsKey(0x2D));
        Assert.True(s.AllowsKey(0x7B));
        Assert.False(s.AllowsKey(0x7C));
        Assert.NotNull(s.Validate("Shift+F5"));
    }

    [Fact]
    public void WithoutAKeysListEveryKeyAndModifierPasses()
    {
        var s = SettingResolver.Resolve("s", "Hotkey", "Ctrl+Mouse4", new KeyMeta { Type = SettingTypes.Key, Format = "name" });
        Assert.True(s.KeyModifiers);
        Assert.Null(s.AllowedKeys);
        Assert.Null(s.Validate("Ctrl+Mouse4"));
    }

    [Fact]
    public void AKeysListWithANameItDoesNotKnowRestrictsNothing()
    {
        Assert.Null(KeyNames.ExpandKeyList(new[] { "F1-F12", "VK_OEM_3" }));
        Assert.Null(KeyNames.ExpandKeyList(new[] { "OEM_3", "VK_F1" }));
        Assert.Null(KeyNames.ExpandKeyList(new[] { "None" }));
        Assert.Equal(12, KeyNames.ExpandKeyList(new[] { "F1-F12", "None" })!.Count);

        var s = SettingResolver.Resolve("s", "Hotkey", "Q",
            new KeyMeta { Type = SettingTypes.Key, Format = "name", Keys = new() { "VK_F1", "VK_F2" } });
        Assert.Null(s.AllowedKeys);
        Assert.Null(s.Validate("Q"));
    }
}
