namespace IniMaster.Core;

public abstract record ViewItem;
public sealed record ViewNote(string Text) : ViewItem;
public sealed record ViewGroup(string Title) : ViewItem;
/// FileValue is null when the file lacks the key and it came from metadata.
public sealed record ViewSetting(ResolvedSetting Setting, string? FileValue) : ViewItem;

public sealed class ViewSection
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool Hidden { get; init; }
    public bool Advanced { get; init; }
    public List<ViewItem> Items { get; } = new();
}

/// Everything the settings page shows for one ini file: the file's own
/// comments, then any sidecar, then the plugin's embedded metadata.
public sealed class IniView
{
    public ModMeta Meta { get; } = new();
    public List<ViewSection> Sections { get; } = new();

    /// False when the ini's own comments were left out, which is what a
    /// plugin carrying its own metadata means unless it says otherwise.
    public bool UsesComments { get; private set; } = true;

    public IEnumerable<ViewSetting> Settings => Sections.SelectMany(s => s.Items.OfType<ViewSetting>());

    public static IniView Build(IniTarget target, IniDocument? doc)
    {
        var view = new IniView();
        var analysis = doc != null ? CommentAnalyzer.Analyze(doc) : null;
        // Metadata inside the plugin describes the whole ini, so the file's own
        // comments are left out rather than shown underneath it. "comments"
        // forces either way, from the ini's own ";@mod comments=" line or,
        // over the top of that, from the sidecar or the plugin. The ";@"
        // directives stay whatever the answer, since they are instructions
        // rather than prose.
        var directives = analysis?.OnlyDirectives();
        view.UsesComments = target.Meta.UseComments ?? directives?.UseComments
            ?? target.Meta.Source < MetaSource.Embedded;
        if (analysis != null)
            view.Meta.OverlayWith(view.UsesComments
                ? analysis.Merged(MetaSource.IniComments, MetaSource.IniDirectives)
                : directives!);
        view.Meta.OverlayWith(target.Meta);

        var order = new List<string>();
        if (analysis != null) order.AddRange(analysis.SectionOrder);
        if (doc != null)
            foreach (var s in doc.SectionNames())
                if (!order.Contains(s, StringComparer.OrdinalIgnoreCase)) order.Add(s);
        foreach (var s in view.Meta.SectionOrder)
            if (!order.Contains(s, StringComparer.OrdinalIgnoreCase)) order.Add(s);

        // Metadata may order sections explicitly; unordered ones keep file order.
        var ranked = order.Select((name, i) => (name, i, rank: view.Meta.Sections.GetValueOrDefault(name)?.Order ?? int.MaxValue))
            .OrderBy(x => x.rank).ThenBy(x => x.i).Select(x => x.name).ToList();

        foreach (var name in ranked)
        {
            view.Meta.Sections.TryGetValue(name, out var sm);
            var section = new ViewSection
            {
                Name = name,
                Title = !string.IsNullOrWhiteSpace(sm?.Label) ? sm!.Label! : name.Length == 0 ? "(no section)" : name,
                Description = sm?.Description,
                Hidden = sm?.Hidden ?? false,
                Advanced = sm?.Advanced ?? false,
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<ViewItem>();
            // With the file's own comments left out, an annotated default ini
            // is the only place the headings and notes still exist, so they go
            // in ahead of the key each one introduces.
            Dictionary<string, List<ViewItem>>? leadIns = null;
            var tail = new List<ViewItem>();
            if (!view.UsesComments) leadIns = LeadIns(view.Meta.Layout.GetValueOrDefault(name), out tail);

            if (analysis != null && analysis.Layout.TryGetValue(name, out var layout))
            {
                foreach (var item in layout)
                {
                    switch (item.Kind)
                    {
                        case LayoutKind.Note: if (view.UsesComments) items.Add(new ViewNote(item.Text)); break;
                        case LayoutKind.Group: if (view.UsesComments && !string.IsNullOrWhiteSpace(item.Text)) items.Add(new ViewGroup(item.Text)); break;
                        case LayoutKind.Setting:
                            if (!seen.Add(item.Text)) break;
                            if (leadIns != null && leadIns.TryGetValue(item.Text, out var ahead)) items.AddRange(ahead);
                            var km = sm?.Keys.GetValueOrDefault(item.Text) ?? new KeyMeta();
                            var value = doc!.Get(name, item.Text);
                            items.Add(new ViewSetting(SettingResolver.Resolve(name, item.Text, value, km), value));
                            break;
                    }
                }
            }
            if (sm != null)
            {
                foreach (var key in sm.KeyOrder)
                {
                    if (!seen.Add(key)) continue;
                    if (leadIns != null && leadIns.TryGetValue(key, out var ahead)) items.AddRange(ahead);
                    var km = sm.Keys[key];
                    // A key only a sidecar or plugin knows about, with nothing
                    // but the comment layer behind it, is not worth a row.
                    if (km.Source < MetaSource.IniDirectives && doc?.Find(name, key) == null) continue;
                    var value = doc?.Get(name, key);
                    items.Add(new ViewSetting(SettingResolver.Resolve(name, key, value, km), value));
                }
            }

            items.AddRange(tail);
            section.Items.AddRange(Arrange(items));
            if (section.Items.Count > 0 || section.Description != null || doc?.SectionNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true)
                view.Sections.Add(section);
        }
        return view;
    }

    /// The headings and notes standing ahead of each key in a layout, plus
    /// whatever trails the last one.
    private static Dictionary<string, List<ViewItem>> LeadIns(List<LayoutItem>? layout, out List<ViewItem> tail)
    {
        var map = new Dictionary<string, List<ViewItem>>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<ViewItem>();
        foreach (var item in layout ?? new List<LayoutItem>())
        {
            switch (item.Kind)
            {
                case LayoutKind.Note: pending.Add(new ViewNote(item.Text)); break;
                case LayoutKind.Group: if (!string.IsNullOrWhiteSpace(item.Text)) pending.Add(new ViewGroup(item.Text)); break;
                case LayoutKind.Setting:
                    if (pending.Count > 0 && !map.ContainsKey(item.Text)) map[item.Text] = pending;
                    pending = new List<ViewItem>();
                    break;
            }
        }
        tail = pending;
        return map;
    }

    /// Applies metadata groups and order. Without either, the file's own
    /// layout stands as it is.
    private static IEnumerable<ViewItem> Arrange(List<ViewItem> items)
    {
        var settings = items.OfType<ViewSetting>().ToList();
        var hasGroups = settings.Any(s => s.Setting.Group != null);
        var hasOrder = settings.Any(s => s.Setting.Order != null);
        if (!hasGroups && !hasOrder) return items;

        var result = new List<ViewItem>();
        var notes = items.OfType<ViewNote>();
        IEnumerable<ViewSetting> ordered = settings;
        if (hasOrder)
        {
            ordered = settings.Select((s, i) => (s, i)).OrderBy(x => x.s.Setting.Order ?? int.MaxValue).ThenBy(x => x.i).Select(x => x.s);
            result.AddRange(notes);
        }
        if (!hasGroups)
        {
            if (!hasOrder) return items;
            result.AddRange(ordered);
            return result;
        }

        // Group by first appearance so a group listed twice stays together.
        var groups = new List<string?>();
        foreach (var s in ordered) if (!groups.Contains(s.Setting.Group)) groups.Add(s.Setting.Group);
        if (!hasOrder)
        {
            // Keep notes in place relative to the first setting after them.
            var pendingNotes = new List<ViewItem>();
            var byGroup = new Dictionary<string, List<ViewItem>>();
            var ungrouped = new List<ViewItem>();
            foreach (var item in items)
            {
                if (item is ViewGroup) continue;
                if (item is ViewNote) { pendingNotes.Add(item); continue; }
                var s = (ViewSetting)item;
                var bucket = s.Setting.Group == null ? ungrouped : byGroup.TryGetValue(s.Setting.Group, out var b) ? b : byGroup[s.Setting.Group] = new();
                bucket.AddRange(pendingNotes);
                pendingNotes.Clear();
                bucket.Add(s);
            }
            foreach (var g in groups)
            {
                if (g == null) { result.AddRange(ungrouped); continue; }
                result.Add(new ViewGroup(g));
                result.AddRange(byGroup[g]);
            }
            result.AddRange(pendingNotes);
            return result;
        }
        foreach (var g in groups)
        {
            if (g != null) result.Add(new ViewGroup(g));
            result.AddRange(ordered.Where(s => s.Setting.Group == g));
        }
        return result;
    }
}
