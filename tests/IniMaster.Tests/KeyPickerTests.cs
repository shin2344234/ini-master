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

    // blinding_flash_vision.ini, reported on Discord on 2026-10-27: after the
    // clear button wrote 0, ToggleKey came back as an on/off switch.
    [Theory]
    [InlineData("35")]
    [InlineData("0")]
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
}
