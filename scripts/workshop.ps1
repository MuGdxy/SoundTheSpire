# Publishes to the Steam Workshop with MegaCrit's official uploader (github.com/megacrit/sts2-mod-uploader).
# Builds the Release package, copies it into workshop/content and, with -Upload, runs the uploader. Steam must be
# running and logged in as the publishing account. The first upload creates the item and writes workshop/mod_id.txt
# (commit it: later uploads update that item). Title, description, visibility and change note: workshop/workshop.json.
param([switch]$Upload)
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workspace = Join-Path $root "workshop"
$content = Join-Path $workspace "content"
$uploader = Join-Path $root "tools\ModUploader\ModUploader.exe"

& (Join-Path $PSScriptRoot "package.ps1")
if ($LASTEXITCODE) { throw "Packaging failed." }

if (Test-Path $content) { Remove-Item -Recurse -Force $content }
Copy-Item -Recurse (Join-Path $root "dist\stage\SoundTheSpire") $content

$image = Get-Item (Join-Path $workspace "image.png")
if ($image.Length -ge 1MB) { throw "workshop/image.png must be under 1 MB (is $([math]::Round($image.Length / 1MB, 2)) MB)." }
$config = Get-Content (Join-Path $workspace "workshop.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$manifest = Get-Content (Join-Path $content "SoundTheSpire.json") -Raw | ConvertFrom-Json
$itemId = if (Test-Path (Join-Path $workspace "mod_id.txt")) { (Get-Content (Join-Path $workspace "mod_id.txt")).Trim() } else { "new item" }

Write-Host "Workshop item: $itemId, $($manifest.version), visibility $($config.visibility)"
Write-Host "Change note: $($config.changeNote)"
Get-ChildItem -Recurse $content -File | ForEach-Object { "  {0,10} {1}" -f $_.Length, $_.FullName.Substring($content.Length + 1) }

if (-not $Upload) {
    Write-Host "Dry run. Re-run with -Upload to publish."
    return
}
if (-not (Test-Path $uploader)) {
    throw "Uploader missing; download ModUploader-win-x64.zip from github.com/megacrit/sts2-mod-uploader/releases into tools\ModUploader."
}
& $uploader upload -w $workspace
if ($LASTEXITCODE) { throw "Upload failed; see tools\ModUploader\mod-uploader.log." }
