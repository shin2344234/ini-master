using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IniMaster.Core;

namespace IniMaster.Controls;

/// A button that opens a drawing of a keyboard. Clicking a key writes it in
/// the setting's own format, the same text the key box writes when that key
/// is pressed, for people who would rather look for a key than reach for it
/// or who want one the window cannot see being pressed. Ctrl, Shift and Alt
/// tick boxes join the key with + for settings stored as names.
public sealed class KeyPickerButton : Button
{
    private const double Unit = 34;
    private const double Gap = 2;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(KeyPickerButton),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(KeyPickerButton), new PropertyMetadata("name"));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    private Popup? _popup;
    private long _closedAt;
    private CheckBox? _ctrl, _shift, _alt;

    public KeyPickerButton()
    {
        Content = "";
        Click += (_, _) => Toggle();
    }

    private void Toggle()
    {
        // The popup closes on the mouse press that lands on this button, so
        // the click that follows would open it again straight away.
        if (_popup is { IsOpen: true }) { _popup.IsOpen = false; return; }
        if (Environment.TickCount64 - _closedAt < 250) return;

        _popup = Build();
        _popup.IsOpen = true;
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private Popup Build()
    {
        var vkOrHex = Format is "vk" or "hex";
        var hasCurrent = KeyNames.TryParse(Value ?? "", Format, out var current, out var ctrl, out var shift, out var alt);

        var canvas = new Canvas { Width = KeyboardLayout.Width * Unit, Height = KeyboardLayout.Height * Unit };

        var numpad = new TextBlock { Text = Loc.T("Numpad"), Style = (Style)Application.Current.FindResource("Caption") };
        Canvas.SetLeft(numpad, KeyboardLayout.NumpadX * Unit);
        Canvas.SetTop(numpad, 6);
        canvas.Children.Add(numpad);

        foreach (var cap in KeyboardLayout.Keys)
        {
            var isCurrent = hasCurrent && cap.Vk == current;
            var code = Format == "hex" ? "0x" + cap.Vk.ToString("X2") : cap.Vk.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var b = new Button
            {
                Content = cap.Label,
                Width = cap.W * Unit - Gap,
                Height = cap.H * Unit - Gap,
                Padding = new Thickness(0),
                FontSize = 11,
                Margin = new Thickness(0),
                // The name and code are what goes in the file, whatever the cap says.
                ToolTip = vkOrHex ? $"{KeyNames.Name(cap.Vk)}   {code}" : KeyNames.Name(cap.Vk),
            };
            if (isCurrent)
            {
                b.Background = Res("Selection");
                b.BorderBrush = Res("Gold");
            }
            var vk = cap.Vk;
            b.Click += (_, _) => Pick(vk);
            Canvas.SetLeft(b, cap.X * Unit);
            Canvas.SetTop(b, cap.Y * Unit);
            canvas.Children.Add(b);
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("Click the key you want."),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        });
        if (!vkOrHex)
        {
            // These are what the file reads, so they are not translated.
            _ctrl = Modifier("Ctrl", ctrl);
            _shift = Modifier("Shift", shift);
            _alt = Modifier("Alt", alt);
            header.Children.Add(_ctrl);
            header.Children.Add(_shift);
            header.Children.Add(_alt);
        }

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(canvas);

        // A popup outside the logical tree inherits from its placement target,
        // and this button's text is in the icon font, which has no letters.
        TextElement.SetFontFamily(body, (FontFamily)Application.Current.FindResource("BodyFont"));
        TextElement.SetFontSize(body, 13);

        var frame = new Border
        {
            Background = Res("Panel"),
            BorderBrush = Res("Bronze"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = body,
        };
        frame.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };

        var popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Child = frame,
        };
        popup.Closed += (_, _) => _closedAt = Environment.TickCount64;
        return popup;
    }

    private static CheckBox Modifier(string name, bool on) => new()
    {
        Content = name,
        IsChecked = on,
        Margin = new Thickness(0, 0, 14, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Pick(int vk)
    {
        // A modifier pressed as the key stands alone, as it does in the key box.
        var isModifier = vk is 0x10 or 0x11 or 0x12;
        var ctrl = !isModifier && _ctrl?.IsChecked == true;
        var shift = !isModifier && _shift?.IsChecked == true;
        var alt = !isModifier && _alt?.IsChecked == true;
        Value = KeyNames.Format(vk, ctrl, shift, alt, Format);
        Close();
    }

    private void Close()
    {
        if (_popup != null) _popup.IsOpen = false;
    }
}
