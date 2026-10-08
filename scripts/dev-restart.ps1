# Closes the game, rebuilds and deploys the mod, relaunches through Steam and waits for the debug bridge.
param([int]$TimeoutSeconds = 120)
$ErrorActionPreference = "Stop"

Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | Stop-Process
while (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }

Push-Location (Join-Path $PSScriptRoot "..")
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
} finally {
    Pop-Location
}

Start-Process "steam://rungameid/2868840"

$sts = Join-Path $PSScriptRoot "sts.ps1"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    try {
        $out = & $sts sts_state -ErrorAction Stop
    } catch {
        continue
    }
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Game is up:"
        Write-Host ($out -join "`n")
        exit 0
    }
}
throw "Game did not answer on the debug bridge within $TimeoutSeconds s."
