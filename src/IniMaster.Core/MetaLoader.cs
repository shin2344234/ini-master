using System.Globalization;
using System.Text.Json;

namespace IniMaster.Core;

/// Loads an INI Master metadata document. Two forms are accepted, told apart
/// by the first character:
///   '{'       JSON, see docs/METADATA.md
///   anything  an annotated default ini: comments become help, ";@" lines are
///             directives, and every value becomes the key's default.
public static class MetaLoader
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ModMeta Load(string text, MetaSource source, string description)
    {
        text = text.TrimStart('﻿');
        var meta = LooksLikeJson(text) ? LoadJson(text) : LoadAnnotatedIni(text);
        meta.SourceDescription = description;
        Stamp(meta, source);
        return meta;
    }

    /// JSON if the first thing after any // or /* */ comments is '{'. An ini
    /// never starts that way, since its comments begin with ; or #.
    public static bool LooksLikeJson(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text.AsSpan(i).StartsWith("//"))
            {
                var nl = text.IndexOf('\n', i);
                if (nl < 0) return false;
                i = nl + 1;
                continue;
            }
            if (text.AsSpan(i).StartsWith("/*"))
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 2;
                continue;
            }
            return text[i] == '{';
        }
        return false;
    }

    private static void Stamp(ModMeta meta, MetaSource source)
    {
        meta.Source = source;
        foreach (var s in meta.Sections.Values)
            foreach (var k in s.Keys.Values) k.Source = source;
    }

    public static ModMeta LoadAnnotatedIni(string text)
    {
        var doc = IniDocument.Parse(text);
        var analysis = CommentAnalyzer.Analyze(doc);
        var meta = analysis.Merged(MetaSource.IniComments, MetaSource.IniDirectives);
        foreach (var line in doc.Lines)
        {
            if (line.Kind != IniLineKind.Key) continue;
            var k = meta.GetOrAddSection(line.Section).GetOrAddKey(line.Key);
            k.Default ??= line.Value;
        }
        // Keep the file's own section order, including sections with no keys.
        foreach (var name in doc.SectionNames()) meta.GetOrAddSection(name);
        meta.DefaultIniText = text;
        return meta;
    }

    public static ModMeta LoadJson(string text)
    {
        using var doc = JsonDocument.Parse(text, JsonOptions);
        var root = doc.RootElement;
        var meta = new ModMeta();
        ReadMod(root, meta);
        if (root.TryGetProperty("mod", out var mod) && mod.ValueKind == JsonValueKind.Object) ReadMod(mod, meta);

        if (root.TryGetProperty("sections", out var sections))
        {
            if (sections.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in sections.EnumerateObject()) ReadSection(meta.GetOrAddSection(p.Name), p.Value);
            }
            else if (sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sections.EnumerateArray())
                    if (Str(s, "name") is { } name) ReadSection(meta.GetOrAddSection(name), s);
            }
        }
        return meta;
    }

    private static void ReadMod(JsonElement e, ModMeta m)
    {
        if (e.ValueKind != JsonValueKind.Object) return;
        m.Name = Str(e, "name") ?? m.Name;
        m.Version = Str(e, "version") ?? m.Version;
        m.Author = Str(e, "author") ?? Str(e, "authors") ?? m.Author;
        m.Url = Str(e, "url") ?? Str(e, "homepage") ?? m.Url;
        m.Description = Str(e, "description") ?? m.Description;
        m.Game = Str(e, "game") ?? m.Game;
        m.Live = Bool(e, "live") ?? Bool(e, "hotReload") ?? m.Live;
        m.Ini = Str(e, "ini") ?? m.Ini;
    }

    private static void ReadSection(SectionMeta s, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return;
        s.Label = Str(e, "label") ?? Str(e, "title") ?? s.Label;
        s.Description = Str(e, "description") ?? Str(e, "help") ?? s.Description;
        s.Hidden = Bool(e, "hidden") ?? s.Hidden;
        s.Advanced = Bool(e, "advanced") ?? s.Advanced;
        s.Order = (int?)Num(e, "order") ?? s.Order;
        if (!e.TryGetProperty("keys", out var keys)) return;
        if (keys.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in keys.EnumerateObject()) ReadKey(s.GetOrAddKey(p.Name), p.Value);
        }
        else if (keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in keys.EnumerateArray())
                if ((Str(k, "name") ?? Str(k, "key")) is { } name) ReadKey(s.GetOrAddKey(name), k);
        }
    }

    private static void ReadKey(KeyMeta k, JsonElement e)
    {
        // A bare string is shorthand for the help text.
        if (e.ValueKind == JsonValueKind.String) { k.Help = e.GetString(); return; }
        if (e.ValueKind != JsonValueKind.Object) return;
        k.Label = Str(e, "label") ?? Str(e, "title") ?? k.Label;
        if (Str(e, "type") is { } type) k.Type = SettingTypes.Normalize(type);
        k.Help = Str(e, "help") ?? Str(e, "description") ?? k.Help;
        k.Tooltip = Str(e, "tooltip") ?? k.Tooltip;
        k.Unit = Str(e, "unit") ?? k.Unit;
        k.Group = Str(e, "group") ?? k.Group;
        k.Default = Str(e, "default") ?? k.Default;
        k.TrueValue = Str(e, "true") ?? Str(e, "trueValue") ?? k.TrueValue;
        k.FalseValue = Str(e, "false") ?? Str(e, "falseValue") ?? k.FalseValue;
        k.Format = Str(e, "format")?.ToLowerInvariant() ?? k.Format;
        k.Min = Num(e, "min") ?? k.Min;
        k.Max = Num(e, "max") ?? k.Max;
        k.Step = Num(e, "step") ?? k.Step;
        k.AllowCustom = Bool(e, "custom") ?? Bool(e, "allowCustom") ?? k.AllowCustom;
        k.Live = Bool(e, "live") ?? k.Live;
        if (Bool(e, "restart") is { } restart) k.Live = !restart;
        k.Advanced = Bool(e, "advanced") ?? k.Advanced;
        k.Hidden = Bool(e, "hidden") ?? k.Hidden;
        k.ReadOnly = Bool(e, "readonly") ?? Bool(e, "readOnly") ?? k.ReadOnly;
        k.Order = (int?)Num(e, "order") ?? k.Order;

        if (e.TryGetProperty("options", out var opts) || e.TryGetProperty("choices", out opts))
        {
            var list = new List<OptionMeta>();
            if (opts.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in opts.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.Object)
                    {
                        var v = Str(o, "value");
                        if (v != null) list.Add(new OptionMeta { Value = v, Label = Str(o, "label"), Help = Str(o, "help") });
                    }
                    else if (Scalar(o) is { } v) list.Add(new OptionMeta { Value = v });
                }
            }
            else if (opts.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in opts.EnumerateObject())
                    list.Add(new OptionMeta { Value = p.Name, Label = Scalar(p.Value) });
            }
            else if (opts.ValueKind == JsonValueKind.String)
            {
                list = CommentAnalyzer.ParseOptionList(opts.GetString() ?? "");
            }
            if (list.Count > 0) k.Options = list;
        }
    }

    private static string? Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }

    private static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !TryGet(e, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Array)
        {
            // Long help may be written as an array of lines.
            var parts = v.EnumerateArray().Select(Scalar).Where(x => x != null);
            return string.Join("\n", parts);
        }
        return Scalar(v);
    }

    private static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !TryGet(e, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static bool? Bool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !TryGet(e, name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetDouble() != 0,
            JsonValueKind.String => CommentAnalyzer.Flag(v.GetString() ?? "0"),
            _ => null,
        };
    }
}
