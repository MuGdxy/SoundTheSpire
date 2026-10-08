# Sound the Spire 一键安装 / 更新 / 卸载。
# 用法（PowerShell 中粘贴）：irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/install.ps1 | iex
# 或双击发布页里的 install.bat。
# 参数（直接运行脚本时）：-ZipPath 本地压缩包  -GamePath 游戏目录  -Uninstall 卸载
# No param() block: piped through iex the file starts with a BOM, which a param() block can't follow.
$ZipPath = ""; $GamePath = ""; $Uninstall = $false
for ($i = 0; $i -lt $args.Count; $i++) {
    switch ($args[$i]) {
        "-ZipPath" { $ZipPath = $args[++$i] }
        "-GamePath" { $GamePath = $args[++$i] }
        "-Uninstall" { $Uninstall = $true }
    }
}
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = "MuGdxy/SoundTheSpire"
$AppId = "2868840"
$GameFolder = "Slay the Spire 2"

function Find-Game {
    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($view in "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall") {
        $key = Join-Path $view "Steam App $AppId"
        $location = (Get-ItemProperty $key -ErrorAction SilentlyContinue).InstallLocation
        if ($location) { $candidates.Add($location) }
    }
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $candidates.Add((Join-Path $steam "steamapps\common\$GameFolder"))
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $library = $match.Groups[1].Value -replace "\\\\", "\"
                $candidates.Add((Join-Path $library "steamapps\common\$GameFolder"))
            }
        }
    }
    $candidates.Add("C:\Program Files (x86)\Steam\steamapps\common\$GameFolder")
    foreach ($path in $candidates) {
        if (Test-Path (Join-Path $path "data_sts2_windows_x86_64")) { return (Resolve-Path $path).Path }
    }
    return $null
}

Write-Host ""
Write-Host "== Sound the Spire 安装程序 ==" -ForegroundColor Cyan

if (-not $GamePath) { $GamePath = Find-Game }
if (-not $GamePath -or -not (Test-Path $GamePath)) {
    Write-Host "没有找到《杀戮尖塔 2》的安装目录。" -ForegroundColor Red
    Write-Host "请在 Steam 里右键游戏 → 管理 → 浏览本地文件，复制地址后输入："
    $GamePath = (Read-Host "游戏目录").Trim('"', ' ')
    if (-not (Test-Path (Join-Path $GamePath "data_sts2_windows_x86_64"))) { throw "这个目录不是《杀戮尖塔 2》的游戏目录：$GamePath" }
}
Write-Host "游戏目录：$GamePath"

while (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($GamePath, [StringComparison]::OrdinalIgnoreCase) }) {
    Read-Host "游戏正在运行，请先关闭游戏，然后按回车继续" | Out-Null
}

$mods = Join-Path $GamePath "mods"
$target = Join-Path $mods "SoundTheSpire"

if ($Uninstall) {
    if (Test-Path $target) { Remove-Item -Recurse -Force $target; Write-Host "已卸载。" -ForegroundColor Green }
    else { Write-Host "没有安装，无需卸载。" }
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("SoundTheSpire-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if (-not $ZipPath) {
        Write-Host "正在查询最新版本……"
        $release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ "User-Agent" = "SoundTheSpire-installer" }
        $asset = $release.assets | Where-Object { $_.name -like "SoundTheSpire-*.zip" } | Select-Object -First 1
        if (-not $asset) { throw "最新发布（$($release.tag_name)）里没有安装包。" }
        $ZipPath = Join-Path $work $asset.name
        Write-Host "正在下载 $($asset.name)（$([math]::Round($asset.size / 1MB, 1)) MB）……"
        Invoke-WebRequest $asset.browser_download_url -OutFile $ZipPath -UseBasicParsing
    }
    $extract = Join-Path $work "extract"
    Expand-Archive -Path $ZipPath -DestinationPath $extract
    $source = Join-Path $extract "SoundTheSpire"
    if (-not (Test-Path (Join-Path $source "SoundTheSpire.json"))) { throw "安装包内容不对：缺少 SoundTheSpire\SoundTheSpire.json。" }

    New-Item -ItemType Directory -Force -Path $mods | Out-Null
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item -Recurse $source $target

    $version = (Get-Content (Join-Path $target "SoundTheSpire.json") -Raw | ConvertFrom-Json).version
    Write-Host ""
    Write-Host "安装完成：Sound the Spire $version" -ForegroundColor Green
    Write-Host "位置：$target"
    Write-Host "启动游戏，若询问是否加载模组，选择加载。按键说明见 $target\README.txt"
}
catch [UnauthorizedAccessException] {
    Write-Host "没有权限写入游戏目录，请右键 install.bat 选择“以管理员身份运行”。" -ForegroundColor Red
    throw
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
