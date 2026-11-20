[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$CiManifest
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\TouchToys\TouchToys.csproj'
$manifestName = if ($CiManifest) { 'AppxManifest.CI.xml' } else { 'AppxManifest.xml' }
$manifestSource = Join-Path $repoRoot "src\TouchToys\Package\$manifestName"
$applicationManifest = Join-Path $repoRoot 'src\TouchToys\app.manifest'
$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot "publish\$Architecture"
$layoutDirectory = Join-Path $artifactRoot "package-layout\$Architecture"
$runtimeIdentifier = "win-$Architecture"

function Invoke-RepoDotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Push-Location -LiteralPath $repoRoot
    try {
        & dotnet @Arguments
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($exitCode -ne 0) {
        throw "dotnet command failed with exit code ${exitCode}: dotnet $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}
if (-not (Test-Path -LiteralPath $manifestSource)) {
    throw "Package manifest not found: $manifestSource"
}
Remove-Item -LiteralPath $publishDirectory, $layoutDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDirectory, $layoutDirectory | Out-Null

Write-Host 'Publishing the original App.xaml text resource (no XBF substitution)...'

Write-Host "Publishing $runtimeIdentifier ($Configuration)..."
$publishArguments = @(
    'publish',
    $project,
    '--configuration', $Configuration,
    '--runtime', $runtimeIdentifier,
    '--self-contained', 'true',
    '--output', $publishDirectory
)
if ($CiManifest) {
    $ciApplicationManifest = Join-Path $artifactRoot 'app.ci.manifest'
    $manifestText = Get-Content -LiteralPath $applicationManifest -Raw
    $manifestText = [regex]::Replace($manifestText, 'uiAccess\s*=\s*["'']true["'']', 'uiAccess="false"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    [System.IO.File]::WriteAllText($ciApplicationManifest, $manifestText, [System.Text.UTF8Encoding]::new($false))
    $publishArguments += "-p:ApplicationManifest=$ciApplicationManifest"
}
Invoke-RepoDotnet -Arguments $publishArguments

$buildOutput = Join-Path $repoRoot "src\TouchToys\bin\$Configuration\net8.0-windows10.0.22621.0\$runtimeIdentifier"
$generatedPri = Join-Path $buildOutput 'TouchToys.pri'
if (-not (Test-Path -LiteralPath $generatedPri)) {
    throw "Windows App SDK resource index not found: $generatedPri"
}
Copy-Item -LiteralPath $generatedPri -Destination (Join-Path $publishDirectory 'TouchToys.pri') -Force

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $layoutDirectory -Recurse -Force
Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $layoutDirectory 'AppxManifest.xml') -Force

$manifestPath = Join-Path $layoutDirectory 'AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if ($null -eq $identity) {
    throw 'Package Identity element is missing from AppxManifest.xml'
}
$identity.SetAttribute('ProcessorArchitecture', $Architecture)
$manifest.Save($manifestPath)

$layoutPri = Join-Path $layoutDirectory 'TouchToys.pri'
$resourcesPri = Join-Path $layoutDirectory 'resources.pri'
if (-not (Test-Path -LiteralPath $layoutPri)) {
    throw "Published resource index missing from package layout: $layoutPri"
}
Move-Item -LiteralPath $layoutPri -Destination $resourcesPri -Force

foreach ($required in @(
    'TouchToys.exe',
    'TouchToys.dll',
    'App.xaml',
    'resources.pri',
    'Assets\Shell\StoreLogo50.png',
    'Assets\Shell\StoreLogo150.png',
    'Assets\Shell\StoreLogo44.png'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $layoutDirectory $required))) {
        throw "Required package file missing: $required"
    }
}

$windowsKits = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem -LiteralPath $windowsKits -Recurse -File -Filter 'makeappx.exe' -ErrorAction Stop |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "MakeAppx.exe was not found under $windowsKits"
}

$packagePath = Join-Path $artifactRoot "TouchToys-Reconstructed-$Architecture.msix"
Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
Write-Host "Packing MSIX with $($makeAppx.FullName)..."
& $makeAppx.FullName pack /d $layoutDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE"
}

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
Write-Host "MSIX_PATH=$packagePath"
Write-Host "MSIX_SHA256=$hash"
