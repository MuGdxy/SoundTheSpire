# Runs a scenario file against the running game, one console command per line.
# Besides console commands, a scenario file supports:
#   # comment
#   wait menu | run | combat     poll sts_state until that state is reached
#                                (run waits for the run scene to finish loading, combat also waits for rolled intents)
#   sleep <seconds>
# Example: .\scripts\scenario.ps1 scripts\scenarios\attack-tiers.txt
param(
    [Parameter(Mandatory = $true)][string]$File,
    [int]$TimeoutSeconds = 60
)

$sts = Join-Path $PSScriptRoot "sts.ps1"

function Send([string]$line) {
    $out = & $sts $line
    return @{ Text = ($out -join "`n"); Ok = ($LASTEXITCODE -eq 0) }
}

function Test-State([string]$state, [string]$text) {
    switch ($state) {
        "menu"   { return $text -match "run: False" }
        "run"    { return $text -match "ready: True" }
        "combat" { return ($text -match "combat: True") -and ($text -match "enemy 0") -and ($text -notmatch "UNSET_MOVE") }
        default  { throw "Unknown wait target '$state'" }
    }
}

foreach ($raw in Get-Content $File) {
    $line = $raw.Trim()
    if ($line -eq "" -or $line.StartsWith("#")) { continue }

    if ($line -match "^sleep\s+([\d.]+)$") {
        Start-Sleep -Milliseconds ([int]([double]$Matches[1] * 1000))
        continue
    }

    if ($line -match "^wait\s+(\w+)$") {
        $state = $Matches[1]
        Write-Host "> wait $state"
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        do {
            Start-Sleep -Milliseconds 500
            $r = Send "sts_state"
            if ((Get-Date) -gt $deadline) { Write-Error "Timed out waiting for $state.`n$($r.Text)"; exit 1 }
        } until ($r.Ok -and (Test-State $state $r.Text))
        continue
    }

    Write-Host "> $line"
    $r = Send $line
    Write-Host $r.Text
    if (-not $r.Ok) { Write-Error "Scenario stopped at: $line"; exit 1 }
}
