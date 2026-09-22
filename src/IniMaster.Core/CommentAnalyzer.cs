using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IniMaster.Core;

public enum LayoutKind { Note, Group, Setting }

public sealed record LayoutItem(LayoutKind Kind, string Text);

/// What an ini file says about itself through its comments.
public sealed class IniAnalysis
{
    /// Help text, choices and ranges read from plain comments.
    public ModMeta Comments { get; } = new() { Source = MetaSource.IniComments };
    /// Explicit ";@" directives.
    public ModMeta Directives { get; } = new() { Source = MetaSource.IniDirectives };
    public List<string> SectionOrder { get; } = new();
    /// Notes, group headings and keys per section, in file order.
    public Dictionary<string, List<LayoutItem>> Layout { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<LayoutItem> LayoutFor(string section)
    {
        if (!Layout.TryGetValue(section, out var list))
        {
            list = new List<LayoutItem>();
            Layout[section] = list;
            SectionOrder.Add(section);
        }
        return list;
    }

    /// The ";@" directives on their own, for a file whose plain comments are
    /// ignored because the plugin ships its own metadata.
    public ModMeta OnlyDirectives()
    {
        Stamp(Directives, MetaSource.IniDirectives);
        return Directives;
    }

    public ModMeta Merged(MetaSource commentsAs, MetaSource directivesAs)
    {
        var m = new ModMeta();
        Stamp(Comments, commentsAs);
        Stamp(Directives, directivesAs);
        m.OverlayWith(Comments);
        m.OverlayWith(Directives);
        return m;
    }

    private static void Stamp(ModMeta meta, MetaSource source)
    {
        meta.Source = source;
        foreach (var s in meta.Sections.Values)
            foreach (var k in s.Keys.Values) k.Source = source;
    }
}

/// Reads the conventions mod makers already use in shipped ini files.
public static partial class CommentAnalyzer
{
    public static IniAnalysis Analyze(IniDocument doc)
    {
        var a = new IniAnalysis();
        var block = new List<string>();
        var section = "";
        var sawSection = false;
        var sawKey = false;
        var prevWasKey = false;
        string? prevHelp = null;

        void FlushFloating()
        {
            if (block.Count == 0) return;
            var text = new List<string>();
            foreach (var body in block)
            {
                if (IsDirective(body))
                {
                    ApplyFloatingDirective(a, section, body);
                    continue;
                }
                if (TryDivider(body, out var title))
                {
                    FlushNote(text);
                    if (title != null && sawSection) a.LayoutFor(section).Add(new LayoutItem(LayoutKind.Group, title));
                    continue;
                }
                text.Add(body);
            }
            FlushNote(text);
            block.Clear();
        }

        void FlushNote(List<string> text)
        {
            var note = FormatHelp(text);
            text.Clear();
            if (note.Length == 0) return;
            if (!sawSection && !sawKey)
            {
                a.Comments.Description = a.Comments.Description == null ? note : a.Comments.Description + "\n\n" + note;
                ScanModLiveHints(a.Comments, note);
            }
            else
            {
                a.LayoutFor(section).Add(new LayoutItem(LayoutKind.Note, note));
                ScanModLiveHints(a.Comments, note);
            }
        }

        foreach (var line in doc.Lines)
        {
            switch (line.Kind)
            {
                case IniLineKind.Comment:
                    block.Add(line.CommentBody);
                    break;

                case IniLineKind.Blank:
                    FlushFloating();
                    prevWasKey = false;
                    break;

                case IniLineKind.Section:
                {
                    var name = line.Section;
                    var attached = new List<string>();
                    foreach (var body in block)
                    {
                        if (IsDirective(body)) ApplySectionDirective(a.Directives.GetOrAddSection(name), a.Directives, body);
                        else if (!TryDivider(body, out _)) attached.Add(body);
                    }
                    var desc = FormatHelp(attached);
                    if (desc.Length > 0)
                    {
                        // Above the first header with nothing before it, a
                        // comment reads as the file's own introduction.
                        if (!sawSection && !sawKey && a.Comments.Description == null)
                        {
                            a.Comments.Description = desc;
                            ScanModLiveHints(a.Comments, desc);
                        }
                        else a.Comments.GetOrAddSection(name).Description = desc;
                    }
                    block.Clear();
                    section = name;
                    sawSection = true;
                    a.LayoutFor(section);
                    prevWasKey = false;
                    prevHelp = null;
                    break;
                }

                case IniLineKind.Key:
                {
                    var layout = a.LayoutFor(section);
                    var km = a.Comments.GetOrAddSection(section).GetOrAddKey(line.Key);
                    var attached = new List<string>();
                    KeyMeta? dk = null;
                    foreach (var body in block)
                    {
                        if (IsDirective(body))
                        {
                            dk ??= a.Directives.GetOrAddSection(section).GetOrAddKey(line.Key);
                            ApplyKeyDirective(dk, a.Directives, body);
                        }
                        else if (TryDivider(body, out var title))
                        {
                            if (title != null) layout.Add(new LayoutItem(LayoutKind.Group, title));
                        }
                        else attached.Add(body);
                    }
                    block.Clear();

                    var help = FormatHelp(attached);
                    var inline = line.InlineCommentBody;
                    if (inline.Length > 0) help = help.Length == 0 ? inline : help + "\n\n" + inline;
                    if (help.Length == 0 && prevWasKey && prevHelp != null) help = prevHelp;
                    if (help.Length > 0) km.Help = help;

                    ReadChoices(attached, km);
                    if (inline.Length > 0) ReadChoices(new List<string> { inline }, km);
                    if (km.Options == null) ReadRange(help, line.Value, km);
                    if (LooksBooleanText(help)) km.LooksBoolean = true;
                    if (RestartHint().IsMatch(help)) km.Live = false;

                    layout.Add(new LayoutItem(LayoutKind.Setting, line.Key));
                    prevWasKey = true;
                    prevHelp = help.Length > 0 ? help : null;
                    sawKey = true;
                    break;
                }

                default:
                    FlushFloating();
                    prevWasKey = false;
                    break;
            }
        }
        FlushFloating();

        foreach (var (name, sm) in a.Comments.Sections)
        {
            if (sm.Description == null || ReadSectionChoices(sm.Description) is not { } choices) continue;
            foreach (var line in doc.Lines)
            {
                if (line.Kind != IniLineKind.Key || !string.Equals(line.Section, name, StringComparison.OrdinalIgnoreCase)) continue;
                var km = sm.GetOrAddKey(line.Key);
                if (km.Options == null && choices.Any(o => o.Value == line.Value)) { km.Options = choices; km.AllowCustom = true; }
            }
        }
        return a;
    }

    // ---------------------------------------------------------------- help text

    /// Joins wrapped comment lines into paragraphs. Indented lines, bullets
    /// and table rows keep their own line.
    public static string FormatHelp(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        var lineOpen = false;
        foreach (var raw in lines)
        {
            var body = raw.TrimEnd();
            if (body.Trim().Length == 0)
            {
                if (sb.Length > 0) { TrimEndSpaces(sb); sb.Append("\n\n"); }
                lineOpen = false;
                continue;
            }
            var own = body.StartsWith(' ') || body.StartsWith('\t') || Bullet().IsMatch(body);
            if (own)
            {
                if (lineOpen) { TrimEndSpaces(sb); sb.Append('\n'); }
                sb.Append(body.TrimEnd());
                sb.Append('\n');
                lineOpen = false;
            }
            else
            {
                if (lineOpen) sb.Append(' ');
                sb.Append(body.Trim());
                lineOpen = true;
            }
        }
        return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim('\n', ' ');
    }

    private static void TrimEndSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && (sb[^1] == ' ' || sb[^1] == '\n')) sb.Length--;
    }

    /// "; ------ marking" gives the title "marking". A rule with no words
    /// gives null. Anything else is not a divider.
    public static bool TryDivider(string body, out string? title)
    {
        title = null;
        var t = body.Trim();
        var m = DividerLeading().Match(t);
        if (!m.Success) m = DividerTrailing().Match(t);
        if (!m.Success) return false;
        var inner = m.Groups["t"].Value.Trim();
        if (inner.Length == 0) return true;
        if (!inner.Any(char.IsLetter)) return true;
        if (inner.Length > 60) return false;
        title = inner;
        return true;
    }

    // ---------------------------------------------------------------- choices

    /// Reads the three choice layouts seen in shipped files:
    ///   an indented table        "   0   off" / "   1   on"
    ///   inline pairs             "0 = ULTRA, 1 = HIGH" or "0: A | 1: B"
    ///   OptiScaler's list        "a, b, c - Default (auto) is b"
    public static void ReadChoices(IReadOnlyList<string> lines, KeyMeta km)
    {
        var options = new List<OptionMeta>();
        var custom = false;

        foreach (var body in lines)
        {
            var m = TableRow().Match(body);
            if (m.Success)
            {
                var v = m.Groups["v"].Value;
                if (v.StartsWith('<')) { custom = true; continue; }
                if (options.All(o => o.Value != v))
                    options.Add(new OptionMeta { Value = v, Label = m.Groups["l"].Value.Trim() });
            }
        }
        if (options.Count < 2 && !custom) options.Clear();

        if (options.Count == 0)
        {
            foreach (var body in lines)
            {
                var pairs = InlinePair().Matches(body);
                if (pairs.Count < 2) continue;
                foreach (Match p in pairs)
                {
                    var v = p.Groups["v"].Value;
                    if (options.All(o => o.Value != v))
                        options.Add(new OptionMeta { Value = v, Label = p.Groups["l"].Value.Trim() });
                }
                break;
            }
        }

        if (options.Count == 0)
        {
            foreach (var body in lines)
            {
                var m = DefaultList().Match(body.Trim());
                if (!m.Success) continue;
                var list = m.Groups["list"].Value;
                var auto = m.Groups["auto"].Success ? m.Groups["auto"].Value : null;
                if (auto != null) options.Add(new OptionMeta { Value = auto, Label = $"{auto} (default: {m.Groups["def"].Value.Trim().TrimEnd('.')})" });
                foreach (var part in SplitTopLevel(list))
                {
                    var item = part.Trim();
                    if (item.Length == 0) continue;
                    var paren = item.IndexOf('(');
                    var v = (paren > 0 ? item[..paren] : item).Trim();
                    if (v.Length == 0 || v.Contains(' ')) continue;
                    if (options.All(o => !o.Value.Equals(v, StringComparison.OrdinalIgnoreCase)))
                        options.Add(new OptionMeta { Value = v, Label = item });
                }
                custom = true;
                break;
            }
        }

        if (options.Count >= 2 || (custom && options.Count > 0))
        {
            km.Options = options;
            if (custom) km.AllowCustom = true;
        }
        else if (custom) km.AllowCustom = true;
    }

    /// Splits "a (x, y), b or c" on commas and "or" outside parentheses.
    private static IEnumerable<string> SplitTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            var ch = list[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && ch == ',')
            {
                yield return list[start..i];
                start = i + 1;
            }
            else if (depth == 0 && i + 4 <= list.Length && list.Substring(i, 4) == " or " )
            {
                yield return list[start..i];
                start = i + 4;
                i += 3;
            }
        }
        yield return list[start..];
    }

    /// "class -> 1 loot, 0 skip" above a section: choices for every key in it
    /// that has none of its own.
    public static List<OptionMeta>? ReadSectionChoices(string description)
    {
        var found = SectionPair().Matches(description);
        if (found.Count < 2) return null;
        var list = new List<OptionMeta>();
        foreach (Match m in found)
        {
            var v = m.Groups["v"].Value;
            if (list.All(o => o.Value != v)) list.Add(new OptionMeta { Value = v, Label = m.Groups["l"].Value.Trim() });
        }
        return list.Count >= 2 ? list : null;
    }

    public static void ReadRange(string help, string value, KeyMeta km)
    {
        if (help.Length == 0) return;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
        var found = new List<(double, double)>();
        foreach (Match m in RangeWords().Matches(help)) Add(m);
        foreach (Match m in RangeDash().Matches(help)) Add(m);
        if (found.Distinct().Count() != 1) return;
        var (lo, hi) = found[0];
        if (v < lo || v > hi) return;
        km.Min = lo;
        km.Max = hi;
        km.RangeGuessed = true;

        void Add(Match m)
        {
            if (double.TryParse(m.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                double.TryParse(m.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && a < b)
                found.Add((a, b));
        }
    }

    public static bool LooksBooleanText(string help) => BoolSentence().IsMatch(help);

    private static void ScanModLiveHints(ModMeta meta, string text)
    {
        if (LiveHint().IsMatch(text)) meta.Live = true;
    }

    // ---------------------------------------------------------------- directives

    public static bool IsDirective(string body) => body.StartsWith('@');

    /// Splits "@ type=int min=0 label=Use cost" into scope words and pairs.
    /// An unquoted value runs until the next " name=".
    public static (List<string> Words, List<KeyValuePair<string, string>> Pairs) ParseDirective(string body)
    {
        var s = body.TrimStart('@').Trim();
        var words = new List<string>();
        var pairs = new List<KeyValuePair<string, string>>();
        var first = DirectivePair().Match(s);
        var head = first.Success ? s[..first.Index] : s;
        words.AddRange(head.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()));
        for (var m = first; m.Success; m = m.NextMatch())
        {
            var val = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["v"].Value.Trim();
            pairs.Add(new(m.Groups["k"].Value.ToLowerInvariant(), val.Replace("\\n", "\n")));
        }
        return (words, Loc.PickPairs(pairs));
    }

    private static void ApplyFloatingDirective(IniAnalysis a, string section, string body)
    {
        var (words, pairs) = ParseDirective(body);
        if (words.Contains("mod")) ApplyModPairs(a.Directives, pairs);
        else if (words.Contains("section") && section.Length > 0) ApplySectionPairs(a.Directives.GetOrAddSection(section), pairs);
        else if (section.Length == 0) ApplyModPairs(a.Directives, pairs);
    }

    private static void ApplySectionDirective(SectionMeta s, ModMeta mod, string body)
    {
        var (words, pairs) = ParseDirective(body);
        if (words.Contains("mod")) ApplyModPairs(mod, pairs);
        else ApplySectionPairs(s, pairs, words);
    }

    private static void ApplyKeyDirective(KeyMeta k, ModMeta mod, string body)
    {
        var (words, pairs) = ParseDirective(body);
        if (words.Contains("mod")) { ApplyModPairs(mod, pairs); return; }
        foreach (var w in words)
        {
            switch (w)
            {
                case "advanced": k.Advanced = true; break;
                case "hidden": k.Hidden = true; break;
                case "readonly": k.ReadOnly = true; break;
                case "live": k.Live = true; break;
                case "restart": k.Live = false; break;
                case "custom": k.AllowCustom = true; break;
                default:
                    var t = SettingTypes.Normalize(w);
                    if (t.Length > 0 && t != SettingTypes.String || w == "string" || w == "text") k.Type = t;
                    break;
            }
        }
        foreach (var (key, value) in pairs) ApplyKeyProperty(k, key, value);
    }

    public static void ApplyKeyProperty(KeyMeta k, string key, string value)
    {
        switch (key)
        {
            case "label": case "name": case "title": k.Label = value; break;
            case "type": k.Type = SettingTypes.Normalize(value); break;
            case "help": case "description": case "desc": k.Help = value; break;
            case "tooltip": case "tip": k.Tooltip = value; break;
            case "unit": case "suffix": k.Unit = value; break;
            case "group": case "category": k.Group = value; break;
            case "default": k.Default = value; break;
            case "true": case "on": k.TrueValue = value; break;
            case "false": case "off": k.FalseValue = value; break;
            case "format": k.Format = value.ToLowerInvariant(); break;
            case "min": k.Min = Num(value); break;
            case "max": k.Max = Num(value); break;
            case "step": k.Step = Num(value); break;
            case "range":
            {
                var m = Regex.Match(value, @"^\s*(?<a>-?[\d.]+)\s*(?:\.\.|to|,|:)\s*(?<b>-?[\d.]+)\s*$");
                if (m.Success) { k.Min = Num(m.Groups["a"].Value); k.Max = Num(m.Groups["b"].Value); }
                break;
            }
            case "options": case "choices": case "values": k.Options = ParseOptionList(value); break;
            case "custom": case "allowcustom": k.AllowCustom = Flag(value); break;
            case "live": k.Live = Flag(value); break;
            case "restart": k.Live = !Flag(value); break;
            case "advanced": k.Advanced = Flag(value); break;
            case "hidden": k.Hidden = Flag(value); break;
            case "readonly": k.ReadOnly = Flag(value); break;
            case "order": k.Order = (int?)Num(value); break;
        }
    }

    /// "0:Off|1:On|2:Auto", or "low|medium|high".
    public static List<OptionMeta> ParseOptionList(string value)
    {
        var list = new List<OptionMeta>();
        foreach (var part in value.Split('|'))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            var colon = p.IndexOf(':');
            list.Add(colon > 0
                ? new OptionMeta { Value = p[..colon].Trim(), Label = p[(colon + 1)..].Trim() }
                : new OptionMeta { Value = p });
        }
        return list;
    }

    private static void ApplySectionPairs(SectionMeta s, List<KeyValuePair<string, string>> pairs, List<string>? words = null)
    {
        if (words != null)
            foreach (var w in words)
            {
                if (w == "hidden") s.Hidden = true;
                if (w == "advanced") s.Advanced = true;
            }
        foreach (var (key, value) in pairs)
        {
            switch (key)
            {
                case "label": case "title": case "name": s.Label = value; break;
                case "description": case "help": case "desc": s.Description = value; break;
                case "hidden": s.Hidden = Flag(value); break;
                case "advanced": s.Advanced = Flag(value); break;
                case "order": s.Order = (int?)Num(value); break;
            }
        }
    }

    private static void ApplyModPairs(ModMeta m, List<KeyValuePair<string, string>> pairs)
    {
        foreach (var (key, value) in pairs)
        {
            switch (key)
            {
                case "name": case "title": m.Name = value; break;
                case "version": m.Version = value; break;
                case "author": case "authors": m.Author = value; break;
                case "url": case "link": case "homepage": m.Url = value; break;
                case "description": case "desc": case "help": m.Description = value; break;
                case "game": m.Game = value; break;
                case "live": case "hotreload": m.Live = Flag(value); break;
                case "ini": m.Ini = value; break;
                case "comments": case "usecomments": m.UseComments = Flag(value); break;
            }
        }
    }

    public static bool Flag(string v) => v.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "";

    public static double? Num(string v) =>
        double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ---------------------------------------------------------------- patterns

    [GeneratedRegex(@"^[-=_~*#]{3,}\s*(?<t>[^-=_~*#].*?)?\s*[-=_~*#]*$")]
    private static partial Regex DividerLeading();

    [GeneratedRegex(@"^(?<t>.*?)\s*[-=_~*#]{4,}$")]
    private static partial Regex DividerTrailing();

    [GeneratedRegex(@"^\s*([-*•]|\d+[.)])\s")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s+(?<v><[^>]+>|-?[\w.+]+)\s{2,}(?<l>\S.*)$|^\s+(?<v><[^>]+>)\s+(?<l>\S.*)$")]
    private static partial Regex TableRow();

    [GeneratedRegex(@"(?<![\w.:])(?<v>-?\d+)\s*[=:]\s*(?<l>[A-Za-z][^,|;=]*?)(?=\s*(?:,|\||;|$|\.\s|\.$))")]
    private static partial Regex InlinePair();

    [GeneratedRegex(@"^(?<list>.+?)\s+-\s+Default\s*(?:\((?<auto>\w+)\))?\s+is\s+(?<def>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultList();

    [GeneratedRegex(@"(?<![\w.])(?<a>-?\d+(?:\.\d+)?)\s+(?:to|through|\.\.)\s+(?<b>-?\d+(?:\.\d+)?)(?![\w.])")]
    private static partial Regex RangeWords();

    [GeneratedRegex(@"(?<![\w.\-])(?<a>\d+(?:\.\d+)?)(?:-|\.\.)(?<b>\d+(?:\.\d+)?)(?![\w.])")]
    private static partial Regex RangeDash();

    [GeneratedRegex(@"^(1|0)\s+([a-z]+s|is|does)\b|(^|\n)\s*0\s{2,}(off|no|disabled?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BoolSentence();

    [GeneratedRegex(@"takes effect (on )?(the )?next (start|launch|restart|run)|requires? (a )?(game )?restart|restart the game|after a restart", RegexOptions.IgnoreCase)]
    private static partial Regex RestartHint();

    [GeneratedRegex(@"picked up (within|while|live)|while the game (is )?runs?|hot.?reload|reloads? (this|the) (file|ini)|changes apply (immediately|live|at once)", RegexOptions.IgnoreCase)]
    private static partial Regex LiveHint();

    [GeneratedRegex(@"(?<![\w.\-])(?<v>-?\d+)\s+(?<l>[A-Za-z][A-Za-z ]*?)\s*(?=,|\(|$|\.)")]
    private static partial Regex SectionPair();

    // A name may end in a language tag, as in label.de="Kosten".
    [GeneratedRegex(@"(?<![\w.])(?<k>[A-Za-z_]\w*(?:\.[A-Za-z]{2,3}(?:[-_][A-Za-z0-9]{2,8})?)?)\s*=\s*(?:""(?<q>[^""]*)""|(?<v>.*?))(?=\s+[A-Za-z_]\w*(?:\.[A-Za-z]{2,3}(?:[-_][A-Za-z0-9]{2,8})?)?\s*=|\s*$)")]
    private static partial Regex DirectivePair();
}
