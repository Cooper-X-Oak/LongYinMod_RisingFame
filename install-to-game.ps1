$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = "D:\Users\luoxu\game\steam\steamapps\common\LongYinLiZhiZhuan"
$pluginDll = Join-Path $projectRoot "bin\Release\net6.0\RisingFame.dll"
$pluginDir = Join-Path $gameRoot "BepInEx\plugins"

if (-not (Test-Path $pluginDll)) {
    throw "DLL not found: $pluginDll. Run build.ps1 first."
}

if (-not (Test-Path (Join-Path $gameRoot "BepInEx"))) {
    Write-Warning "BepInEx not found in game directory. Install BepInEx IL2CPP first."
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $pluginDll (Join-Path $pluginDir "RisingFame.dll") -Force

Write-Host "Deployed to: $pluginDir\RisingFame.dll"
