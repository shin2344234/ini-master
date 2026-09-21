using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IniMaster.Core;

/// Writes a .inimeta JSON template from what the tool currently knows about
/// an ini, as a starting point for a mod maker.
public static class MetaExporter
{
    public static string ToJson(IniView view, string iniFileName)
    {
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, options))
        {
            var m = view.Meta;
            w.WriteStartObject();
            w.WriteNumber("inimeta", 1);
            w.WriteString("name", m.Name ?? Path.GetFileNameWithoutExtension(iniFileName));
            w.WriteString("version", m.Version ?? "1.0.0");
            w.WriteString("author", m.Author ?? "");
            w.WriteString("url", m.Url ?? "");
            w.WriteString("ini", m.Ini ?? iniFileName);
            if (m.Live is { } live) w.WriteBoolean("live", live);
            else w.WriteBoolean("live", false);
            if (m.Description != null) w.WriteString("description", m.Description);

            w.WriteStartObject("sections");
            foreach (var s in view.Sections)
            {
                w.WriteStartObject(s.Name);
                if (s.Title != s.Name) w.WriteString("label", s.Title);
                if (s.Description != null) w.WriteString("description", s.Description);
                w.WriteStartObject("keys");
                string? group = null;
                foreach (var item in s.Items)
                {
                    if (item is ViewGroup g) { group = g.Title; continue; }
                    if (item is not ViewSetting vs) continue;
                    var r = vs.Setting;
                    w.WriteStartObject(r.Key);
                    w.WriteString("label", r.Label);
                    w.WriteString("type", r.Type);
                    if ((r.Group ?? group) is { } grp) w.WriteString("group", grp);
                    if (r.Help != null) w.WriteString("help", r.Help);
                    w.WriteString("default", r.Default ?? vs.FileValue ?? "");
                    if (r.Min is { } min) w.WriteNumber("min", min);
                    if (r.Max is { } max) w.WriteNumber("max", max);
                    if (r.Step is { } step) w.WriteNumber("step", step);
                    if (r.Unit != null) w.WriteString("unit", r.Unit);
                    if (r.Type == SettingTypes.Bool) { w.WriteString("true", r.TrueValue); w.WriteString("false", r.FalseValue); }
                    if (r.Type == SettingTypes.Key) w.WriteString("format", r.KeyFormat);
                    if (r.Options.Count > 0 && r.Type is SettingTypes.Enum or SettingTypes.Bool)
                    {
                        w.WriteStartArray("options");
                        foreach (var o in r.Options)
                        {
                            w.WriteStartObject();
                            w.WriteString("value", o.Value);
                            if (!string.IsNullOrWhiteSpace(o.Label)) w.WriteString("label", o.Label);
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                        if (r.AllowCustom) w.WriteBoolean("custom", true);
                    }
                    if (r.Live is { } kl) w.WriteBoolean("live", kl);
                    if (r.Advanced) w.WriteBoolean("advanced", true);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }
}
