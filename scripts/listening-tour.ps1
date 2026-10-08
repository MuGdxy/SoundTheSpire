# Restarts the development build, waits for the main menu, then auditions every regular-battle music profile.
param([int]$StartupSeconds = 10)
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "dev-restart.ps1")
if ($LASTEXITCODE -ne 0) { throw "Development restart failed." }

# The debug bridge is available before cloud sync/logo animation finishes; avoid cancelling game startup.
Start-Sleep -Seconds $StartupSeconds

& (Join-Path $PSScriptRoot "scenario.ps1") (Join-Path $PSScriptRoot "scenarios/all-music-tour.txt")
if ($LASTEXITCODE -ne 0) { throw "Listening tour failed." }
