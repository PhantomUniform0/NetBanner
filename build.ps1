<#
.SYNOPSIS
  Builds NetBanner: runs tests, publishes both exes (self-contained, trimmed, single-file) and builds the MSI.

.EXAMPLE
  ./build.ps1                     # version from Directory.Build.props
  ./build.ps1 -Version 3.0.1      # override the version
  ./build.ps1 -SkipTests

  Output: artifacts\publish\NetBanner.exe, NetBannerConfig.exe and artifacts\NetBanner-<version>-x64.msi
#>
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

if (-not $SkipTests) {
    Invoke-Step 'Tests' { dotnet test --project "$root\tests\NetBanner.Core.Tests" -c Release }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# Start the published projects from clean Release output: incremental trimmed publishes can keep a stale .deps.json
# that omits newly needed framework assemblies (seen with System.Net.Http), which then fail to load at runtime.
foreach ($p in 'NetBanner.App', 'NetBanner.Config') {
    foreach ($d in 'bin\Release', 'obj\Release') {
        Remove-Item (Join-Path $root "src\$p\$d") -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Step 'Publish NetBanner.exe' {
    dotnet publish "$root\src\NetBanner.App" -c Release -o $publish @versionArgs
}
Invoke-Step 'Publish NetBannerConfig.exe' {
    dotnet publish "$root\src\NetBanner.Config" -c Release -o $publish @versionArgs
}

# Only the exes ship; drop symbol files that native packages copy alongside.
Get-ChildItem $publish -Filter *.pdb | Remove-Item

Invoke-Step 'Build MSI' {
    dotnet build "$root\installer\NetBanner.Installer.wixproj" -c Release -o "$root\artifacts" "-p:PublishDir=$publish" @versionArgs
}

Write-Host ''
Get-ChildItem "$publish\*.exe", "$root\artifacts\*.msi" |
    Select-Object Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize
