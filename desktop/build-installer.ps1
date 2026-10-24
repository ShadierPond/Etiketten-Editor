$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'FuniturenEditor.Desktop.csproj'
$publishScript = Join-Path $PSScriptRoot 'publish-windows.ps1'
$installerScript = Join-Path $PSScriptRoot 'installer\FuniturenEditor.iss'
$publishPath = Join-Path $PSScriptRoot 'publish\win-x64'
$outputPath = Join-Path $PSScriptRoot 'installer-output'
$certificateSubject = 'CN=Tariq Alsalem'
$version = ([string]([xml](Get-Content -LiteralPath $projectPath)).Project.PropertyGroup.Version).Trim()

if ([string]::IsNullOrWhiteSpace($version)) {
  throw "Could not read the application version from $projectPath."
}

& $publishScript
if ($LASTEXITCODE -ne 0) {
  throw "Windows publish failed with exit code $LASTEXITCODE."
}

$compilerPaths = @(
  (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
  'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
  'C:\Program Files\Inno Setup 6\ISCC.exe',
  (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$compilerPath = $compilerPaths | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if ($null -eq $compilerPath) {
  throw 'Inno Setup 6 was not found. Install it and run this script again.'
}

& $compilerPath "/DMyAppVersion=$version" "/DSourceDir=$publishPath" $installerScript
if ($LASTEXITCODE -ne 0) {
  throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputPath "FuniturenEditor-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
  throw "The installer was not created at $installerPath."
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

$signature = Set-AuthenticodeSignature -FilePath $installerPath -Certificate $certificate -HashAlgorithm SHA256
if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
  throw "The installer could not be signed with $certificateSubject."
}

Write-Host "Installer: $installerPath"
Write-Host "Publisher: $($certificate.Subject)"
