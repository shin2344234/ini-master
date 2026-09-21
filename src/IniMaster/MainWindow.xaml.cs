using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using System.Runtime.InteropServices;
using IniMaster.Services;
using IniMaster.ViewModels;

namespace IniMaster;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _vm;
    private readonly string? _startPath;

    public MainWindow(string? startPath)
    {
        InitializeComponent();
        _startPath = startPath;
        _settings = AppSettings.Load();
        _vm = new MainViewModel(_settings);
        DataContext = _vm;

        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);
        ListColumn.Width = new GridLength(Math.Clamp(_settings.ListWidth, 200, 520));
        if (_settings.Maximized) WindowState = WindowState.Maximized;

        var v = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        SourceInitialized += (_, _) => TintTitleBar();

        InputBindings.Add(new KeyBinding(new RelayCommand(FocusFilter), Key.F, ModifierKeys.Control));
        Loaded += (_, _) => _vm.Start(_startPath);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// Paints the Windows title bar in the menu's colours: dark mode, the
    /// panel black behind, gold text and a bronze border. Windows 10 takes
    /// the dark mode and ignores the colours.
    private void TintTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        void Set(int attr, int value) => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
        Set(20, 1);              // DWMWA_USE_IMMERSIVE_DARK_MODE
        Set(35, 0x000B0D0F);     // DWMWA_CAPTION_COLOR, #0F0D0B as COLORREF
        Set(36, 0x0070B0D1);     // DWMWA_TEXT_COLOR, #D1B070
        Set(34, 0x00395B73);     // DWMWA_BORDER_COLOR, #735B39
    }

    private void FocusFilter()
    {
        _vm.Tab = 0;
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_vm.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.Width = Width;
            _settings.Height = Height;
        }
        _settings.ListWidth = ListColumn.ActualWidth;
        _vm.Shutdown();
        base.OnClosing(e);
    }

    private void Row_Select(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingViewModel s }) _vm.SelectedSetting = s;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu!;
        menu.PlacementTarget = MoreButton;
        // Right edges lined up, so the menu opens into the window rather than off its side.
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popup, target, _) =>
            new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 2),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
        menu.DataContext = _vm;
        menu.IsOpen = true;
    }

    private void File_Changed(object sender, SelectionChangedEventArgs e) => _vm.OnSelectedFileChanged();

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }
}
