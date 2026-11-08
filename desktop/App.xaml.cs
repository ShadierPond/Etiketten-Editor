using System.Windows;

namespace LabelEditor.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ScrollViewerWheelBehavior.Register();
        DispatcherUnhandledException += (_, args) =>
            AppDiagnostics.Exception("dispatcher.unhandled", args.Exception);
        try
        {
            AppDiagnostics.Event("app.startup", $"arguments={e.Args.Length}");
            AppDiagnostics.Event("app.store.create.start");
            var store = new LocalStore();
            AppDiagnostics.Event("app.store.create.complete");
            AppDiagnostics.Event("app.catalog.check.start");
            var isFirstRun = !store.HasCatalog;
            AppDiagnostics.Event("app.catalog.check.complete", $"isFirstRun={isFirstRun}");
            AppDiagnostics.Event("app.window.create.start");
            var window = new MainWindow(store, isFirstRun);
            AppDiagnostics.Event("app.window.create.complete");
            MainWindow = window;
            window.Show();
            AppDiagnostics.Event("app.window.show.complete");
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
