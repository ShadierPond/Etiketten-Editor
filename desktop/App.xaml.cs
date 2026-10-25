using System.Windows;

namespace FuniturenEditor.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
            AppDiagnostics.Exception("dispatcher.unhandled", args.Exception);
        try
        {
            AppDiagnostics.Event("app.startup", $"arguments={e.Args.Length}");
            var store = new LocalStore();
            var window = new MainWindow(store, isFirstRun: !store.HasCatalog);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Exception("app.startup", ex);
            MessageBox.Show(
                $"Die lokale Datenbank konnte nicht geöffnet werden.\n\n{ex.Message}",
                "Etiketten Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
