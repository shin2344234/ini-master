using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IniMaster.Core;

namespace IniMaster.Controls;

/// A box that records the next key pressed while it has focus and writes it
/// in the setting's own format: a virtual-key code, hex, or a name with
/// Ctrl, Shift and Alt joined by +. Tab still moves focus.
public sealed class KeyCaptureBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((KeyCaptureBox)d).ShowValue()));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(KeyCaptureBox), new PropertyMetadata("name", (d, _) => ((KeyCaptureBox)d).ShowValue()));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        ToolTip = "Click, then press the key you want. Tab moves on.";
        GotKeyboardFocus += (_, _) => ShowValue();
        LostKeyboardFocus += (_, _) => ShowValue();
    }

    private void ShowValue()
    {
        if (IsKeyboardFocused) { Text = "Press a key..."; return; }
        var v = Value ?? "";
        var described = KeyNames.Describe(v, Format);
        Text = Format is "vk" or "hex" && described != v ? $"{described}   ({v})" : described;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        if (key is Key.ImeProcessed or Key.DeadCharProcessed or Key.None) return;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        var mods = Keyboard.Modifiers;
        var isModifier = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt;
        if (isModifier)
        {
            // A modifier on its own is a key too (a run modifier, say).
            vk = key is Key.LeftCtrl or Key.RightCtrl ? 0x11 : key is Key.LeftShift or Key.RightShift ? 0x10 : 0x12;
            mods = ModifierKeys.None;
        }
        Commit(vk, mods);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (IsKeyboardFocused)
        {
            var vk = e.ChangedButton switch
            {
                MouseButton.Middle => 0x04,
                MouseButton.XButton1 => 0x05,
                MouseButton.XButton2 => 0x06,
                _ => 0,
            };
            if (vk != 0) { e.Handled = true; Commit(vk, Keyboard.Modifiers); return; }
        }
        base.OnPreviewMouseDown(e);
    }

    private void Commit(int vk, ModifierKeys mods)
    {
        if (vk <= 0) return;
        Value = KeyNames.Format(vk, mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Alt), Format);
        // Leave the box so the next key press goes back to the page.
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
}
