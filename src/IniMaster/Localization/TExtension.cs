using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using IniMaster.Core;

namespace IniMaster.Localization;

/// One English string in the current language. Bindings to Value update
/// when the language changes, so the window relabels itself in place.
public sealed class LocString : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocString> Cache = new(StringComparer.Ordinal);
    private static readonly PropertyChangedEventArgs ValueArgs = new(nameof(Value));

    static LocString() => Loc.Changed += () =>
    {
        foreach (var s in Cache.Values) s.PropertyChanged?.Invoke(s, ValueArgs);
    };

    private LocString(string english) => English = english;

    public static LocString For(string english)
    {
        if (!Cache.TryGetValue(english, out var s)) Cache[english] = s = new LocString(english);
        return s;
    }

    public string English { get; }
    public string Value => Loc.Lookup(English);
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// {l:T 'English text'} in XAML. With Path, the bound value fills {0}:
/// {l:T 'by {0}', Path=Meta.Author}. Apostrophes in the text are written \'.
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var entry = new Binding(nameof(LocString.Value)) { Source = LocString.For(Text), Mode = BindingMode.OneWay };
        if (Path == null) return entry.ProvideValue(serviceProvider);
        var multi = new MultiBinding { Converter = FormatConverter.Instance, Mode = BindingMode.OneWay };
        multi.Bindings.Add(entry);
        multi.Bindings.Add(new Binding(Path));
        return multi.ProvideValue(serviceProvider);
    }
}

/// The first value is a format string, the rest fill its placeholders.
public sealed class FormatConverter : IMultiValueConverter
{
    public static readonly FormatConverter Instance = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not string format) return "";
        var args = values.Skip(1).Select(v => v == DependencyProperty.UnsetValue ? "" : v).ToArray();
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
