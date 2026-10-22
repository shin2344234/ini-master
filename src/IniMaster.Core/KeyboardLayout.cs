namespace IniMaster.Core;

/// One key on the picker's keyboard. X, Y, W and H are in key widths from the
/// top left corner, so a 1.5 wide Tab key at X 0 ends where the Q key starts.
public readonly record struct KeyCap(int Vk, string Label, double X, double Y, double W = 1, double H = 1);

/// Where each key sits on the picker's on-screen keyboard: a full-size layout
/// with the function row, the main block, the navigation keys and the
/// numpad, then the mouse buttons and F13 to F24 underneath. The caps carry
/// the virtual-key codes the key box writes, so both end up with the same value.
public static class KeyboardLayout
{
    /// Width and height of the whole drawing, in key widths.
    public const double Width = 23;
    public const double Height = 7.4;

    /// Where the numpad's own caption goes.
    public const double NumpadX = 19;

    public static IReadOnlyList<KeyCap> Keys { get; } = Build();

    private static List<KeyCap> Build()
    {
        var k = new List<KeyCap>();
        void Cap(int vk, string label, double x, double y, double w = 1, double h = 1) => k.Add(new KeyCap(vk, label, x, y, w, h));

        // Function row.
        Cap(0x1B, "Esc", 0, 0);
        double[] fx = { 2, 3, 4, 5, 6.5, 7.5, 8.5, 9.5, 11, 12, 13, 14 };
        for (var i = 0; i < 12; i++) Cap(0x70 + i, "F" + (i + 1), fx[i], 0);
        Cap(0x2C, "PrtSc", 15.5, 0);
        Cap(0x91, "ScrLk", 16.5, 0);
        Cap(0x13, "Pause", 17.5, 0);

        // Number row.
        const double r1 = 1.2;
        Cap(0xC0, "`", 0, r1);
        for (var i = 1; i <= 10; i++) Cap(0x30 + i % 10, (i % 10).ToString(), i, r1);
        Cap(0xBD, "-", 11, r1);
        Cap(0xBB, "=", 12, r1);
        Cap(0x08, "Backspace", 13, r1, 2);

        // Top letter row.
        const double r2 = 2.2;
        Cap(0x09, "Tab", 0, r2, 1.5);
        var top = "QWERTYUIOP";
        for (var i = 0; i < top.Length; i++) Cap(top[i], top[i].ToString(), 1.5 + i, r2);
        Cap(0xDB, "[", 11.5, r2);
        Cap(0xDD, "]", 12.5, r2);
        Cap(0xDC, "\\", 13.5, r2, 1.5);

        // Home row.
        const double r3 = 3.2;
        Cap(0x14, "Caps", 0, r3, 1.75);
        var home = "ASDFGHJKL";
        for (var i = 0; i < home.Length; i++) Cap(home[i], home[i].ToString(), 1.75 + i, r3);
        Cap(0xBA, ";", 10.75, r3);
        Cap(0xDE, "'", 11.75, r3);
        Cap(0x0D, "Enter", 12.75, r3, 2.25);

        // Bottom letter row.
        const double r4 = 4.2;
        Cap(0x10, "Shift", 0, r4, 2.25);
        var low = "ZXCVBNM";
        for (var i = 0; i < low.Length; i++) Cap(low[i], low[i].ToString(), 2.25 + i, r4);
        Cap(0xBC, ",", 9.25, r4);
        Cap(0xBE, ".", 10.25, r4);
        Cap(0xBF, "/", 11.25, r4);
        Cap(0x10, "Shift", 12.25, r4, 2.75);

        // Space row.
        const double r5 = 5.2;
        Cap(0x11, "Ctrl", 0, r5, 1.25);
        Cap(0x5B, "Win", 1.25, r5, 1.25);
        Cap(0x12, "Alt", 2.5, r5, 1.25);
        Cap(0x20, "Space", 3.75, r5, 6.25);
        Cap(0x12, "Alt", 10, r5, 1.25);
        Cap(0x5C, "Win", 11.25, r5, 1.25);
        Cap(0x5D, "Menu", 12.5, r5, 1.25);
        Cap(0x11, "Ctrl", 13.75, r5, 1.25);

        // Navigation cluster.
        Cap(0x2D, "Ins", 15.5, r1);
        Cap(0x24, "Home", 16.5, r1);
        Cap(0x21, "PgUp", 17.5, r1);
        Cap(0x2E, "Del", 15.5, r2);
        Cap(0x23, "End", 16.5, r2);
        Cap(0x22, "PgDn", 17.5, r2);
        Cap(0x26, "↑", 16.5, r4);
        Cap(0x25, "←", 15.5, r5);
        Cap(0x28, "↓", 16.5, r5);
        Cap(0x27, "→", 17.5, r5);

        // Numpad.
        Cap(0x90, "Num", NumpadX, r1);
        Cap(0x6F, "/", NumpadX + 1, r1);
        Cap(0x6A, "*", NumpadX + 2, r1);
        Cap(0x6D, "-", NumpadX + 3, r1);
        Cap(0x67, "7", NumpadX, r2);
        Cap(0x68, "8", NumpadX + 1, r2);
        Cap(0x69, "9", NumpadX + 2, r2);
        Cap(0x6B, "+", NumpadX + 3, r2, 1, 2);
        Cap(0x64, "4", NumpadX, r3);
        Cap(0x65, "5", NumpadX + 1, r3);
        Cap(0x66, "6", NumpadX + 2, r3);
        Cap(0x61, "1", NumpadX, r4);
        Cap(0x62, "2", NumpadX + 1, r4);
        Cap(0x63, "3", NumpadX + 2, r4);
        Cap(0x60, "0", NumpadX, r5, 2);
        Cap(0x6E, ".", NumpadX + 2, r5);

        // Mouse buttons, then the function keys past F12.
        const double r6 = 6.4;
        for (var i = 1; i <= 5; i++) Cap(i == 3 ? 0x04 : i > 3 ? 0x01 + i : i, "Mouse" + i, (i - 1) * 2, r6, 1.9);
        for (var i = 13; i <= 24; i++) Cap(0x6F + i, "F" + i, 10.5 + (i - 13), r6);
        return k;
    }
}
