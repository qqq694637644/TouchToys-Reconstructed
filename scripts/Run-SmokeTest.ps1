[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\smoke-x64'),
    [int]$TimeoutSeconds = 300,
    [switch]$IncludeTouch
)

$ErrorActionPreference = 'Stop'

$package = Get-AppxPackage -Name 'TouchToys.Reconstructed' |
    Sort-Object Version -Descending |
    Select-Object -First 1
if ($null -eq $package) {
    throw 'TouchToys.Reconstructed is not installed for the current user.'
}

$executable = Join-Path $package.InstallLocation 'TouchToys.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Installed executable not found: $executable"
}

Remove-Item -LiteralPath $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$env:TOUCHTOYS_TEST_OUTPUT_DIR = (Resolve-Path -LiteralPath $OutputDirectory).Path

$arguments = @('--smoke-test', '--capture-ui')
if ($IncludeTouch) {
    $arguments += '--touch-smoke'
}

Get-Process -Name 'TouchToys' -ErrorAction SilentlyContinue | Stop-Process -Force
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $package.InstallLocation -PassThru
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $process.Refresh()
}

if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Smoke test timed out after $TimeoutSeconds seconds."
}
if ($process.ExitCode -ne 0) {
    throw "TouchToys smoke test exited with code $($process.ExitCode)."
}

$reportPath = Join-Path $OutputDirectory 'smoke-test.json'
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "Smoke test report was not created: $reportPath"
}

$reportText = Get-Content -LiteralPath $reportPath -Raw
try {
    $report = $reportText | ConvertFrom-Json
} catch {
    throw "Smoke test report contains an exception instead of JSON: $reportText"
}
if ($report.Result -ne 'PASS') {
    throw "Smoke test did not pass: $reportText"
}
if ($report.ContinuousScrolling -ne 'PASS' -or $report.EdgeGestureRouting -notlike 'PASS*') {
    throw "Core interaction assertions did not pass: $reportText"
}

if ($IncludeTouch) {
    if ($report.PanelTouchInput -ne 'PASS' -or $report.EdgeTouchInput -ne 'PASS') {
        throw "Touch input assertions did not pass: $reportText"
    }
    $touchReport = Join-Path $OutputDirectory 'touch-smoke.txt'
    if (-not (Test-Path -LiteralPath $touchReport)) {
        throw "Touch smoke report was not created: $touchReport"
    }
    $touchText = Get-Content -LiteralPath $touchReport -Raw
    if ($touchText -notmatch '(?im)^PASS') {
        throw "Touch smoke report did not contain a PASS result: $touchReport"
    }
    if ($touchText -match '(?im)^(FAIL|ERROR)') {
        throw "Touch smoke report contains a failure: $touchReport"
    }
}

$screenshots = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File |
    Where-Object { $_.Extension -in @('.png', '.jpg', '.jpeg', '.webp') }
Write-Host "SMOKE_RESULT=$($report.Result)"
Write-Host "SMOKE_ARCHITECTURE=$($report.Architecture)"
Write-Host "SMOKE_SCREENSHOTS=$($screenshots.Count)"
Write-Host "SMOKE_REPORT=$reportPath"
if ($screenshots.Count -eq 0) {
    throw 'Smoke test passed but did not produce any screenshots.'
}
