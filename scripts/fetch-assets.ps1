$ErrorActionPreference = "Stop"

$dest = Join-Path $PSScriptRoot "..\SoundTheSpire\soundfonts"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$base = "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main"
$files = @{
    "GeneralUser-GS.sf2"         = "$base/GeneralUser-GS.sf2"
    "GeneralUser-GS-LICENSE.txt" = "$base/documentation/LICENSE.txt"
}

foreach ($name in $files.Keys) {
    $target = Join-Path $dest $name
    if (Test-Path $target) {
        Write-Host "exists: $name"
        continue
    }
    Write-Host "downloading: $name"
    Invoke-WebRequest -Uri $files[$name] -OutFile $target
}
