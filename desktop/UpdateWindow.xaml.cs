using System.Diagnostics;
using System.Windows;

namespace FuniturenEditor.Desktop;

public partial class UpdateWindow : Window
{
    private readonly GitHubUpdateService updater = new();
    private readonly CancellationTokenSource cancellation = new();
    private UpdateRelease? availableUpdate;
    private string? stagedArchive;

    public UpdateWindow(bool checkForUpdatesOnOpen = false)
    {
        InitializeComponent();
        RefreshAuthenticationState();
        Closed += (_, _) => cancellation.Cancel();
        if (checkForUpdatesOnOpen)
            Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        SetBusy(true);
        availableUpdate = null;
        InstallButton.IsEnabled = false;
        try
        {
            var token = await updater.GetTokenAsync(cancellation.Token);
            if (token is null)
            {
                StatusText.Text = "Die GitHub-Anmeldung ist abgelaufen oder fehlt. Bitte erneut mit GitHub verbinden.";
                return;
            }

            StatusText.Text = "Suche nach dem neuesten Release …";
            availableUpdate = await updater.GetLatestReleaseAsync(token, cancellation.Token);
            var currentVersion = typeof(UpdateWindow).Assembly.GetName().Version;
            if (!GitHubUpdateService.IsNewerVersion(availableUpdate.Version, currentVersion))
            {
                StatusText.Text = $"Du verwendest bereits die neueste Version (installiert: {currentVersion?.ToString(3)}, Release: {availableUpdate.Version}).";
                availableUpdate = null;
                return;
            }

            StatusText.Text = $"Update {availableUpdate.Version} ist verfügbar. Paket wird vorbereitet …";
            stagedArchive = await updater.DownloadAndStageAsync(availableUpdate, token, cancellation.Token);
            StatusText.Text = $"Version {availableUpdate.Version} ist bereit. Das Programm wird beim Installieren geschlossen und danach neu gestartet.";
            InstallButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            availableUpdate = null;
            stagedArchive = null;
            StatusText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null || stagedArchive is null)
            return;

        try
        {
            updater.StartInstaller(stagedArchive, Environment.ProcessId);
            Application.Current.MainWindow?.Close();
            Close();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Der Installer konnte nicht gestartet werden: {exception.Message}";
        }
    }

    private void RefreshAuthenticationState()
    {
        CheckButton.IsEnabled = true;
    }

    private void SetBusy(bool busy)
    {
        CheckButton.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && stagedArchive is not null;
    }
}
