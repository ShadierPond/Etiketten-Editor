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

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        DeviceCodeText.Visibility = Visibility.Collapsed;
        try
        {
            await updater.AuthorizeAsync(
                code =>
                {
                    DeviceCodeText.Text = $"Code: {code}";
                    DeviceCodeText.Visibility = Visibility.Visible;
                },
                status => StatusText.Text = status,
                cancellation.Token);
            StatusText.Text = "GitHub ist verbunden. Die Anmeldung wird geschützt auf diesem Windows-Konto gespeichert.";
            RefreshAuthenticationState();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
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

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        updater.SignOut();
        availableUpdate = null;
        stagedArchive = null;
        InstallButton.IsEnabled = false;
        StatusText.Text = "Von GitHub abgemeldet. Die lokale Anwendung funktioniert weiterhin.";
        RefreshAuthenticationState();
    }

    private void RefreshAuthenticationState()
    {
        var authenticated = updater.IsAuthenticated;
        SignInButton.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.IsEnabled = authenticated;
        if (authenticated)
            StatusText.Text = "Mit GitHub verbunden. Du kannst nach neuen Updates suchen.";
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy && updater.IsAuthenticated;
        SignOutButton.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && stagedArchive is not null;
    }
}
