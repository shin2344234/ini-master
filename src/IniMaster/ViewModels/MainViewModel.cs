using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using IniMaster.Core;
using IniMaster.Localization;
using IniMaster.Services;
using Microsoft.Win32;

namespace IniMaster.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly FolderWatcher _watcher;
    private readonly DispatcherTimer _gameTimer;
    private string? _gameRoot;
    private bool _gameRunning;
    private ModViewModel? _selectedMod;
    private SettingViewModel? _selectedSetting;
    private string _filter = "";
    private string _modFilter = "";
    private string _status = "";
    private int _tab;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => o is ModViewModel m && FilterMod(m);
        _watcher = new FolderWatcher(OnFilesChanged);
        _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameTimer.Tick += (_, _) => GameRunning = GameLocator.IsGameRunning();

        BrowseCommand = new RelayCommand(Browse);
        RescanCommand = new RelayCommand(Rescan, () => GameRoot != null);
        SaveCommand = new RelayCommand(() => CurrentFile?.Save(), () => CurrentFile?.HasChanges == true);
        SaveAllCommand = new RelayCommand(SaveAll, () => Mods.Any(m => m.HasChanges));
        RevertCommand = new RelayCommand(() => CurrentFile?.RevertAll(), () => CurrentFile?.HasChanges == true);
        OpenFolderCommand = new RelayCommand(() => OpenShell(CurrentFile != null ? Path.GetDirectoryName(CurrentFile.Path)! : BinFolder!), () => GameRoot != null);
        OpenIniCommand = new RelayCommand(() => OpenShell(CurrentFile!.Path), () => CurrentFile?.Exists == true);
        OpenBackupsCommand = new RelayCommand(OpenBackups, () => CurrentFile != null);
        ExportMetaCommand = new RelayCommand(ExportMeta, () => CurrentFile != null);
        GuideCommand = new RelayCommand(OpenGuide);
        ClearFilterCommand = new RelayCommand(() => Filter = "");
        OpenLanguageFolderCommand = new RelayCommand(OpenLanguageFolder);
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckForUpdates(quiet: false), () => !_updating);
    }

    public ObservableCollection<ModViewModel> Mods { get; } = new();
    public ICollectionView ModsView { get; }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenIniCommand { get; }
    public RelayCommand OpenBackupsCommand { get; }
    public RelayCommand ExportMetaCommand { get; }
    public RelayCommand GuideCommand { get; }
    public RelayCommand ClearFilterCommand { get; }

    public string? GameRoot
    {
        get => _gameRoot;
        private set
        {
            if (Set(ref _gameRoot, value)) Raise(nameof(GameRootText), nameof(HasGame), nameof(BinFolder));
        }
    }

    public bool HasGame => GameRoot != null;
    public string? BinFolder => GameRoot == null ? null : GameLocator.BinFolder(GameRoot);
    public string GameRootText => GameRoot ?? Loc.T("Crimson Desert was not found. Choose its folder.");

    public bool GameRunning
    {
        get => _gameRunning;
        set
        {
            if (!Set(ref _gameRunning, value)) return;
            Raise(nameof(GameStateText));
            foreach (var m in Mods) foreach (var f in m.Files) f.RefreshGameState();
            if (value) Status = Loc.T("Crimson Desert started. Changes still save straight to the ini files.");
        }
    }

    public string GameStateText => GameRunning ? Loc.T("Game running") : Loc.T("Game not running");

    public bool ApplyInstantly
    {
        get => _settings.ApplyInstantly;
        set
        {
            if (_settings.ApplyInstantly == value) return;
            _settings.ApplyInstantly = value;
            Raise();
            if (value) SaveAll();
        }
    }

    public bool ShowAdvanced
    {
        get => _settings.ShowAdvanced;
        set { if (_settings.ShowAdvanced == value) return; _settings.ShowAdvanced = value; Raise(); CurrentFile?.ApplyFilter(); }
    }

    public bool RawValues
    {
        get => _settings.RawValues;
        set { if (_settings.RawValues == value) return; _settings.RawValues = value; Raise(); }
    }

    public bool ShowIniOnly
    {
        get => _settings.ShowIniOnly;
        set { if (_settings.ShowIniOnly == value) return; _settings.ShowIniOnly = value; Raise(); ModsView.Refresh(); }
    }

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value ?? "")) { CurrentFile?.ApplyFilter(); Raise(nameof(HasFilter)); } }
    }

    public bool HasFilter => _filter.Length > 0;

    public string ModFilter
    {
        get => _modFilter;
        set { if (Set(ref _modFilter, value ?? "")) ModsView.Refresh(); }
    }

    public string Status { get => _status; set => Set(ref _status, value); }

    /// 0 settings, 1 file text.
    public int Tab { get => _tab; set => Set(ref _tab, value); }

    public ModViewModel? SelectedMod
    {
        get => _selectedMod;
        set
        {
            _selectedMod?.SelectedFile?.FlushPendingSave();
            if (!Set(ref _selectedMod, value)) return;
            value?.EnsureLoaded();
            SelectedSetting = null;
            if (value != null) _settings.LastMod = value.Id;
            CurrentFile?.ApplyFilter();
            Raise(nameof(CurrentFile), nameof(HasSelection));
        }
    }

    public void OnSelectedFileChanged()
    {
        SelectedSetting = null;
        CurrentFile?.ApplyFilter();
        Raise(nameof(CurrentFile));
    }

    public bool HasSelection => SelectedMod != null;
    public IniFileViewModel? CurrentFile => SelectedMod?.SelectedFile;

    public SettingViewModel? SelectedSetting
    {
        get => _selectedSetting;
        set { if (Set(ref _selectedSetting, value)) Raise(nameof(HasSelectedSetting)); }
    }

    public bool HasSelectedSetting => SelectedSetting != null;

    public string ChangeSummary
    {
        get
        {
            var n = Mods.SelectMany(m => m.Files).Sum(f => f.ChangeCount + (f.RawDirty ? 1 : 0));
            return n == 0 ? "" : Loc.Plural(n, "1 unsaved change", "{0} unsaved changes");
        }
    }

    // ------------------------------------------------------------ language

    public sealed record LanguageChoice(string Tag, string Name, bool IsCurrent, RelayCommand Command);

    /// "Same as Windows" first, then English and every translation found.
    public IReadOnlyList<LanguageChoice> LanguageChoices
    {
        get
        {
            var current = _settings.Language;
            var list = new List<LanguageChoice>
            {
                new("", Loc.T("Same as Windows: {0}", Loc.DisplayName(Languages.WindowsTag)), string.IsNullOrEmpty(current),
                    new RelayCommand(() => SetLanguage(null))),
            };
            foreach (var tag in Loc.Available(Languages.Folders))
                list.Add(new(tag, Loc.DisplayName(tag), string.Equals(current, tag, StringComparison.OrdinalIgnoreCase),
                    new RelayCommand(() => SetLanguage(tag))));
            return list;
        }
    }

    public RelayCommand OpenLanguageFolderCommand { get; }

    // ------------------------------------------------------------ updates

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private bool _updating;

    public RelayCommand CheckUpdatesCommand { get; }

    public bool CheckUpdatesOnStart
    {
        get => _settings.CheckUpdates;
        set { if (_settings.CheckUpdates == value) return; _settings.CheckUpdates = value; Raise(); }
    }

    /// Asks GitHub for the newest release. On start this says nothing unless
    /// there is one to offer; from the menu it reports either way.
    public async Task CheckForUpdates(bool quiet)
    {
        if (_updating || Updates.Current is not { } current) return;
        _updating = true;
        try
        {
            if (!quiet) Status = Loc.T("Asking GitHub for the newest version...");
            ReleaseInfo? release;
            try { release = await Updates.LatestAsync(Http); }
            catch (Exception ex)
            {
                if (!quiet) Status = Loc.T("Could not check for updates: {0}", ex.Message);
                return;
            }
            if (release == null || release.Version <= current)
            {
                if (!quiet) Status = Loc.T("INI Master {0} is the newest version.", current.ToString());
                return;
            }
            if (quiet && string.Equals(_settings.SkippedVersion, release.Version.ToString(), StringComparison.Ordinal)) return;

            // A copy that is not signed, or one in a folder this account cannot
            // write, cannot replace itself. Those go to the download page.
            var canInstall = Updates.Signer(Updates.ExePath) != null && Updates.CanReplaceExe();
            var question = canInstall
                ? Loc.T("INI Master {0} is out. You have {1}.\n\n{2}\n\nDownload and install it now?", release.Version.ToString(), current.ToString(), Summary(release.Notes))
                : Loc.T("INI Master {0} is out. You have {1}.\n\n{2}\n\nThis copy cannot replace itself, so open the download page?", release.Version.ToString(), current.ToString(), Summary(release.Notes));
            if (MessageBox.Show(question, "INI Master", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                _settings.SkippedVersion = release.Version.ToString();
                Status = Loc.T("Staying on {0}. More, Check for updates asks again.", current.ToString());
                return;
            }
            _settings.SkippedVersion = null;
            if (!canInstall) { OpenShell(release.PageUrl); return; }
            if (!ConfirmDiscard()) return;
            await Install(release);
        }
        finally { _updating = false; }
    }

    private async Task Install(ReleaseInfo release)
    {
        var percent = -1;
        var progress = new Progress<double>(f =>
        {
            var p = (int)(f * 100);
            if (p == percent) return;
            percent = p;
            Status = Loc.T("Downloading INI Master {0}, {1}%", release.Version.ToString(), p);
        });
        try
        {
            var file = await Updates.DownloadAsync(release, Http, progress);
            Status = Loc.T("Installing INI Master {0}...", release.Version.ToString());
            _settings.Save();
            Updates.Apply(file);
            Shutdown();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status = Loc.T("Could not install the update: {0}", ex.Message);
            MessageBox.Show(Loc.T("Could not install the update: {0}", ex.Message) + "\n\n" + Updates.ReleasesPage,
                "INI Master", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// The first paragraph of the release notes, short enough for a message box.
    private static string Summary(string? notes)
    {
        var text = (notes ?? "").Replace("\r", "").Trim();
        if (text.Length == 0) return "";
        var para = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return para.Length > 400 ? para[..397].TrimEnd() + "..." : para;
    }

    /// Picks up translation files added since the menu last opened.
    public void RefreshLanguageChoices() => Raise(nameof(LanguageChoices));

    /// Switches language in place. Metadata is read again, since a mod may
    /// ship its help in more than one language; unsaved edits carry over.
    public void SetLanguage(string? tag)
    {
        _settings.Language = string.IsNullOrWhiteSpace(tag) ? null : Loc.Normalize(tag);
        foreach (var f in Mods.SelectMany(m => m.Files)) f.FlushPendingSave();
        Languages.Apply(_settings.Language);
        Rescan();
        foreach (var m in Mods) m.RefreshText();
        Raise(string.Empty);
        Status = Loc.SourcePath == null && Loc.Language != "en"
            ? Loc.T("No translation for {0} yet, so the app stays in English. Mod help still follows the language where the mod has it.", Loc.DisplayName(Loc.Language))
            : Loc.IgnoredLines > 0
                ? Loc.T("Language: {0}. {1} lines of the translation were skipped because their placeholders did not match the English.", Loc.DisplayName(Loc.Language), Loc.IgnoredLines)
                : Loc.T("Language: {0}.", Loc.DisplayName(Loc.Language));
    }

    private void OpenLanguageFolder()
    {
        try { OpenShell(Languages.PrepareUserFolder()); }
        catch (Exception ex) { Status = Loc.T("Could not open the language folder: {0}", ex.Message); }
    }

    // ------------------------------------------------------------ start up

    public void Start(string? commandLinePath)
    {
        var root = GameLocator.Normalize(commandLinePath) ?? GameLocator.Normalize(_settings.GameRoot) ?? GameLocator.Find();
        GameRunning = GameLocator.IsGameRunning();
        _gameTimer.Start();
        Updates.CleanUp();
        if (_settings.CheckUpdates) _ = CheckForUpdates(quiet: true);
        if (root == null)
        {
            Status = Loc.T("Could not find Crimson Desert. Use the Game folder button to pick it.");
            return;
        }
        SetGameRoot(root);
    }

    private void SetGameRoot(string root)
    {
        GameRoot = root;
        _settings.GameRoot = root;
        _watcher.Watch(ModScanner.PluginFolders(root));
        Rescan();
        var last = Mods.FirstOrDefault(m => string.Equals(m.Id, _settings.LastMod, StringComparison.OrdinalIgnoreCase));
        SelectedMod = last ?? Mods.FirstOrDefault(FilterMod);
    }

    public static string CommunityFolder => Path.Combine(AppContext.BaseDirectory, "inimeta");

    public void Rescan()
    {
        if (GameRoot == null) return;
        List<ModInfo> infos;
        try { infos = ModScanner.Scan(GameRoot, Directory.Exists(CommunityFolder) ? CommunityFolder : null); }
        catch (Exception ex)
        {
            Status = Loc.T("Could not read the plugin folder: {0}", ex.Message);
            return;
        }
        var selectedId = SelectedMod?.Id;
        foreach (var info in infos)
        {
            var existing = Mods.FirstOrDefault(m => string.Equals(m.Id, info.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Update(info);
            else Mods.Add(new ModViewModel(this, info));
        }
        foreach (var gone in Mods.Where(m => !infos.Any(i => string.Equals(i.Id, m.Id, StringComparison.OrdinalIgnoreCase))).ToList())
            Mods.Remove(gone);

        // Plugins first, then ini files without one, each alphabetical.
        var sorted = Mods.OrderBy(m => m.Info.HasPlugin ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var at = Mods.IndexOf(sorted[i]);
            if (at != i) Mods.Move(at, i);
        }
        if (selectedId != null && SelectedMod == null)
            SelectedMod = Mods.FirstOrDefault(m => string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        var plugins = Mods.Count(m => m.Info.HasPlugin);
        Status = Loc.T("Found {0} and {1} in {2}.",
            Loc.Plural(plugins, "1 plugin", "{0} plugins"),
            Loc.Plural(Mods.Count - plugins, "1 other ini file", "{0} other ini files"),
            BinFolder);
    }

    private bool FilterMod(ModViewModel m)
    {
        if (!ShowIniOnly && !m.Info.HasPlugin) return false;
        return _modFilter.Length == 0 || m.Name.Contains(_modFilter, StringComparison.OrdinalIgnoreCase)
               || m.Files.Any(f => f.FileName.Contains(_modFilter, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------ watching

    private void OnFilesChanged(IReadOnlyCollection<string> paths)
    {
        var rescan = false;
        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".asi" or ".inimeta") { rescan = true; continue; }
            var owner = Mods.SelectMany(m => m.Files).FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (owner == null) { rescan |= File.Exists(path); continue; }
            if (owner.Sections.Count > 0 || !owner.Exists) owner.OnChangedOnDisk();
        }
        if (rescan) Rescan();
        foreach (var m in Mods) m.RaiseChanges();
    }

    public void OnFileStateChanged()
    {
        Raise(nameof(ChangeSummary));
        SelectedMod?.RaiseChanges();
        CommandManager.InvalidateRequerySuggested();
    }

    // ------------------------------------------------------------ commands

    private void Browse()
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.T("Choose the Crimson Desert folder, the one with bin64 in it"),
            InitialDirectory = GameRoot ?? @"C:\Program Files (x86)\Steam\steamapps\common",
        };
        if (dlg.ShowDialog() != true) return;
        var root = GameLocator.Normalize(dlg.FolderName);
        if (root == null)
        {
            MessageBox.Show(Loc.T("{0} is not in that folder or in a bin64 folder inside it.", GameLocator.ExeName), "INI Master",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!ConfirmDiscard()) return;
        Mods.Clear();
        SelectedMod = null;
        SetGameRoot(root);
    }

    public void SaveAll()
    {
        foreach (var f in Mods.SelectMany(m => m.Files).Where(f => f.HasUnsavedWork).ToList()) f.SaveAllWork();
    }

    public bool HasUnsaved => Mods.Any(m => m.HasChanges);

    /// Asks before throwing edits away. False means stay.
    public bool ConfirmDiscard()
    {
        foreach (var f in Mods.SelectMany(m => m.Files)) f.FlushPendingSave();
        if (!HasUnsaved) return true;
        var r = MessageBox.Show(Loc.T("There are unsaved changes ({0}). Save them first?", ChangeSummary), "INI Master",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.No) return true;
        SaveAll();
        if (!HasUnsaved) return true;
        MessageBox.Show(Loc.T("Some changes were not saved ({0}).\n\n{1}\n\nFix them, or choose No next time to discard them.", ChangeSummary, Status),
            "INI Master", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    public void Shutdown()
    {
        _gameTimer.Stop();
        _watcher.Dispose();
        _settings.Save();
    }

    private void OpenBackups()
    {
        var dir = IniStore.BackupFolder(CurrentFile!.Path);
        Directory.CreateDirectory(dir);
        OpenShell(dir);
    }

    private void ExportMeta()
    {
        var f = CurrentFile!;
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Save a metadata template for this ini"),
            FileName = Path.GetFileNameWithoutExtension(f.Path) + ModScanner.SidecarExtension,
            Filter = Loc.T("INI Master metadata") + " (*.inimeta)|*.inimeta|" + Loc.T("All files") + "|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = IniStore.ReadBytes(f.Path);
            var view = IniView.Build(f.Target, bytes == null ? null : IniDocument.Load(bytes));
            File.WriteAllText(dlg.FileName, MetaExporter.ToJson(view, f.FileName));
            Status = Loc.T("Wrote {0}. Edit it, then ship it next to the ini or embed it in the plugin.", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { Status = Loc.T("Could not write the template: {0}", ex.Message); }
    }

    private void OpenGuide()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "docs", "METADATA.md");
        if (File.Exists(local)) OpenShell(local);
        else MessageBox.Show(GuideText, Loc.T("Adding help for INI Master"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static string GuideText => Loc.T("INI Master reads help for a setting from three places, and later ones win:\n\n1. The ini's own comments. The comment lines directly above a key are its help. Lines like \";   0  off\" and \";   1  on\" become choices, and \"; ---- name\" starts a group. A comment line that starts with ;@ sets things exactly, for example:\n   ;@ type=int min=0 max=100 unit=% label=\"Use cost\"\n\n2. A file named after the ini with the .inimeta extension, next to it. It holds JSON, or an annotated copy of the default ini.\n\n3. The plugin itself. Add the same .inimeta file to the plugin's .rc file:\n   INIMETA INIMETA \"MyMod.inimeta\"\n\nAny text in the metadata can come in several languages. More > Export metadata template writes a starting .inimeta for the selected ini.");

    private static void OpenShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }
}
