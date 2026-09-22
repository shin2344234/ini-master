using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Threading;
using IniMaster.Core;

namespace IniMaster.ViewModels;

public sealed class SectionViewModel : ObservableObject
{
    private bool _isVisible = true;

    public SectionViewModel(ViewSection s)
    {
        Name = s.Name;
        Title = s.Title;
        Description = s.Description;
        Advanced = s.Advanced;
        Hidden = s.Hidden;
    }

    public string Name { get; }
    public string Title { get; }
    public string? Description { get; }
    public bool Advanced { get; }
    public bool Hidden { get; }
    public bool ShowName => !string.Equals(Title, Name, StringComparison.OrdinalIgnoreCase) && Name.Length > 0;
    public ObservableCollection<ItemViewModel> Items { get; } = new();
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
}

/// One ini file on the settings page: its values, unsaved edits and the
/// plumbing that writes them while the game runs.
public sealed class IniFileViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dictionary<(string, string), string> _pending = new(new KeyComparer());
    private readonly DispatcherTimer _saveTimer;
    private IniDocument? _doc;
    private string? _lastHash;
    private string? _structure;
    private bool _backedUp;
    private string? _rawText;
    private bool _rawDirty;
    private IniView? _view;

    public IniFileViewModel(MainViewModel main, IniTarget target)
    {
        _main = main;
        Target = target;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        SaveRawCommand = new RelayCommand(() => SaveRaw(),() => _rawDirty);
        ReloadRawCommand = new RelayCommand(() => { _rawText = null; _rawDirty = false; Raise(nameof(RawText), nameof(RawDirty)); _main.OnFileStateChanged(); });
        CreateCommand = new RelayCommand(CreateFile, () => !Exists);
    }

    public IniTarget Target { get; private set; }
    public string Path => Target.IniPath;
    public string FileName => Target.FileName;
    public bool Exists => File.Exists(Path);
    public ObservableCollection<SectionViewModel> Sections { get; } = new();
    public ModMeta Meta => _view?.Meta ?? Target.Meta;
    public bool UsesComments => _view?.UsesComments ?? true;
    public bool? ModLive => Meta.Live;
    public bool GameRunning => _main.GameRunning;
    public RelayCommand SaveRawCommand { get; }
    public RelayCommand ReloadRawCommand { get; }
    public RelayCommand CreateCommand { get; }
    public string? LoadError { get; private set; }
    public bool HasChanges => _pending.Count > 0;

    /// Setting edits or an unsaved text edit on the Text tab.
    public bool HasUnsavedWork => HasChanges || _rawDirty;
    public int ChangeCount => _pending.Count;
    public bool HasErrors => AllSettings.Any(s => s.HasError && _pending.ContainsKey((s.Section, s.Key)));
    public bool CanCreate => !Exists && (Meta.DefaultIniText != null || Meta.Sections.Values.Any(s => s.Keys.Count > 0));
    public string MissingText => Meta.DefaultIniText != null || Meta.Sections.Values.Any(s => s.Keys.Count > 0)
        ? Loc.T("{0} does not exist yet. The plugin may write it on its first run, or you can create it now from the defaults it describes.", FileName)
        : Loc.T("{0} does not exist yet. Most plugins write their ini the first time the game runs with them.", FileName);

    /// Why the file cannot hold the value as it is encoded now, if it cannot.
    public string? EncodingProblem(string value) =>
        _doc != null && TextCodec.FirstUnencodable(value, _doc.Encoding) is { } ch
            ? Loc.T("{0} is saved as {1}, which has no \"{2}\".", FileName, _doc.Encoding.WebName, ch)
            : null;

    /// After a language change: every computed text on this file.
    public void RefreshText()
    {
        Raise(string.Empty);
        foreach (var s in AllSettings) s.RefreshText();
    }

    public IEnumerable<SettingViewModel> AllSettings => Sections.SelectMany(s => s.Items.OfType<SettingViewModel>());

    public bool HasPending(SettingViewModel s) => _pending.ContainsKey((s.Section, s.Key));

    public void UpdateTarget(IniTarget target, bool load)
    {
        Target = target;
        if (load) Load();
    }

    // ------------------------------------------------------------ load

    public void Load()
    {
        LoadError = null;
        byte[]? bytes = null;
        try { bytes = IniStore.ReadBytes(Path); }
        catch (Exception ex) { LoadError = Loc.T("Could not read {0}: {1}", FileName, ex.Message); }
        _doc = bytes == null ? null : IniDocument.Load(bytes);
        _lastHash = bytes == null ? null : IniStore.Hash(bytes);
        _structure = Structure(_doc);
        Build();
        if (!_rawDirty) _rawText = null;
        Raise(nameof(Exists), nameof(LoadError), nameof(Meta), nameof(ModLive), nameof(CanCreate), nameof(MissingText), nameof(RawText));
    }

    private void Build()
    {
        _view = IniView.Build(Target, _doc);
        Sections.Clear();
        foreach (var s in _view.Sections)
        {
            var svm = new SectionViewModel(s);
            foreach (var item in s.Items)
            {
                svm.Items.Add(item switch
                {
                    ViewNote n => new NoteViewModel(n.Text),
                    ViewGroup g => new GroupViewModel(g.Title),
                    ViewSetting v => new SettingViewModel(this, v.Setting, v.FileValue),
                    _ => throw new InvalidOperationException(),
                });
            }
            Sections.Add(svm);
        }
        // Edits made before a rebuild survive it.
        foreach (var s in AllSettings)
            if (_pending.TryGetValue((s.Section, s.Key), out var v)) s.RestoreEdit(v);
        ApplyFilter();
        RaiseChangeState();
    }

    /// The file with its values blanked. If only values changed on disk, the
    /// page updates in place and keeps focus and scroll position.
    private static string? Structure(IniDocument? doc)
    {
        if (doc == null) return null;
        var sb = new StringBuilder();
        foreach (var l in doc.Lines)
            sb.Append(l.Kind == IniLineKind.Key ? $"{l.Section}{l.Key}{l.InlineComment}" : l.Text).Append('\n');
        return sb.ToString();
    }

    /// Called when the watcher sees the file change. Our own writes are
    /// recognised by their hash and ignored.
    public void OnChangedOnDisk()
    {
        byte[]? bytes;
        try { bytes = IniStore.ReadBytes(Path); }
        catch { return; }
        var hash = bytes == null ? null : IniStore.Hash(bytes);
        if (hash == _lastHash) return;

        var doc = bytes == null ? null : IniDocument.Load(bytes);
        var structure = Structure(doc);
        _lastHash = hash;
        if (doc != null && _doc != null && structure == _structure)
        {
            _doc = doc;
            foreach (var s in AllSettings)
            {
                var fv = doc.Get(s.Section, s.Key);
                var key = (s.Section, s.Key);
                var keep = _pending.TryGetValue(key, out var mine);
                if (keep && mine == fv) { _pending.Remove(key); keep = false; }
                s.AcceptFileValue(fv, keep);
            }
            RaiseChangeState();
        }
        else
        {
            _doc = doc;
            _structure = structure;
            Build();
            Raise(nameof(Exists), nameof(Meta), nameof(ModLive), nameof(CanCreate), nameof(MissingText));
        }
        if (!_rawDirty) { _rawText = null; Raise(nameof(RawText)); }
        _main.Status = Loc.T("{0} changed on disk and was reloaded.", FileName);
    }

    // ------------------------------------------------------------ edit and save

    public void OnEdited(SettingViewModel s)
    {
        var key = (s.Section, s.Key);
        if (s.Value == s.SavedValue) _pending.Remove(key);
        else _pending[key] = s.Value;
        RaiseChangeState();
        if (_main.ApplyInstantly && !s.HasError)
        {
            // Text boxes save when typing pauses; everything else at once.
            _saveTimer.Stop();
            if (s.Type is SettingTypes.Bool or SettingTypes.Enum or SettingTypes.Key) Save();
            else _saveTimer.Start();
        }
    }

    public void FlushPendingSave()
    {
        if (_saveTimer.IsEnabled) { _saveTimer.Stop(); Save(); }
    }

    public bool Save()
    {
        _saveTimer.Stop();
        var invalid = AllSettings.Where(s => s.HasError && _pending.ContainsKey((s.Section, s.Key))).ToList();
        var edits = _pending.Where(p => !invalid.Any(s => (s.Section, s.Key) == p.Key))
                            .ToDictionary(p => p.Key, p => p.Value);
        if (edits.Count == 0)
        {
            if (invalid.Count > 0) _main.Status = Loc.T("Not saved. {0}: {1}", invalid[0].Label, invalid[0].Error);
            return invalid.Count == 0;
        }
        try
        {
            var existed = File.Exists(Path);
            var result = IniStore.Save(Path, edits, backup: !_backedUp);
            if (result.BackupPath != null) _backedUp = true;
            _lastHash = IniStore.Hash(result.Bytes);
            _doc = result.Document;
            _structure = Structure(_doc);
            foreach (var (key, value) in edits) _pending.Remove(key);
            foreach (var s in AllSettings)
                if (edits.ContainsKey((s.Section, s.Key))) s.AcceptFileValue(_doc.Get(s.Section, s.Key), keepEdit: _pending.ContainsKey((s.Section, s.Key)));
            if (!existed) Build();
            if (!_rawDirty) { _rawText = null; Raise(nameof(RawText)); }
            RaiseChangeState();
            Raise(nameof(Exists), nameof(CanCreate));
            _main.Status = SavedMessage(edits.Count, invalid);
            return invalid.Count == 0;
        }
        catch (UnauthorizedAccessException)
        {
            _main.Status = Loc.T("Windows refused to write {0}. If the game is under Program Files, run INI Master as administrator, or clear the file's read-only flag.", FileName);
        }
        catch (Exception ex)
        {
            _main.Status = Loc.T("Could not save {0}: {1}", FileName, ex.Message);
        }
        return false;
    }

    private string SavedMessage(int count, List<SettingViewModel> invalid)
    {
        var msg = Loc.T("Saved {0} to {1} at {2}.", Loc.Plural(count, "1 change", "{0} changes"), FileName, DateTime.Now.ToString("HH:mm:ss"));
        if (_main.GameRunning)
        {
            msg += ModLive switch
            {
                true => " " + Loc.T("The plugin rereads it while the game runs."),
                false => " " + Loc.T("This plugin reads its ini at startup, so restart the game to apply it."),
                _ => " " + Loc.T("The game is running. If the plugin only reads its ini at startup, the change applies next launch."),
            };
        }
        if (invalid.Count > 0) msg += " " + Loc.T("Skipped {0}: {1}", invalid[0].Label, invalid[0].Error);
        return msg;
    }

    public void RevertAll()
    {
        _saveTimer.Stop();
        _pending.Clear();
        foreach (var s in AllSettings) s.AcceptFileValue(s.FileValue, keepEdit: false);
        RaiseChangeState();
    }

    private void RaiseChangeState()
    {
        Raise(nameof(HasChanges), nameof(ChangeCount), nameof(HasErrors));
        _main.OnFileStateChanged();
    }

    public void RefreshGameState()
    {
        Raise(nameof(GameRunning));
        foreach (var s in AllSettings) s.RefreshGameState();
    }

    // ------------------------------------------------------------ filter

    public void ApplyFilter()
    {
        var filter = _main.Filter.Trim();
        foreach (var section in Sections)
        {
            var any = false;
            foreach (var item in section.Items)
            {
                if (item is SettingViewModel s)
                {
                    s.IsVisible = !s.R.Hidden && (_main.ShowAdvanced || !s.R.Advanced) && s.Matches(filter);
                    any |= s.IsVisible;
                }
                else item.IsVisible = filter.Length == 0;
            }
            var sectionMatches = filter.Length > 0 && section.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (sectionMatches)
                foreach (var s in section.Items.OfType<SettingViewModel>()) { s.IsVisible = !s.R.Hidden; any = true; }
            // A group heading shows only while something under it does.
            GroupViewModel? group = null;
            var groupHasVisible = false;
            foreach (var item in section.Items)
            {
                if (item is GroupViewModel g)
                {
                    if (group != null) group.IsVisible = groupHasVisible;
                    group = g;
                    groupHasVisible = false;
                }
                else if (item is SettingViewModel { IsVisible: true }) groupHasVisible = true;
            }
            if (group != null) group.IsVisible = groupHasVisible;
            section.IsVisible = !section.Hidden && (_main.ShowAdvanced || !section.Advanced) &&
                                (any || (filter.Length == 0 && (section.Items.Count > 0 || section.Description != null)));
        }
    }

    // ------------------------------------------------------------ raw text

    public string RawText
    {
        get
        {
            if (_rawText == null)
            {
                try
                {
                    var bytes = IniStore.ReadBytes(Path);
                    _rawText = bytes == null ? "" : TextCodec.Decode(bytes, out _);
                }
                catch (Exception ex) { _rawText = "; could not read the file: " + ex.Message; }
            }
            return _rawText;
        }
        set
        {
            if (_rawText == value) return;
            _rawText = value;
            var was = _rawDirty;
            _rawDirty = true;
            Raise(nameof(RawText), nameof(RawDirty));
            if (!was) _main.OnFileStateChanged();
        }
    }

    public bool RawDirty => _rawDirty;

    /// Saves the text edit if there is one, since it holds the whole file,
    /// and the setting edits otherwise.
    public bool SaveAllWork() => _rawDirty ? SaveRaw() : Save();

    private bool SaveRaw()
    {
        try
        {
            var result = IniStore.SaveText(Path, _rawText ?? "", backup: !_backedUp);
            if (result.BackupPath != null) _backedUp = true;
            _rawDirty = false;
            _rawText = null;
            _pending.Clear();
            Load();
            Raise(nameof(RawDirty));
            _main.OnFileStateChanged();
            _main.Status = result.Changed ? Loc.T("Saved {0} as text.", FileName) : Loc.T("Nothing to save. The text matches the file.");
            return true;
        }
        catch (Exception ex) { _main.Status = Loc.T("Could not save {0}: {1}", FileName, ex.Message); }
        return false;
    }

    // ------------------------------------------------------------ create

    private void CreateFile()
    {
        try
        {
            string text;
            if (Meta.DefaultIniText != null) text = Meta.DefaultIniText;
            else
            {
                var sb = new StringBuilder();
                if (Meta.Name != null) sb.Append("; ").AppendLine(Meta.Name);
                foreach (var name in Meta.SectionOrder)
                {
                    var sm = Meta.Sections[name];
                    if (sm.Keys.Count == 0) continue;
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append('[').Append(name).AppendLine("]");
                    foreach (var key in sm.KeyOrder)
                    {
                        var km = sm.Keys[key];
                        if (!string.IsNullOrWhiteSpace(km.Help))
                        {
                            sb.AppendLine();
                            foreach (var line in km.Help.Split('\n')) sb.Append("; ").AppendLine(line);
                        }
                        sb.Append(key).Append('=').AppendLine(km.Default ?? "");
                    }
                }
                text = sb.ToString();
            }
            IniStore.WriteAtomic(Path, TextCodec.Encode(text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false)));
            Load();
            _main.Status = Loc.T("Created {0} from the defaults the plugin describes.", FileName);
        }
        catch (Exception ex) { _main.Status = Loc.T("Could not create {0}: {1}", FileName, ex.Message); }
    }

    private sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item2));
    }
}
