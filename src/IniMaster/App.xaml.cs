using System.Windows;
using System.Windows.Threading;
using IniMaster.Core;

namespace IniMaster;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        var window = new MainWindow(e.Args.FirstOrDefault());
        window.Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.T("Something went wrong:\n\n{0}", e.Exception.Message), "INI Master", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
