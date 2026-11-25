param(
    # Defaults to <Version> in Directory.Build.props, so Windows and Linux builds share one number.
    [string]$Version,
    [switch]$SkipTests,
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts 'linux\stage'

# nfpm v2.48.0 (released 2026-10-12). Pinned; bump deliberately.
$nfpmVersion = '2.48.0'

function Invoke-Step([string]$name, [scriptblock]$block) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "$name failed (exit $LASTEXITCODE)" }
}

function Get-Nfpm {
    $dir = Join-Path $root "tools\.bin\nfpm-$nfpmVersion"
    $exe = Join-Path $dir 'nfpm.exe'
    if (Test-Path $exe) { return $exe }

    New-Item -ItemType Directory -Force $dir | Out-Null
    $base = "https://github.com/goreleaser/nfpm/releases/download/v$nfpmVersion"
    $zipName = "nfpm_${nfpmVersion}_Windows_x86_64.zip"
    $zip = Join-Path $dir $zipName
    Invoke-WebRequest "$base/$zipName" -OutFile $zip -UseBasicParsing
    # Saved to a file and read as text: GitHub serves it as binary, so .Content would be a byte array.
    $sumsFile = Join-Path $dir 'checksums.txt'
    Invoke-WebRequest "$base/checksums.txt" -OutFile $sumsFile -UseBasicParsing
    $line = Get-Content $sumsFile | Where-Object { $_ -match ('\s' + [regex]::Escape($zipName) + '$') } | Select-Object -First 1
    Remove-Item $sumsFile -Force
    $expected = if ($line) { ($line -split '\s+')[0] } else { '' }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $expected -or $actual -ne $expected.Trim().ToLowerInvariant()) {
        Remove-Item $zip -Force
        throw "nfpm download checksum mismatch (expected '$expected', got '$actual')."
    }
    Expand-Archive $zip -DestinationPath $dir -Force
    Remove-Item $zip -Force
    return $exe
}

$publish = Join-Path $artifacts 'linux\netbanner'
Remove-Item $publish, $stage -Recurse -Force -ErrorAction SilentlyContinue
# Clean Release output first: incremental trimmed publishes can keep a stale .deps.json that omits newly needed
# framework assemblies (seen with System.Net.Http), which then fail to load at runtime.
foreach ($d in 'bin\Release', 'obj\Release') {
    Remove-Item (Join-Path $root "src\NetBanner.Linux\$d") -Recurse -Force -ErrorAction SilentlyContinue
}
Invoke-Step 'Publish netbanner (linux-x64)' {
    dotnet publish (Join-Path $root 'src\NetBanner.Linux') -c Release -o $publish -p:Version=$Version --nologo
}

Write-Host '==> Stage files' -ForegroundColor Cyan
foreach ($d in 'lib', 'bin', 'gnome-shell\esm', 'gnome-shell\legacy', 'system', 'doc') {
    New-Item -ItemType Directory -Force (Join-Path $stage $d) | Out-Null
}
Remove-Item (Join-Path $publish 'createdump') -ErrorAction SilentlyContinue   # crash-dump helper, not needed
Move-Item (Join-Path $publish 'netbanner') (Join-Path $stage 'bin\netbanner')
Copy-Item (Join-Path $publish '*') (Join-Path $stage 'lib') -Recurse

# GNOME extension: one shared script, wrapped per flavour. Linux line endings throughout.
$gnome = Join-Path $root 'linux\gnome-shell'
$core = (Get-Content -Raw (Join-Path $gnome 'banner-core.js')) -replace "`r`n", "`n"
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $stage 'gnome-shell\legacy\banner.js'), $core, $utf8)
[IO.File]::WriteAllText((Join-Path $stage 'gnome-shell\esm\banner.js'), $core + "`nexport { createBanner };`n", $utf8)
foreach ($flavour in 'esm', 'legacy') {
    foreach ($f in 'extension.js', 'metadata.json') {
        $text = (Get-Content -Raw (Join-Path $gnome "$flavour\$f")) -replace "`r`n", "`n"
        [IO.File]::WriteAllText((Join-Path $stage "gnome-shell\$flavour\$f"), $text, $utf8)
    }
}

$packaging = Join-Path $root 'linux\packaging'
foreach ($f in 'netbanner.service', 'netbanner-sync.service', 'netbanner-autostart.desktop', 'dconf-00-netbanner', 'dconf-lock-netbanner',
               'postinstall.sh', 'preremove.sh', 'postremove.sh') {
    $text = (Get-Content -Raw (Join-Path $packaging $f)) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $stage "system\$f"), $text, $utf8)
}
foreach ($pair in @(@('linux\packaging\config.example.json', 'doc\config.example.json'), @('linux\README.md', 'doc\README.md'))) {
    $text = (Get-Content -Raw (Join-Path $root $pair[0])) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $stage $pair[1]), $text, $utf8)
}
Copy-Item (Join-Path $packaging 'nfpm.yaml') (Join-Path $stage 'nfpm.yaml')
if ($StageOnly) {
    Write-Host "Staged in $stage (packaging skipped)."
    return
}

$nfpm = Get-Nfpm
$env:VERSION = $Version
Push-Location $stage
try {
    foreach ($packager in 'deb', 'rpm') {
        Invoke-Step "Package .$packager" { & $nfpm package --config nfpm.yaml --packager $packager --target $artifacts }
    }
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------- NetBanner server (.deb / .rpm)
$serverPublish = Join-Path $artifacts 'linux\netbanner-server'
$serverStage = Join-Path $artifacts 'linux\server-stage'
Remove-Item $serverPublish, $serverStage -Recurse -Force -ErrorAction SilentlyContinue
foreach ($d in 'bin\Release', 'obj\Release') {
    Remove-Item (Join-Path $root "src\NetBanner.Server\$d") -Recurse -Force -ErrorAction SilentlyContinue
}
Invoke-Step 'Publish netbanner-server (linux-x64)' {
    dotnet publish (Join-Path $root 'src\NetBanner.Server') -c Release -r linux-x64 --self-contained -o $serverPublish -p:Version=$Version --nologo
}

Write-Host '==> Stage server files' -ForegroundColor Cyan
foreach ($d in 'lib', 'bin', 'system', 'doc') { New-Item -ItemType Directory -Force (Join-Path $serverStage $d) | Out-Null }
Remove-Item (Join-Path $serverPublish 'createdump') -ErrorAction SilentlyContinue
Move-Item (Join-Path $serverPublish 'netbanner-server') (Join-Path $serverStage 'bin\netbanner-server')
Copy-Item (Join-Path $serverPublish '*') (Join-Path $serverStage 'lib') -Recurse
$serverSrc = Join-Path $root 'linux\server'
foreach ($f in 'netbanner-server.service', 'preinstall.sh', 'postinstall.sh', 'preremove.sh') {
    $text = (Get-Content -Raw (Join-Path $serverSrc $f)) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $serverStage "system\$f"), $text, $utf8)
}
$text = (Get-Content -Raw (Join-Path $serverSrc 'README.md')) -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $serverStage 'doc\README.md'), $text, $utf8)
Copy-Item (Join-Path $serverSrc 'nfpm.yaml') (Join-Path $serverStage 'nfpm.yaml')

Push-Location $serverStage
try {
    foreach ($packager in 'deb', 'rpm') {
        Invoke-Step "Package server .$packager" { & $nfpm package --config nfpm.yaml --packager $packager --target $artifacts }
    }
}
finally {
    Pop-Location
}

Get-ChildItem $artifacts -File | Where-Object { $_.Name -like "netbanner*$Version*" } |
    Select-Object Name, @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
