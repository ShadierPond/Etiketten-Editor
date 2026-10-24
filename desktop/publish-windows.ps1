$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'FuniturenEditor.Desktop.csproj'
$publishPath = Join-Path $PSScriptRoot 'publish\win-x64'
$archivePath = Join-Path $PSScriptRoot 'publish\FuniturenEditor-win-x64.zip'
$certificateSubject = 'CN=Tariq Alsalem'
$publicCertificatePath = Join-Path $PSScriptRoot 'certificates\FuniturenEditor-Tariq-Alsalem.cer'
$version = ([string]([xml](Get-Content -LiteralPath $projectPath)).Project.PropertyGroup.Version).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
  throw "Could not read the application version from $projectPath."
}

dotnet publish $projectPath `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output $publishPath `
  -p:Version=$version `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
  throw "Windows publish failed with exit code $LASTEXITCODE."
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
  Where-Object {
    $_.Subject -eq $certificateSubject -and
    $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3' -and
    $_.NotAfter -gt (Get-Date)
  } |
  Sort-Object NotAfter -Descending |
  Select-Object -First 1
if ($null -eq $certificate) {
  throw "No valid code-signing certificate for $certificateSubject was found in Cert:\CurrentUser\My."
}
if (-not (Test-Path -LiteralPath $publicCertificatePath)) {
  throw "The public publisher certificate was not found at $publicCertificatePath."
}

$executablePath = Join-Path $publishPath 'FuniturenEditor.Desktop.exe'
$signature = Set-AuthenticodeSignature -FilePath $executablePath -Certificate $certificate -HashAlgorithm SHA256
if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
  throw "The published application could not be signed with $certificateSubject."
}
Copy-Item -LiteralPath $publicCertificatePath -Destination $publishPath -Force

Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $archivePath -Force
Write-Host "Publish complete: $publishPath"
Write-Host "Version: $version"
Write-Host "Publisher: $($certificate.Subject)"
Write-Host "Update package: $archivePath"
