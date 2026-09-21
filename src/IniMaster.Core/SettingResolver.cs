using System.Globalization;
using System.Text.RegularExpressions;

namespace IniMaster.Core;

/// The final answer for one key: its control type and everything the UI
/// shows about it, from the merged metadata plus the value in the file.
public sealed class ResolvedSetting
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Type { get; init; }
    public bool TypeGuessed { get; init; }
    public string TrueValue { get; init; } = "1";
    public string FalseValue { get; init; } = "0";
    /// For key: "vk" (decimal virtual-key code), "hex" (0x2D) or "name" (Ctrl+F1).
    public string KeyFormat { get; init; } = "name";
    public double? Min { get; init; }
    public double? Max { get; init; }
    public bool RangeGuessed { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }
    public List<OptionMeta> Options { get; init; } = new();
    public bool AllowCustom { get; init; }
    public string? Help { get; init; }
    public string? Tooltip { get; init; }
    public string? Default { get; init; }
    public string? Group { get; init; }
    public bool? Live { get; init; }
    public bool Advanced { get; init; }
    public bool Hidden { get; init; }
    public bool ReadOnly { get; init; }
    public int? Order { get; init; }
    public MetaSource Source { get; init; }

    public bool IsNumeric => Type is SettingTypes.Int or SettingTypes.Float;

    /// Null when the value is acceptable, otherwise why not. Guessed ranges
    /// only warn, so they never land here.
    public string? Validate(string value)
    {
        switch (Type)
        {
            case SettingTypes.Int:
            case SettingTypes.Float:
            {
                if (value.Trim().Length == 0) return Loc.T("Needs a number.");
                if (!TryNumber(value, Type == SettingTypes.Int, out var d))
                    return Type == SettingTypes.Int ? Loc.T("Needs a whole number.") : Loc.T("Needs a number.");
                if (!RangeGuessed && Min is { } lo && d < lo) return Loc.T("The lowest allowed is {0}.", Fmt(lo));
                if (!RangeGuessed && Max is { } hi && d > hi) return Loc.T("The highest allowed is {0}.", Fmt(hi));
                return null;
            }
            case SettingTypes.Enum:
                if (!AllowCustom && Options.Count > 0 && !Options.Any(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
                    return Loc.T("Not one of the listed choices.");
                return null;
            default:
                return null;
        }
    }

    /// A soft warning for a number outside a range read from prose.
    public string? Warn(string value)
    {
        if (!RangeGuessed || !TryNumber(value, false, out var d)) return null;
        if (Min is { } lo && d < lo || Max is { } hi && d > hi)
            return Loc.T("The comments mention {0} to {1}.", Fmt(Min ?? 0), Fmt(Max ?? 0));
        return null;
    }

    public static bool TryNumber(string value, bool integer, out double d)
    {
        var v = value.Trim();
        if (integer && v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) { d = h; return true; }
        if (integer)
        {
            var ok = long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l);
            d = l;
            return ok;
        }
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    public static string Fmt(double d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
}

public static partial class SettingResolver
{
    private static readonly (string T, string F)[] BoolWords =
    {
        ("1", "0"), ("true", "false"), ("on", "off"), ("yes", "no"), ("enabled", "disabled"),
    };

    public static ResolvedSetting Resolve(string section, string key, string? fileValue, KeyMeta m)
    {
        var value = fileValue ?? m.Default ?? "";
        var options = m.Options?.ToList() ?? new List<OptionMeta>();
        var type = m.Type;
        var guessed = false;
        string? tv = m.TrueValue, fv = m.FalseValue;
        var allowCustom = m.AllowCustom ?? false;
        var keyFormat = m.Format is "vk" or "hex" or "name" ? m.Format : null;

        if (string.IsNullOrEmpty(type) || m.TypeGuessed)
        {
            guessed = true;
            type = Guess(key, value, m, options, ref tv, ref fv, ref keyFormat);
        }

        if (type == SettingTypes.Bool)
        {
            if (tv == null || fv == null)
            {
                var pair = PairFor(value) ?? PairFor(m.Default) ?? PairFromOptions(options) ?? ("1", "0");
                tv ??= MatchCase(pair.T, value);
                fv ??= MatchCase(pair.F, value);
            }
        }

        if (type == SettingTypes.Enum && options.Count > 0 && value.Length > 0 &&
            !options.Any(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
            allowCustom = true;

        if (type == SettingTypes.Key && keyFormat == null)
            keyFormat = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? "hex"
                : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? "vk" : "name";

        return new ResolvedSetting
        {
            Section = section,
            Key = key,
            Label = string.IsNullOrWhiteSpace(m.Label) ? Humanize(key) : m.Label!,
            Type = type!,
            TypeGuessed = guessed,
            TrueValue = tv ?? "1",
            FalseValue = fv ?? "0",
            KeyFormat = keyFormat ?? "name",
            Min = m.Min,
            Max = m.Max,
            RangeGuessed = m.RangeGuessed,
            Step = m.Step,
            Unit = m.Unit,
            Options = options,
            AllowCustom = allowCustom,
            Help = m.Help,
            Tooltip = m.Tooltip,
            Default = m.Default,
            Group = string.IsNullOrWhiteSpace(m.Group) ? null : m.Group.Trim(),
            Live = m.Live,
            Advanced = m.Advanced ?? false,
            Hidden = m.Hidden ?? false,
            ReadOnly = m.ReadOnly ?? false,
            Order = m.Order,
            Source = m.Source,
        };
    }

    private static string Guess(string key, string value, KeyMeta m, List<OptionMeta> options,
        ref string? tv, ref string? fv, ref string? keyFormat)
    {
        if (options.Count > 0)
        {
            // Two choices become a checkbox only when their labels are short,
            // like "off"/"on" or "skip"/"loot". Longer labels read better in
            // a list than as the caption of a checkbox.
            if (options.Count == 2 && PairFromOptions(options) is { } p &&
                options.All(o => (o.Label ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2))
            {
                tv ??= options.First(o => o.Value.Equals(p.T, StringComparison.OrdinalIgnoreCase)).Value;
                fv ??= options.First(o => o.Value.Equals(p.F, StringComparison.OrdinalIgnoreCase)).Value;
                return SettingTypes.Bool;
            }
            return SettingTypes.Enum;
        }

        var probe = value.Length > 0 ? value : m.Default ?? "";
        var lower = probe.Trim().ToLowerInvariant();
        if (lower is "true" or "false" or "on" or "off" or "yes" or "no") return SettingTypes.Bool;

        var hasRealRange = (m.Min != null || m.Max != null) && !m.RangeGuessed;
        if ((lower is "0" or "1") && !hasRealRange && !NumericName().IsMatch(key) && (m.LooksBoolean == true || BoolishName().IsMatch(key)))
            return SettingTypes.Bool;

        if (KeyishName().IsMatch(key) && !key.StartsWith("Keep", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(lower, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vk) && vk is > 0 and < 256) { keyFormat ??= "vk"; return SettingTypes.Key; }
            if (lower.StartsWith("0x") && KeyNames.TryParseVk(probe, out _)) { keyFormat ??= "hex"; return SettingTypes.Key; }
            if (KeyNames.LooksLikeKeyName(probe)) { keyFormat ??= "name"; return SettingTypes.Key; }
        }

        if (hasRealRange || m.Step != null)
            return (m.Step is { } s && s % 1 != 0) || probe.Contains('.') ? SettingTypes.Float : SettingTypes.Int;
        if (long.TryParse(probe, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return SettingTypes.Int;
        if (FloatText().IsMatch(probe)) return SettingTypes.Float;
        return SettingTypes.String;
    }

    private static (string T, string F)? PairFor(string? v)
    {
        if (v == null) return null;
        foreach (var p in BoolWords)
            if (v.Equals(p.T, StringComparison.OrdinalIgnoreCase) || v.Equals(p.F, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private static (string T, string F)? PairFromOptions(List<OptionMeta> options)
    {
        if (options.Count != 2) return null;
        foreach (var p in BoolWords)
        {
            var hasT = options.Any(o => o.Value.Equals(p.T, StringComparison.OrdinalIgnoreCase));
            var hasF = options.Any(o => o.Value.Equals(p.F, StringComparison.OrdinalIgnoreCase));
            if (hasT && hasF) return p;
        }
        return null;
    }

    // Keeps "True"/"TRUE" styling when the file uses it.
    private static string MatchCase(string word, string sample)
    {
        if (sample.Length == 0 || !char.IsLetter(sample[0])) return word;
        if (sample.All(c => !char.IsLetter(c) || char.IsUpper(c))) return word.ToUpperInvariant();
        if (char.IsUpper(sample[0])) return char.ToUpperInvariant(word[0]) + word[1..];
        return word;
    }

    /// "MountRegenPercent" becomes "Mount regen percent".
    public static string Humanize(string key)
    {
        if (key.Length == 0) return key;
        // Data keys (item names, paths, all-lowercase ids) read best as written.
        if (key.IndexOfAny(new[] { '/', '\\', '.', '-', ' ' }) >= 0 || !key.Any(char.IsUpper)) return key;
        var spaced = SplitWords().Replace(key.Replace('_', ' '), " ").Trim();
        spaced = Regex.Replace(spaced, @"\s+", " ");
        var words = spaced.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            // Leave acronyms (HUD, DX12, FOV) alone.
            if (w.Length > 1 && w.Skip(1).Any(char.IsUpper)) continue;
            words[i] = i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w.ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    // Letters in any script, so ВидимостьМеню splits like MenuVisibility.
    [GeneratedRegex(@"(?<=[\p{Ll}\p{Nd}])(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})")]
    private static partial Regex SplitWords();

    [GeneratedRegex(@"^(Enable|Enabled|Disable|Disabled|Show|Hide|Hook|Notify|Debug|Log|Use|Allow|Skip|Is|Has|Keep|Auto|Stop|Draw|Break|Take|Pick|Gather|Loot|Catch|Search|Wrap|No|Force|Toggle|Lock|Unlock|Always|Never|Can|Should|Ignore|Block|Remember|Probe|Rumble|Mute|Pause|Invert)([A-Z0-9_]|$)|(Enabled|Disabled|Enable|Disable|Log|Logging|Debug|Hud|Overlay)$", RegexOptions.None)]
    private static partial Regex BoolishName();

    [GeneratedRegex(@"(Percent|Pct|Count|Ms|Sec|Secs|Seconds|Time|Size|Range|Radius|Distance|Speed|Scale|Multiplier|Mult|Amount|Level|Limit|Max|Min|Bonus|Delay|Rate|Lines|Mode|Index|Id|Width|Height|Offset|Steps?|Version)$")]
    private static partial Regex NumericName();

    [GeneratedRegex(@"(^Key([A-Z_]|$)|Key$|Hotkey|HotKey|Keybind|KeyBind|KeyCode|Vk$|^Vk[A-Z])")]
    private static partial Regex KeyishName();

    [GeneratedRegex(@"^-?\d*\.\d+([eE][-+]?\d+)?f?$|^-?\d+\.\d*$")]
    private static partial Regex FloatText();
}
