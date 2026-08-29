# Builds the release zip: RavenIron-Undertow-<version>.zip in dist\.
# Ported from RagnaroksWrath, whose version was itself corrected on FireFront's upload day.
#
# Guards the mistakes a hand-made zip invites:
#   1. The THREE places the version lives (plugin const, csproj, manifest.json) drifting apart,
#      so a release can never claim a version its own log denies.
#   2. The store layout. Store files at the ROOT, the DLL under plugins\ — Hexium refuses a
#      root-level DLL.
#   3. The zip writer. PS 5.1's Compress-Archive builds archives Hexium's parser rejects
#      ("No manifest.json found"), while .NET Framework's CreateFromDirectory names nested
#      entries with spec-invalid BACKSLASHES. Entries are written by hand.
#   4. A missing icon. Thunderstore requires a 256x256 PNG and will reject the upload rather
#      than tell you why, so it is checked here instead.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# --- the three versions must agree -------------------------------------------------
$pluginVer   = (Select-String -Path "$root\Undertow\Plugin.cs" -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
$csprojVer   = (Select-String -Path "$root\Undertow\Undertow.csproj" -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$manifestVer = (Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json).version_number

if (($pluginVer -ne $csprojVer) -or ($pluginVer -ne $manifestVer)) {
    Write-Host "VERSION MISMATCH - refusing to package:" -ForegroundColor Red
    Write-Host "  Plugin const : $pluginVer"
    Write-Host "  csproj       : $csprojVer"
    Write-Host "  manifest.json: $manifestVer"
    exit 1
}

# --- the store files must exist ----------------------------------------------------
$required = @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")
$missing = @()
foreach ($f in $required) { if (-not (Test-Path (Join-Path $root $f))) { $missing += $f } }
if ($missing.Count -gt 0) {
    Write-Host "MISSING STORE FILES - refusing to package:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

# Thunderstore rejects an icon that is not exactly 256x256, and does it late and unhelpfully.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile("$root\icon.png")
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
        Write-Host "icon.png is $($icon.Width)x$($icon.Height) - Thunderstore requires exactly 256x256." -ForegroundColor Red
        exit 1
    }
} finally { $icon.Dispose() }

# --- clean Release build -----------------------------------------------------------
dotnet build "$root\Undertow\Undertow.csproj" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

$dll = "$root\Undertow\bin\Release\Undertow.dll"
if (-not (Test-Path $dll)) { Write-Host "No Release DLL at $dll" -ForegroundColor Red; exit 1 }

# --- assemble the zip both stores expect -------------------------------------------
$dist = "$root\dist"
$stage = "$dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item "$root\manifest.json", "$root\README.md", "$root\CHANGELOG.md", "$root\icon.png" -Destination $stage
New-Item -ItemType Directory -Force -Path "$stage\plugins" | Out-Null
Copy-Item $dll -Destination "$stage\plugins"

$zip = "$dist\RavenIron-Undertow-$pluginVer.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $rel,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

Write-Host "Packaged: $zip" -ForegroundColor Green
Get-Item $zip | Select-Object Name, Length
