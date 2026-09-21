using System.Collections.ObjectModel;
using System.IO;
using IniMaster.Core;

namespace IniMaster.ViewModels;

public sealed class ModViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private IniFileViewModel? _selectedFile;
    private bool _loaded;

    public ModViewModel(MainViewModel main, ModInfo info)
    {
        _main = main;
        Info = info;
        foreach (var t in info.Files) Files.Add(new IniFileViewModel(main, t));
        _selectedFile = Files.FirstOrDefault();
    }

    public ModInfo Info { get; private set; }
    public string Id => Info.Id;
    public string Name => Info.DisplayName;
    public ObservableCollection<IniFileViewModel> Files { get; } = new();
    public bool HasManyFiles => Files.Count > 1;

    public IniFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (Set(ref _selectedFile, value)) EnsureLoaded();
        }
    }

    public string Subtitle
    {
        get
        {
            var ini = Files.Count == 0 ? "no ini" : string.Join(", ", Files.Select(f => f.FileName));
            if (!Info.HasPlugin) return ini + ", no plugin";
            var missing = Files.Count > 0 && Files.All(f => !f.Exists) ? " (missing)" : "";
            return ini + missing;
        }
    }

    public string SourceBadge => Info.BestSource switch
    {
        MetaSource.Embedded => "Embedded help",
        MetaSource.Sidecar => ".inimeta",
        _ => "",
    };

    public bool HasBadge => SourceBadge.Length > 0;

    public string PluginText => Info.AsiPath == null
        ? "No plugin with this name. The ini may belong to a DLL mod or an overlay."
        : $"Plugin: {Path.GetFileName(Info.AsiPath)}";

    public string MetaText
    {
        get
        {
            var f = SelectedFile;
            if (f == null) return "";
            var parts = new List<string>(f.Target.MetaSources);
            if (parts.Count == 0) parts.Add("the ini's own comments");
            var text = "Help from " + string.Join(", then ", parts) + ".";
            if (f.Target.MetaErrors.Count > 0) text += " Could not read: " + string.Join("; ", f.Target.MetaErrors);
            return text;
        }
    }

    public bool HasChanges => Files.Any(f => f.HasChanges);

    public void EnsureLoaded()
    {
        if (_selectedFile == null) return;
        if (!_loaded || _selectedFile.Sections.Count == 0 && _selectedFile.Exists) _selectedFile.Load();
        _loaded = true;
        foreach (var f in Files) if (f != _selectedFile && f.Sections.Count == 0) f.Load();
        Raise(nameof(MetaText));
    }

    /// Swaps in rescanned metadata while keeping the view models, so unsaved
    /// edits survive a plugin being updated under us.
    public void Update(ModInfo info)
    {
        Info = info;
        foreach (var t in info.Files)
        {
            var existing = Files.FirstOrDefault(f => string.Equals(f.Path, t.IniPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (_loaded) existing.UpdateTarget(t);
            }
            else
            {
                var vm = new IniFileViewModel(_main, t);
                Files.Add(vm);
                if (_loaded) vm.Load();
            }
        }
        foreach (var gone in Files.Where(f => !info.Files.Any(t => string.Equals(t.IniPath, f.Path, StringComparison.OrdinalIgnoreCase))).ToList())
            Files.Remove(gone);
        if (_selectedFile == null || !Files.Contains(_selectedFile)) SelectedFile = Files.FirstOrDefault();
        Raise(nameof(Name), nameof(Subtitle), nameof(SourceBadge), nameof(HasBadge), nameof(PluginText), nameof(MetaText), nameof(HasManyFiles));
    }

    public void RaiseChanges() => Raise(nameof(HasChanges), nameof(Subtitle));
}
