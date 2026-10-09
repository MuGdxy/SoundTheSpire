# Builds a Release copy of the mod (no debug bridge) into dist/ and zips it for sharing; the installed dev copy in the
# game's mods folder is left untouched. Output: dist/SoundTheSpire-<version>.zip, holding a SoundTheSpire/ folder that
# goes into "Slay the Spire 2/mods/".
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
$version = (Get-Content (Join-Path $root "SoundTheSpire.json") -Raw | ConvertFrom-Json).version

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$modsPath = ($stage -replace "\\", "/") + "/"
dotnet build (Join-Path $root "SoundTheSpire.csproj") -c Release -p:ModsPath=$modsPath --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

$mod = Join-Path $stage "SoundTheSpire"
foreach ($file in "SoundTheSpire.json", "SoundTheSpire.dll", "SoundTheSpire.Hot.dll", "MeltySynth.dll", "Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll", "Tolk-LICENSE.txt", "NVDA-LICENSE.txt", "soundfonts/GeneralUser-GS.sf2", "profiles/waterfall.json", "voices/zhs/manifest.json") {
    if (-not (Test-Path (Join-Path $mod $file))) { throw "Package is missing $file." }
}
Get-ChildItem $mod -Filter *.pdb | Remove-Item
Copy-Item (Join-Path $root "docs/README-player.txt") (Join-Path $mod "README.txt")

$zip = Join-Path $dist "SoundTheSpire-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $mod -DestinationPath $zip
$latestZip = Join-Path $dist "SoundTheSpire-latest.zip"
Copy-Item $zip $latestZip -Force
Write-Host "Packaged: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Get-ChildItem -Recurse $mod | ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) }
