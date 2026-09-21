using System.Text;
using IniMaster.Core;

namespace IniMaster.Tests;

/// Writes what the tool infers for every ini in a real game folder, for a
/// person to read. Runs only when INIMASTER_DUMP names the output file.
public class RealFolderDump
{
    [Fact]
    public void Dump()
    {
        var output = Environment.GetEnvironmentVariable("INIMASTER_DUMP");
        if (string.IsNullOrEmpty(output)) return;
        var root = Environment.GetEnvironmentVariable("INIMASTER_GAME") ?? GameLocator.Find();
        Assert.NotNull(root);
        var sb = new StringBuilder();
        foreach (var mod in ModScanner.Scan(root!, null))
        {
            sb.AppendLine($"################ {mod.DisplayName}  asi={mod.AsiPath != null}");
            foreach (var f in mod.Files)
            {
                sb.AppendLine($"== {f.IniPath} exists={f.Exists} sources=[{string.Join(", ", f.MetaSources)}] errors=[{string.Join(", ", f.MetaErrors)}]");
                var bytes = IniStore.ReadBytes(f.IniPath);
                var doc = bytes == null ? null : IniDocument.Load(bytes);
                if (doc != null) Assert.Equal(bytes, doc.ToBytes());
                var view = IniView.Build(f, doc);
                sb.AppendLine($"   name={view.Meta.Name} live={view.Meta.Live}");
                sb.AppendLine($"   desc={Short(view.Meta.Description)}");
                foreach (var s in view.Sections)
                {
                    sb.AppendLine($"  [{s.Name}] title={s.Title} desc={Short(s.Description)}");
                    foreach (var item in s.Items.Take(40))
                    {
                        switch (item)
                        {
                            case ViewNote n: sb.AppendLine($"    NOTE {Short(n.Text)}"); break;
                            case ViewGroup g: sb.AppendLine($"    GROUP {g.Title}"); break;
                            case ViewSetting v:
                                var r = v.Setting;
                                sb.AppendLine($"    {r.Key} = {v.FileValue}  -> {r.Type}{(r.TypeGuessed ? "?" : "")} label='{r.Label}'" +
                                              (r.Type == SettingTypes.Bool ? $" [{r.TrueValue}/{r.FalseValue}]" : "") +
                                              (r.Options.Count > 0 ? $" opts=[{string.Join(" | ", r.Options.Select(o => o.Value + ":" + o.Label))}]{(r.AllowCustom ? "+custom" : "")}" : "") +
                                              (r.Min != null || r.Max != null ? $" range={r.Min}..{r.Max}{(r.RangeGuessed ? "?" : "")}" : "") +
                                              (r.Type == SettingTypes.Key ? $" fmt={r.KeyFormat} ({KeyNames.Describe(v.FileValue ?? "", r.KeyFormat)})" : "") +
                                              (r.Live != null ? $" live={r.Live}" : "") +
                                              $" help={Short(r.Help)}");
                                break;
                        }
                    }
                }
            }
        }
        File.WriteAllText(output, sb.ToString());
    }

    private static string Short(string? s) => s == null ? "-" : (s.Length > 90 ? s[..90] + "..." : s).Replace("\n", " / ");
}
