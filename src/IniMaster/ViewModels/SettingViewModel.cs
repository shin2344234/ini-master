using System.Globalization;
using System.Text;
using IniMaster.Core;

namespace IniMaster.ViewModels;

public abstract class ItemViewModel : ObservableObject
{
    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

public sealed class NoteViewModel : ItemViewModel
{
    public NoteViewModel(string text) => Text = text;
    public string Text { get; }
}

public sealed class GroupViewModel : ItemViewModel
{
    public GroupViewModel(string title) => Title = title.Length == 0 ? title : char.ToUpperInvariant(title[0]) + title[1..];
    public string Title { get; }
}

public sealed class SettingViewModel : ItemViewModel
{
    private readonly IniFileViewModel _owner;
    private string _value;
    private string? _fileValue;
    private bool _suppress;

    public SettingViewModel(IniFileViewModel owner, ResolvedSetting setting, string? fileValue)
    {
        _owner = owner;
        R = setting;
        _fileValue = fileValue;
        _value = fileValue ?? setting.Default ?? "";
        ResetCommand = new RelayCommand(() => Value = R.Default ?? "", () => CanReset);
        RevertCommand = new RelayCommand(() => Value = SavedValue, () => IsDirty);
        ClearKeyCommand = new RelayCommand(() => Value = KeyNames.NoneValue(R.KeyFormat));
    }

    public ResolvedSetting R { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand ClearKeyCommand { get; }

    public string Section => R.Section;
    public string Key => R.Key;
    public string Label => R.Label;
    public string Type => R.Type;
    public string? Unit => R.Unit;
    public bool InFile => _fileValue != null;
    public bool ReadOnly => R.ReadOnly;
    public bool AllowCustom => R.AllowCustom;
    public IReadOnlyList<OptionMeta> Options => R.Options;
    public bool HasSlider => R.IsNumeric && R.Min != null && R.Max != null && R.Max > R.Min;
    public double SliderMin => R.Min ?? 0;
    public double SliderMax => R.Max ?? 100;
    public double SliderStep => R.Step ?? (R.Type == SettingTypes.Int ? Math.Max(1, Math.Round((SliderMax - SliderMin) / 100)) : (SliderMax - SliderMin) / 100);
    public bool SliderSnaps => R.Type == SettingTypes.Int || R.Step != null;

    /// The value on disk, or what the file will get if the key is missing.
    public string SavedValue => _fileValue ?? R.Default ?? "";
    public string? FileValue => _fileValue;

    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (_value == value) return;
            _value = value;
            RaiseValueChanged();
            if (!_suppress) _owner.OnEdited(this);
        }
    }

    private void RaiseValueChanged() =>
        Raise(nameof(Value), nameof(IsChecked), nameof(NumberValue), nameof(KeyDescription), nameof(Error), nameof(Warning),
              nameof(HasError), nameof(HasWarning), nameof(IsDirty), nameof(CanReset), nameof(StateLabel), nameof(IsCustomValue),
              nameof(SelectedOption), nameof(BoolCaption));

    /// Updates both the stored and the shown value without counting it as
    /// an edit, for a file that changed on disk.
    public void AcceptFileValue(string? fileValue, bool keepEdit)
    {
        _fileValue = fileValue;
        if (!keepEdit)
        {
            _suppress = true;
            try { _value = fileValue ?? R.Default ?? ""; }
            finally { _suppress = false; }
        }
        RaiseValueChanged();
        Raise(nameof(InFile), nameof(SavedValue), nameof(FileValue));
    }

    /// Restores an unsaved edit after the settings page was rebuilt.
    public void RestoreEdit(string value)
    {
        _suppress = true;
        try { Value = value; }
        finally { _suppress = false; }
    }

    public bool IsDirty => _value != SavedValue;
    public bool CanReset => R.Default != null && !string.Equals(_value, R.Default, StringComparison.Ordinal);

    // ------------------------------------------------------------ bool

    public bool IsChecked
    {
        get => string.Equals(_value.Trim(), R.TrueValue, StringComparison.OrdinalIgnoreCase)
               || (!string.Equals(_value.Trim(), R.FalseValue, StringComparison.OrdinalIgnoreCase) && _value.Trim() is not ("" or "0"));
        set => Value = value ? R.TrueValue : R.FalseValue;
    }

    /// For a checkbox built from two described choices, the description of
    /// the current one.
    public string? StateLabel
    {
        get
        {
            if (R.Type != SettingTypes.Bool || R.Options.Count == 0) return null;
            var opt = R.Options.FirstOrDefault(o => string.Equals(o.Value, _value.Trim(), StringComparison.OrdinalIgnoreCase));
            return opt?.Label;
        }
    }

    public string BoolCaption => StateLabel ?? (IsChecked ? Loc.T("On") : Loc.T("Off"));

    // ------------------------------------------------------------ enum

    public bool IsCustomValue => R.Type == SettingTypes.Enum && _value.Length > 0 &&
                                 !R.Options.Any(o => string.Equals(o.Value, _value, StringComparison.OrdinalIgnoreCase));

    public OptionMeta? SelectedOption
    {
        get => R.Options.FirstOrDefault(o => string.Equals(o.Value, _value, StringComparison.OrdinalIgnoreCase));
        set { if (value != null) Value = value.Value; }
    }

    // ------------------------------------------------------------ number

    public double NumberValue
    {
        get => ResolvedSetting.TryNumber(_value, R.Type == SettingTypes.Int, out var d) ? d : SliderMin;
        set
        {
            if (R.Type == SettingTypes.Int)
            {
                Value = Math.Round(value).ToString(CultureInfo.InvariantCulture);
                return;
            }
            var decimals = Math.Clamp(Math.Max(Decimals(R.Step), Math.Max(Decimals(SavedValue), 1)), 1, 4);
            Value = Math.Round(value, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }

    private static int Decimals(double? d) => d is { } v ? Decimals(v.ToString(CultureInfo.InvariantCulture)) : 0;
    private static int Decimals(string s)
    {
        var dot = s.IndexOf('.');
        return dot < 0 ? 0 : s.Length - dot - 1;
    }

    // ------------------------------------------------------------ key

    public string KeyFormat => R.KeyFormat;
    public string KeyDescription => KeyNames.Describe(_value, R.KeyFormat);

    // ------------------------------------------------------------ validation

    public string? Error => R.Validate(_value) ?? (IsDirty ? _owner.EncodingProblem(_value) : null);
    public string? Warning => Error == null ? R.Warn(_value) : null;
    public bool HasError => Error != null;
    public bool HasWarning => Warning != null;

    // ------------------------------------------------------------ help

    public string? Help => R.Help;
    public bool HasHelp => !string.IsNullOrWhiteSpace(R.Help);

    /// The first paragraph, shortened, shown under the label.
    public string? HelpShort
    {
        get
        {
            var h = R.Tooltip ?? R.Help;
            if (string.IsNullOrWhiteSpace(h)) return null;
            var para = h.Split("\n\n")[0].Split('\n')[0].Trim();
            return para.Length > 180 ? para[..177].TrimEnd() + "..." : para;
        }
    }

    public bool? EffectiveLive => R.Live ?? _owner.ModLive;

    public string? LiveBadge => _owner.GameRunning && EffectiveLive == false ? Loc.T("Next launch") : null;

    public void RefreshGameState() => Raise(nameof(LiveBadge), nameof(Facts));

    public void RefreshText() => Raise(string.Empty);

    /// Default, range, choices and where the information came from.
    public string Facts
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(R.Section).Append("] ").Append(R.Key);
            if (!InFile) sb.Append("   ").Append(Loc.T("(not in the file yet)"));
            sb.AppendLine();
            sb.AppendLine(R.TypeGuessed ? Loc.T("Type: {0}, guessed from the value and comments", TypeName) : Loc.T("Type: {0}", TypeName));
            if (R.Default != null) sb.AppendLine(Loc.T("Default: {0}", R.Default.Length == 0 ? Loc.T("(empty)") : R.Default));
            if (R.Min != null || R.Max != null)
            {
                var lo = R.Min is { } a ? ResolvedSetting.Fmt(a) : Loc.T("any");
                var hi = R.Max is { } b ? ResolvedSetting.Fmt(b) : Loc.T("any");
                sb.AppendLine(R.RangeGuessed ? Loc.T("Range: {0} to {1}, read from the comments and not enforced", lo, hi) : Loc.T("Range: {0} to {1}", lo, hi));
            }
            if (R.Type == SettingTypes.Bool) sb.AppendLine(Loc.T("Writes {0} for on and {1} for off.", R.TrueValue, R.FalseValue));
            if (R.Type == SettingTypes.Key)
                sb.AppendLine(R.KeyFormat switch
                {
                    "vk" => Loc.T("Stored as a virtual-key code."),
                    "hex" => Loc.T("Stored as a hex virtual-key code."),
                    _ => Loc.T("Stored as a key name. Ctrl, Shift and Alt combine with +."),
                });
            if (R.Options.Count > 0 && R.Type == SettingTypes.Enum && AllowCustom) sb.AppendLine(Loc.T("Other values are allowed too."));
            switch (EffectiveLive)
            {
                case true: sb.AppendLine(Loc.T("The plugin rereads this while the game runs.")); break;
                case false: sb.AppendLine(Loc.T("Takes effect the next time the game starts.")); break;
            }
            sb.Append(Loc.T("Described by: {0}", SourceName(R.Source)));
            return sb.ToString();
        }
    }

    public string TypeName => R.Type switch
    {
        SettingTypes.Bool => Loc.T("on or off"),
        SettingTypes.Int => Loc.T("whole number"),
        SettingTypes.Float => Loc.T("number"),
        SettingTypes.Enum => Loc.T("choice"),
        SettingTypes.Key => Loc.T("key"),
        _ => Loc.T("text"),
    };

    public static string SourceName(MetaSource s) => s switch
    {
        MetaSource.Embedded => Loc.T("the plugin's embedded metadata"),
        MetaSource.Sidecar => Loc.T("an .inimeta file"),
        MetaSource.IniDirectives => Loc.T("@ directives in the ini"),
        MetaSource.IniComments => Loc.T("the ini's comments"),
        _ => Loc.T("nothing, the type is a guess"),
    };

    public string ToolTipText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(Label);
            if (!string.IsNullOrWhiteSpace(R.Tooltip)) sb.AppendLine().AppendLine(R.Tooltip);
            else if (HasHelp) sb.AppendLine().AppendLine(R.Help);
            sb.AppendLine().Append(Facts);
            return sb.ToString();
        }
    }

    public bool Matches(string filter)
    {
        if (filter.Length == 0) return true;
        return Contains(R.Key) || Contains(R.Label) || Contains(R.Help) || Contains(R.Section) || Contains(R.Group);
        bool Contains(string? s) => s != null && s.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
