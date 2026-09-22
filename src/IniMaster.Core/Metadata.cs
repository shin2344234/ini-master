namespace IniMaster.Core;

/// Where a piece of metadata came from, lowest priority first. A higher layer
/// replaces only the fields it sets.
public enum MetaSource { Guessed = 0, IniComments = 1, IniDirectives = 2, Sidecar = 3, Embedded = 4 }

public static class SettingTypes
{
    public const string Bool = "bool";
    public const string Int = "int";
    public const string Float = "float";
    public const string String = "string";
    public const string Enum = "enum";
    public const string Key = "key";

    public static string Normalize(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "bool" or "boolean" or "toggle" or "flag" or "checkbox" => Bool,
        "int" or "integer" or "number" or "long" => Int,
        "float" or "double" or "decimal" or "real" => Float,
        "enum" or "choice" or "list" or "select" or "combo" => Enum,
        "key" or "hotkey" or "vk" or "keybind" => Key,
        "" => "",
        _ => String,
    };
}

public sealed class OptionMeta
{
    public string Value { get; set; } = "";
    public string? Label { get; set; }
    public string? Help { get; set; }
    public string Display => string.IsNullOrWhiteSpace(Label) || Label == Value ? Value
        : Value.Length == 0 ? Label! : $"{Label}  ({Value})";
    public override string ToString() => Display;
}

public sealed class KeyMeta
{
    public string? Label { get; set; }
    public string? Type { get; set; }
    public bool TypeGuessed { get; set; }
    public string? Help { get; set; }
    public string? Tooltip { get; set; }
    public string? Unit { get; set; }
    public string? Group { get; set; }
    public string? Default { get; set; }
    /// For bool: the text written for on and off. For key: "vk", "hex" or "name".
    public string? TrueValue { get; set; }
    public string? FalseValue { get; set; }
    public string? Format { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public bool RangeGuessed { get; set; }
    public double? Step { get; set; }
    public List<OptionMeta>? Options { get; set; }
    public bool? AllowCustom { get; set; }
    public bool? Live { get; set; }
    public bool? Advanced { get; set; }
    public bool? Hidden { get; set; }
    public bool? ReadOnly { get; set; }
    public int? Order { get; set; }
    /// Set from comment wording such as "1 turns the mod on", for a 0/1
    /// value that has no declared type.
    public bool? LooksBoolean { get; set; }
    public MetaSource Source { get; set; }

    public void OverlayWith(KeyMeta o)
    {
        LooksBoolean = o.LooksBoolean ?? LooksBoolean;
        Label = o.Label ?? Label;
        if (o.Type != null) { Type = o.Type; TypeGuessed = o.TypeGuessed; }
        Help = o.Help ?? Help;
        Tooltip = o.Tooltip ?? Tooltip;
        Unit = o.Unit ?? Unit;
        Group = o.Group ?? Group;
        Default = o.Default ?? Default;
        TrueValue = o.TrueValue ?? TrueValue;
        FalseValue = o.FalseValue ?? FalseValue;
        Format = o.Format ?? Format;
        if (o.Min != null || o.Max != null) { Min = o.Min; Max = o.Max; RangeGuessed = o.RangeGuessed; }
        Step = o.Step ?? Step;
        Options = o.Options ?? Options;
        AllowCustom = o.AllowCustom ?? AllowCustom;
        Live = o.Live ?? Live;
        Advanced = o.Advanced ?? Advanced;
        Hidden = o.Hidden ?? Hidden;
        ReadOnly = o.ReadOnly ?? ReadOnly;
        Order = o.Order ?? Order;
        if (o.Source > Source) Source = o.Source;
    }

    public KeyMeta Clone() { var k = new KeyMeta(); k.OverlayWith(this); k.Source = Source; return k; }
}

public sealed class SectionMeta
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool? Hidden { get; set; }
    public bool? Advanced { get; set; }
    public int? Order { get; set; }
    /// Keys in the order the metadata lists them.
    public List<string> KeyOrder { get; } = new();
    public Dictionary<string, KeyMeta> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public KeyMeta GetOrAddKey(string key)
    {
        if (!Keys.TryGetValue(key, out var k))
        {
            k = new KeyMeta();
            Keys[key] = k;
            KeyOrder.Add(key);
        }
        return k;
    }

    public void OverlayWith(SectionMeta o)
    {
        Label = o.Label ?? Label;
        Description = o.Description ?? Description;
        Hidden = o.Hidden ?? Hidden;
        Advanced = o.Advanced ?? Advanced;
        Order = o.Order ?? Order;
        foreach (var name in o.KeyOrder) GetOrAddKey(name).OverlayWith(o.Keys[name]);
    }
}

/// Everything known about one ini file: mod identity plus per-key metadata.
public sealed class ModMeta
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Url { get; set; }
    public string? Description { get; set; }
    public string? Game { get; set; }
    /// True if the plugin rereads its ini while the game runs.
    public bool? Live { get; set; }
    /// The ini this metadata describes, as a file name. Null means the one
    /// with the plugin's own base name.
    public string? Ini { get; set; }
    /// Whether to read the ini's own comments as help. Null means the default:
    /// yes, unless the plugin carries embedded metadata, which then stands on
    /// its own. A mod sets it with "comments" in its metadata.
    public bool? UseComments { get; set; }
    /// Raw text of an annotated default ini, when the metadata came as one.
    /// Used to create a missing ini with every comment intact.
    public string? DefaultIniText { get; set; }
    /// Group headings and notes the metadata carries itself, per section, when
    /// it came as an annotated default ini. The page falls back on these when
    /// the file's own comments are left out, since nothing else can express
    /// them.
    public Dictionary<string, List<LayoutItem>> Layout { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SectionOrder { get; } = new();
    public Dictionary<string, SectionMeta> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
    public MetaSource Source { get; set; }
    public string? SourceDescription { get; set; }

    public SectionMeta GetOrAddSection(string name)
    {
        if (!Sections.TryGetValue(name, out var s))
        {
            s = new SectionMeta();
            Sections[name] = s;
            SectionOrder.Add(name);
        }
        return s;
    }

    public KeyMeta? FindKey(string section, string key) =>
        Sections.TryGetValue(section, out var s) && s.Keys.TryGetValue(key, out var k) ? k : null;

    public void OverlayWith(ModMeta o)
    {
        Name = o.Name ?? Name;
        Version = o.Version ?? Version;
        Author = o.Author ?? Author;
        Url = o.Url ?? Url;
        Description = o.Description ?? Description;
        Game = o.Game ?? Game;
        Live = o.Live ?? Live;
        Ini = o.Ini ?? Ini;
        UseComments = o.UseComments ?? UseComments;
        DefaultIniText = o.DefaultIniText ?? DefaultIniText;
        foreach (var (name, items) in o.Layout) Layout[name] = items;
        foreach (var name in o.SectionOrder) GetOrAddSection(name).OverlayWith(o.Sections[name]);
        if (o.Source > Source) Source = o.Source;
    }
}
