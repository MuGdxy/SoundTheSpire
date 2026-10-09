# Rebuilds SoundTheSpire.Hot, copies encounter voice assets and swaps it into the running game; the current run and
# combat stay as they are.
# Optionally runs commands afterwards, e.g.: .\scripts\hot.ps1 "sts_intents"
# Changes to the loader (SoundTheSpireCode/) are not picked up; those still need scripts/dev-restart.ps1.
param([string[]]$Then = @())
$ErrorActionPreference = "Stop"

$root = Join-Path $PSScriptRoot ".."
$sts = Join-Path $PSScriptRoot "sts.ps1"

dotnet build (Join-Path $root "SoundTheSpire.Hot/SoundTheSpire.Hot.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Hot module build failed." }

$deployedLoader = Get-Item "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\SoundTheSpire\SoundTheSpire.dll"
$changedLoaderSources = Get-ChildItem (Join-Path $root "SoundTheSpireCode") -Recurse -Filter *.cs |
    Where-Object { $_.LastWriteTime -gt $deployedLoader.LastWriteTime }
if ($changedLoaderSources) {
    Write-Warning "Loader sources changed since deploy ($($changedLoaderSources.Name -join ', ')); run scripts/dev-restart.ps1 to apply them."
}

& $sts sts_reload
if ($LASTEXITCODE -ne 0) { exit 1 }
foreach ($command in $Then) {
    & $sts $command
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
