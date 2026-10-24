using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuniturenEditor.Desktop;

internal sealed class GitHubUpdateService
{
    private const string ClientId = "Iv23lilyRMBabzNQezH4";
    private const string Owner = "ShadierPond";
    private const string Repository = "Etiketten-Editor";
    private const string ApplicationName = "FuniturenEditor";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ShadierPond.FuniturenEditor.GitHubUpdates.v1");
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string tokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShadierPond", ApplicationName, "Updates", "github-token.bin");

    public bool IsAuthenticated => File.Exists(tokenPath);

    public async Task<string> AuthorizeAsync(
        Action<string> showCode,
        Action<string> updateStatus,
        CancellationToken cancellationToken)
    {
        using var deviceRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId
            })
        };
        deviceRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var deviceResponse = await Http.SendAsync(deviceRequest, cancellationToken);
        await EnsureSuccessAsync(deviceResponse, "GitHub-Geräteanmeldung starten");
        var device = await deviceResponse.Content.ReadFromJsonAsync<GitHubDeviceCode>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("GitHub hat keine gültigen Anmeldedaten zurückgegeben.");

        showCode(device.UserCode);
        Process.Start(new ProcessStartInfo(device.VerificationUri) { UseShellExecute = true });
        updateStatus("Warte auf die Bestätigung im Browser …");

        var interval = Math.Max(device.Interval, 5);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = device.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                })
            };
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var tokenResponse = await Http.SendAsync(tokenRequest, cancellationToken);
            await EnsureSuccessAsync(tokenResponse, "GitHub-Anmeldung prüfen");
            var token = await tokenResponse.Content.ReadFromJsonAsync<DeviceToken>(JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("GitHub hat keine gültige Token-Antwort zurückgegeben.");

            switch (token.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "access_denied":
                    throw new InvalidOperationException("Die GitHub-Anmeldung wurde abgelehnt.");
                case "expired_token":
                    throw new InvalidOperationException("Der GitHub-Anmeldecode ist abgelaufen. Bitte erneut versuchen.");
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidDataException($"GitHub hat kein Zugriffstoken geliefert{FormatError(token.ErrorDescription)}.");

            SaveToken(token.AccessToken, token.RefreshToken, token.ExpiresIn);
            return token.AccessToken;
        }

        throw new TimeoutException("Der GitHub-Anmeldecode ist abgelaufen. Bitte erneut versuchen.");
    }

    public async Task<UpdateRelease> GetLatestReleaseAsync(string? token, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest",
            token);
        using var response = await Http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Neuestes Update abrufen");
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("GitHub hat keine gültigen Release-Daten zurückgegeben.");
        if (string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
            throw new InvalidDataException("Das GitHub-Release enthält keine Version oder Release-Seite.");

        var asset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "FuniturenEditor-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            throw new InvalidDataException("Das neueste Release enthält nicht das Paket FuniturenEditor-win-x64.zip.");

        return new UpdateRelease(release.TagName, release.Name, release.HtmlUrl, asset.ApiUrl, asset.Digest);
    }

    public async Task<string> DownloadAndStageAsync(
        UpdateRelease release,
        string? token,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, release.AssetApiUrl, token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "Update-Paket herunterladen");

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadierPond", ApplicationName, "Updates");
        Directory.CreateDirectory(updateDirectory);
        var archivePath = Path.Combine(updateDirectory, $"update-{Guid.NewGuid():N}.zip");
        await using (var output = File.Create(archivePath))
            await response.Content.CopyToAsync(output, cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(release.Digest))
            {
                await using var archiveFile = File.OpenRead(archivePath);
                var actualDigest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(archiveFile, cancellationToken))
                    .ToLowerInvariant();
                if (!string.Equals(actualDigest, release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Die Prüfsumme des Update-Pakets stimmt nicht mit GitHub überein.");
            }

            using var archive = ZipFile.OpenRead(archivePath);
            var executable = archive.GetEntry("FuniturenEditor.Desktop.exe");
            if (executable is null || executable.Length == 0)
                throw new InvalidDataException("Das Update-Paket enthält keine gültige FuniturenEditor.Desktop.exe.");

            ValidateArchivePaths(archive, updateDirectory);
            return archivePath;
        }
        catch
        {
            File.Delete(archivePath);
            throw;
        }
    }

    public void StartInstaller(string archivePath, int processId)
    {
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new InvalidOperationException("Der Speicherort der laufenden Anwendung konnte nicht bestimmt werden.");

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $archivePath = '{{EscapePowerShell(archivePath)}}'
            $installDirectory = '{{EscapePowerShell(installDirectory)}}'
            $executablePath = '{{EscapePowerShell(executablePath)}}'
            $processId = {{processId}}
            $workDirectory = Join-Path $env:TEMP ('FuniturenEditorUpdate-' + [guid]::NewGuid().ToString('N'))
            $logDirectory = Join-Path $env:LOCALAPPDATA 'ShadierPond\FuniturenEditor\Updates'
            $logPath = Join-Path $logDirectory 'install.log'
            try {
                New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
                $deadline = (Get-Date).AddSeconds(300)
                while ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
                    if ((Get-Date) -ge $deadline) {
                        throw 'Das Programm wurde nicht innerhalb von fünf Minuten beendet.'
                    }
                    Start-Sleep -Milliseconds 200
                }
                New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null
                Expand-Archive -LiteralPath $archivePath -DestinationPath $workDirectory -Force
                if (-not (Test-Path -LiteralPath (Join-Path $workDirectory 'FuniturenEditor.Desktop.exe'))) {
                    throw 'Das Update-Paket enthält keine Programmdatei.'
                }
                Copy-Item -Path (Join-Path $workDirectory '*') -Destination $installDirectory -Recurse -Force
                Start-Process -FilePath $executablePath
                Remove-Item -LiteralPath $archivePath -Force
                Remove-Item -LiteralPath $workDirectory -Recurse -Force
                Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) Update installed successfully."
            }
            catch {
                New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
                Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) $($_.Exception.ToString())"
                Add-Type -AssemblyName PresentationFramework
                [System.Windows.MessageBox]::Show(
                    "Das Update konnte nicht installiert werden.`n`n$($_.Exception.Message)`n`nDetails: $logPath",
                    'Funituren Editor - Update',
                    'OK',
                    'Error'
                ) | Out-Null
                exit 1
            }
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedScript}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var savedToken = LoadSavedToken();
        if (savedToken is null)
            return null;
        if (savedToken.ExpiresAt is null || savedToken.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return savedToken.AccessToken;
        if (string.IsNullOrWhiteSpace(savedToken.RefreshToken))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = savedToken.RefreshToken
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "GitHub-Anmeldung erneuern");
        var refreshed = await response.Content.ReadFromJsonAsync<DeviceToken>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("GitHub hat keine gültige Token-Erneuerung zurückgegeben.");
        if (!string.IsNullOrWhiteSpace(refreshed.Error) || string.IsNullOrWhiteSpace(refreshed.AccessToken))
            throw new InvalidOperationException(
                $"Die GitHub-Anmeldung ist abgelaufen; bitte erneut verbinden{FormatError(refreshed.ErrorDescription)}.");

        SaveToken(refreshed.AccessToken, refreshed.RefreshToken, refreshed.ExpiresIn);
        return refreshed.AccessToken;
    }

    public void SignOut()
    {
        if (File.Exists(tokenPath))
            File.Delete(tokenPath);
    }

    private SavedGitHubToken? LoadSavedToken()
    {
        if (!File.Exists(tokenPath))
            return null;
        var protectedToken = File.ReadAllBytes(tokenPath);
        var tokenData = ProtectedData.Unprotect(protectedToken, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<SavedGitHubToken>(tokenData, JsonOptions)
            ?? throw new InvalidDataException("Die lokal gespeicherte GitHub-Anmeldung ist beschädigt. Bitte abmelden und erneut verbinden.");
    }

    private void SaveToken(string accessToken, string? refreshToken, int? expiresIn)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
        var savedToken = new SavedGitHubToken(
            accessToken,
            refreshToken,
            expiresIn is null ? null : DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value));
        var serialized = JsonSerializer.SerializeToUtf8Bytes(savedToken, JsonOptions);
        var protectedToken = ProtectedData.Protect(serialized, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(tokenPath, protectedToken);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FuniturenEditor-Updater/1.0");
        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync();
        if (detail.Length > 400)
            detail = detail[..400];
        throw new HttpRequestException(
            $"{operation} fehlgeschlagen (HTTP {(int)response.StatusCode} {response.ReasonPhrase}). {detail}",
            null,
            response.StatusCode);
    }

    private static void ValidateArchivePaths(ZipArchive archive, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Das Update-Paket enthält einen ungültigen Dateipfad.");
        }
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    public static bool IsNewerVersion(string releaseTag, Version? currentVersion)
    {
        var normalizedTag = releaseTag.TrimStart('v', 'V');
        if (!Version.TryParse(normalizedTag, out var releaseVersion))
            throw new InvalidDataException($"Die Release-Version '{releaseTag}' ist kein gültiges Versionsformat.");
        if (currentVersion is null)
            throw new InvalidDataException("Die installierte Programmversion konnte nicht bestimmt werden.");
        return releaseVersion > currentVersion;
    }

    private static string FormatError(string? description) =>
        string.IsNullOrWhiteSpace(description) ? "" : $": {description}";

    private sealed record GitHubDeviceCode(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval);

    private sealed record DeviceToken(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[] Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("url")] string ApiUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}

internal sealed record SavedGitHubToken(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

internal sealed record UpdateRelease(
    string Version,
    string? Name,
    string HtmlUrl,
    string AssetApiUrl,
    string? Digest);
