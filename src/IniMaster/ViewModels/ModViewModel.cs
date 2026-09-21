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
            var ini = Files.Count == 0 ? Loc.T("no ini") : string.Join(", ", Files.Select(f => f.FileName));
            if (!Info.HasPlugin) return Loc.T("{0}, no plugin", ini);
            return Files.Count > 0 && Files.All(f => !f.Exists) ? Loc.T("{0} (missing)", ini) : ini;
        }
    }

    public string SourceBadge => Info.BestSource switch
    {
        MetaSource.Embedded => Loc.T("Embedded help"),
        MetaSource.Sidecar => ".inimeta",
        _ => "",
    };

    public bool HasBadge => SourceBadge.Length > 0;

    public string PluginText => Info.AsiPath == null
        ? Loc.T("No plugin with this name. The ini may belong to a DLL mod or an overlay.")
        : Loc.T("Plugin: {0}", Path.GetFileName(Info.AsiPath));

    public string MetaText
    {
        get
        {
            var f = SelectedFile;
            if (f == null) return "";
            var parts = new List<string>(f.Target.MetaSources);
            if (parts.Count == 0) parts.Add(Loc.T("the ini's own comments"));
            var text = Loc.T("Help from {0}.", string.Join(Loc.T(", then "), parts));
            if (f.Target.MetaErrors.Count > 0) text += " " + Loc.T("Could not read: {0}", string.Join("; ", f.Target.MetaErrors));
            return text;
        }
    }

    public bool HasChanges => Files.Any(f => f.HasUnsavedWork);

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
            // A file not shown yet still takes the new metadata, so it opens
            // with it later instead of with what the first scan found.
            if (existing != null) existing.UpdateTarget(t, load: _loaded);
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

    /// After a language change: every computed text, here and in the files.
    public void RefreshText()
    {
        Raise(string.Empty);
        foreach (var f in Files) f.RefreshText();
    }
}
