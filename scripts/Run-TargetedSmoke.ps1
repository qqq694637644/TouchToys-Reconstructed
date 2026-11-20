[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Get-Location).Path 'artifacts\targeted'),
    [int]$TimeoutSeconds = 420
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

$scenarios = @(
    @{ Name = 'v088'; Argument = '--v088-smoke'; Report = 'v088-targeted-test.json'; Screenshots = $false },
    @{ Name = 'v087'; Argument = '--v087-smoke'; Report = 'v087-targeted-test.json'; Screenshots = $true },
    @{ Name = 'v086'; Argument = '--v086-smoke'; Report = 'v086-targeted-test.json'; Screenshots = $true }
)

foreach ($scenario in $scenarios) {
    $outputDirectory = Join-Path $OutputRoot $scenario.Name
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    $env:TOUCHTOYS_TEST_OUTPUT_DIR = (Resolve-Path -LiteralPath $outputDirectory).Path

    Get-Process -Name 'TouchToys' -ErrorAction SilentlyContinue | Stop-Process -Force
    $arguments = @('--smoke-test', '--capture-ui', $scenario.Argument)
    Write-Host "Running $($scenario.Name) targeted suite..."
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $package.InstallLocation -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $process.Refresh()
    }

    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "$($scenario.Name) targeted suite timed out after $TimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) {
        throw "$($scenario.Name) targeted suite exited with code $($process.ExitCode)."
    }

    $reportPath = Join-Path $outputDirectory $scenario.Report
    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "$($scenario.Name) targeted report was not created: $reportPath"
    }
    $reportText = Get-Content -LiteralPath $reportPath -Raw
    try {
        $report = $reportText | ConvertFrom-Json
    } catch {
        throw "$($scenario.Name) targeted report contains an exception: $reportText"
    }
    if ($report.Result -ne 'PASS') {
        throw "$($scenario.Name) targeted suite did not pass: $reportText"
    }

    $screenshots = Get-ChildItem -LiteralPath $outputDirectory -Recurse -File |
        Where-Object { $_.Extension -in @('.png', '.jpg', '.jpeg', '.webp') }
    if ($scenario.Screenshots -and $screenshots.Count -eq 0) {
        throw "$($scenario.Name) targeted suite passed but produced no screenshots."
    }
    Write-Host "TARGETED_RESULT=$($scenario.Name):PASS; SCREENSHOTS=$($screenshots.Count); REPORT=$reportPath"
}
