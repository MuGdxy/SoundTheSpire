param(
    [string]$Output = "$env:USERPROFILE\Videos\SoundTheSpire-combat-showcase.mp4",
    [string[]]$Only = @("slimes", "entomancer", "test-subject"),
    [string[]]$ExistingClips = @()
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$scenario = Join-Path $PSScriptRoot "scenario.ps1"
$restart = Join-Path $PSScriptRoot "dev-restart.ps1"

function Invoke-Obs([string]$code) {
    $env:SOUND_THE_SPIRE_OBS_CODE = $code
    $bootstrap = "import os; import obsws_python as obs; c=obs.ReqClient(host='127.0.0.1', port=4455, password=''); exec(os.environ['SOUND_THE_SPIRE_OBS_CODE'])"
    $result = & python -c $bootstrap
    if ($LASTEXITCODE -ne 0) { throw "OBS command failed." }
    return $result
}

function Start-Recording {
    Invoke-Obs @"
c.set_input_settings(
    "Slay the Spire 2 Audio",
    {"window": "Slay the Spire 2:Engine:SlayTheSpire2.exe", "exclude_process_tree": False},
    True,
)
c.set_input_mute("Desktop Audio", False)
c.set_input_volume("Desktop Audio", vol_db=-6.0)
c.set_input_mute("Slay the Spire 2 Audio", False)
c.set_input_volume("Slay the Spire 2 Audio", vol_db=6.0)
c.start_record()
import time
for _ in range(100):
    if c.get_record_status().output_active:
        break
    time.sleep(0.05)
else:
    raise RuntimeError("OBS recording did not become active")
"@ | Out-Null
}

function Stop-Recording {
    $result = Invoke-Obs 'print(c.stop_record().output_path)'
    return ($result -join "").Trim()
}

$segments = @(
    @{ Name = "slimes"; Prepare = "recording-prepare-slimes.txt"; Play = "recording-segment-slimes.txt" },
    @{ Name = "entomancer"; Prepare = "recording-prepare-entomancer.txt"; Play = "recording-segment-entomancer.txt" },
    @{ Name = "test-subject"; Prepare = "recording-prepare-test-subject.txt"; Play = "recording-segment-test-subject.txt" },
    @{ Name = "extra-test-subject"; Prepare = "recording-prepare-test-subject.txt"; Play = "recording-extra-test-subject-drums.txt" }
)

$clips = [System.Collections.Generic.List[string]]::new()
$clips.AddRange($ExistingClips)
try {
    foreach ($segment in $segments | Where-Object { $Only -contains $_.Name }) {
        Write-Host "=== Restarting game for $($segment.Name) ==="
        & $restart
        if ($LASTEXITCODE -ne 0) { throw "Failed to restart game for $($segment.Name)." }

        Write-Host "=== Preparing $($segment.Name) ==="
        & $scenario (Join-Path $PSScriptRoot "scenarios\$($segment.Prepare)")
        if ($LASTEXITCODE -ne 0) { throw "Failed to prepare $($segment.Name)." }

        Write-Host "=== Recording $($segment.Name) ==="
        Start-Recording
        & $scenario (Join-Path $PSScriptRoot "scenarios\$($segment.Play)")
        if ($LASTEXITCODE -ne 0) { throw "Failed to play $($segment.Name)." }
        $clip = Stop-Recording
        $clips.Add($clip)
        Write-Host "Saved $clip"
    }
}
catch {
    try {
        $active = Invoke-Obs 'print(c.get_record_status().output_active)'
        if (($active -join "").Trim() -eq "True") { Stop-Recording | Out-Null }
    }
    catch {}
    throw
}

$inputs = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $clips.Count; $i++) {
    $inputs.Add("-i")
    $inputs.Add($clips[$i])
}
$pads = (0..($clips.Count - 1) | ForEach-Object { "[$($_):v:0][$($_):a:0]" }) -join ""
$filter = "${pads}concat=n=$($clips.Count):v=1:a=1[v][a]"
& ffmpeg -y @inputs -filter_complex $filter -map "[v]" -map "[a]" `
    -c:v h264_nvenc -preset p5 -cq 18 -b:v 0 -c:a aac -b:a 192k -movflags +faststart $Output
if ($LASTEXITCODE -ne 0) { throw "FFmpeg concat failed." }
Write-Host "Final showcase: $Output"
