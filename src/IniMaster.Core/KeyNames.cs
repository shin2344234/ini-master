using System.Globalization;

namespace IniMaster.Core;

/// Windows virtual-key codes and the key names Crimson Desert plugins
/// already accept in their ini files (F11, Ctrl+F1, Numpad0, PageUp...).
public static class KeyNames
{
    private static readonly Dictionary<int, string> ByVk = new();
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt" };

    static KeyNames()
    {
        void Add(int vk, string name, params string[] aliases)
        {
            ByVk.TryAdd(vk, name);
            ByName.TryAdd(name, vk);
            foreach (var a in aliases) ByName.TryAdd(a, vk);
        }

        Add(0x01, "Mouse1", "LButton");
        Add(0x02, "Mouse2", "RButton");
        Add(0x04, "Mouse3", "MButton");
        Add(0x05, "Mouse4", "XButton1");
        Add(0x06, "Mouse5", "XButton2");
        Add(0x08, "Backspace", "Back");
        Add(0x09, "Tab");
        Add(0x0D, "Enter", "Return");
        Add(0x10, "Shift");
        Add(0x11, "Ctrl", "Control");
        Add(0x12, "Alt", "Menu");
        Add(0x13, "Pause", "Break");
        Add(0x14, "CapsLock", "Capital");
        Add(0x1B, "Escape", "Esc");
        Add(0x20, "Space", "Spacebar");
        Add(0x21, "PageUp", "Prior", "PgUp");
        Add(0x22, "PageDown", "Next", "PgDn");
        Add(0x23, "End");
        Add(0x24, "Home");
        Add(0x25, "Left");
        Add(0x26, "Up");
        Add(0x27, "Right");
        Add(0x28, "Down");
        Add(0x2C, "PrintScreen", "Snapshot", "PrtSc");
        Add(0x2D, "Insert", "Ins");
        Add(0x2E, "Delete", "Del");
        for (var i = 0; i <= 9; i++) Add(0x30 + i, i.ToString(CultureInfo.InvariantCulture), "D" + i);
        for (var c = 'A'; c <= 'Z'; c++) Add(c, c.ToString());
        Add(0x5B, "LWin");
        Add(0x5C, "RWin");
        Add(0x5D, "Apps");
        for (var i = 0; i <= 9; i++) Add(0x60 + i, "Numpad" + i, "Num" + i, "NumPad" + i);
        Add(0x6A, "Multiply");
        Add(0x6B, "Add");
        Add(0x6C, "Separator");
        Add(0x6D, "Subtract");
        Add(0x6E, "Decimal");
        Add(0x6F, "Divide");
        for (var i = 1; i <= 24; i++) Add(0x6F + i, "F" + i);
        Add(0x90, "NumLock");
        Add(0x91, "ScrollLock", "Scroll");
        Add(0xA0, "LShift");
        Add(0xA1, "RShift");
        Add(0xA2, "LCtrl");
        Add(0xA3, "RCtrl");
        Add(0xA4, "LAlt");
        Add(0xA5, "RAlt");
        Add(0xBA, "Semicolon");
        Add(0xBB, "Equals", "Plus");
        Add(0xBC, "Comma");
        Add(0xBD, "Minus");
        Add(0xBE, "Period");
        Add(0xBF, "Slash");
        Add(0xC0, "Tilde", "Grave", "Backtick");
        Add(0xDB, "LeftBracket");
        Add(0xDC, "Backslash");
        Add(0xDD, "RightBracket");
        Add(0xDE, "Quote", "Apostrophe");
        ByName.TryAdd("Control", 0x11);
    }

    public static string Name(int vk) => ByVk.TryGetValue(vk, out var n) ? n : "0x" + vk.ToString("X2");

    public static bool TryName(string name, out int vk) => ByName.TryGetValue(name.Trim(), out vk);

    public static bool TryParseVk(string text, out int vk)
    {
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 256;
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 256;
    }

    /// "F11", "Ctrl+F1", "Shift+I", "None".
    public static bool LooksLikeKeyName(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (t.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = t.Split('+');
        if (parts.Any(p => p.Trim().Length == 0)) return false;
        for (var i = 0; i < parts.Length - 1; i++)
            if (!Modifiers.Contains(parts[i].Trim(), StringComparer.OrdinalIgnoreCase) && !parts[i].Trim().Equals("Control", StringComparison.OrdinalIgnoreCase)) return false;
        var last = parts[^1].Trim();
        // A single digit on its own is a number, not the 0 key.
        if (parts.Length == 1 && last.All(char.IsDigit)) return false;
        return ByName.ContainsKey(last);
    }

    /// Builds the text to write for a key press in the setting's own format.
    public static string Format(int vk, bool ctrl, bool shift, bool alt, string format)
    {
        switch (format)
        {
            case "vk": return vk.ToString(CultureInfo.InvariantCulture);
            case "hex": return "0x" + vk.ToString("X2");
            default:
                var parts = new List<string>();
                if (ctrl) parts.Add("Ctrl");
                if (shift) parts.Add("Shift");
                if (alt) parts.Add("Alt");
                parts.Add(Name(vk));
                return string.Join('+', parts);
        }
    }

    /// What to show next to the stored value: "Insert" for 45, or the name
    /// itself.
    public static string Describe(string value, string format)
    {
        if (format is "vk" or "hex")
            return TryParseVk(value, out var vk) ? Name(vk) : value.Trim() is "0" or "" ? "None" : value;
        return value.Length == 0 ? "None" : value;
    }

    public static string NoneValue(string format) => format is "vk" or "hex" ? "0" : "None";
}
