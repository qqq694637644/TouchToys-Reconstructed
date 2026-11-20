[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [ValidateRange(30, 900)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

$package = (Resolve-Path -LiteralPath $PackagePath).Path
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "MSIX package not found: $PackagePath"
}

Write-Host 'Creating ephemeral CI-only signing certificate...'
$certificate = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject 'CN=TouchToys-Reconstructed' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy Exportable `
    -KeyLength 2048 `
    -HashAlgorithm SHA256

$passwordText = [Guid]::NewGuid().ToString('N')
$password = ConvertTo-SecureString $passwordText -AsPlainText -Force
$pfxPath = Join-Path $env:RUNNER_TEMP 'TouchToys-Reconstructed-CI.pfx'
$cerPath = Join-Path $env:RUNNER_TEMP 'TouchToys-Reconstructed-CI.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

$unlockKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
New-Item -Path $unlockKey -Force | Out-Null
New-ItemProperty -Path $unlockKey -Name 'AllowAllTrustedApps' -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $unlockKey -Name 'AllowDevelopmentWithoutDevLicense' -PropertyType DWord -Value 1 -Force | Out-Null

$windowsKits = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Windows Kits\10\bin'
$signtool = Get-ChildItem -LiteralPath $windowsKits -Recurse -File -Filter 'signtool.exe' |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signtool) {
    throw "SignTool.exe was not found under $windowsKits"
}

Write-Host 'Signing the CI MSIX...'
& $signtool.FullName sign /fd SHA256 /f $pfxPath /p $passwordText $package
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE"
}

Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue

$installerScript = Join-Path $env:RUNNER_TEMP 'Install-TouchToys-CI.ps1'
$installerStdout = Join-Path $env:RUNNER_TEMP 'Install-TouchToys-CI.stdout.log'
$installerStderr = Join-Path $env:RUNNER_TEMP 'Install-TouchToys-CI.stderr.log'
$escapedPackage = $package.Replace("'", "''")
$installerContent = @"
`$ErrorActionPreference = 'Stop'
try {
    Add-AppxPackage -Path '$escapedPackage' -ForceApplicationShutdown -ErrorAction Stop
    Write-Output 'APPX_INSTALL_SUCCEEDED'
    exit 0
} catch {
    Write-Error (($_ | Out-String))
    exit 1
}
"@
Set-Content -LiteralPath $installerScript -Value $installerContent -Encoding UTF8

$powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$installerArguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $installerScript + '"'
Write-Host "Installing MSIX (timeout: $TimeoutSeconds seconds)..."
$process = Start-Process -FilePath $powershellExe -ArgumentList $installerArguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $installerStdout -RedirectStandardError $installerStderr

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $process.Refresh()
}

if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $installerStdout) { Get-Content -LiteralPath $installerStdout | Write-Host }
    if (Test-Path -LiteralPath $installerStderr) { Get-Content -LiteralPath $installerStderr | Write-Host }
    throw "Add-AppxPackage timed out after $TimeoutSeconds seconds."
}

if (Test-Path -LiteralPath $installerStdout) { Get-Content -LiteralPath $installerStdout | Write-Host }
if (Test-Path -LiteralPath $installerStderr) { Get-Content -LiteralPath $installerStderr | Write-Host }
if ($process.ExitCode -ne 0) {
    throw "Add-AppxPackage exited with code $($process.ExitCode)."
}

$installed = Get-AppxPackage -Name 'TouchToys.Reconstructed' | Select-Object -First 1
if ($null -eq $installed) {
    throw 'AppX deployment exited successfully but the package is not registered.'
}
Write-Host "Installed package: $($installed.PackageFullName)"
