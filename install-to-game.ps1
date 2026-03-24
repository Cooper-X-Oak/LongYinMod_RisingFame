$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = "D:\Users\luoxu\game\steam\steamapps\common\LongYinLiZhiZhuan"
$gameExe = Join-Path $gameRoot "LongYinLiZhiZhuan.exe"
$bepInExDir = Join-Path $gameRoot "BepInEx"
$pluginDll = Join-Path $projectRoot "bin\Release\net6.0\RisingFame.dll"
$pluginDir = Join-Path $bepInExDir "plugins"

if (-not (Test-Path $pluginDll)) {
    throw "DLL not found: $pluginDll. Run build.ps1 first."
}

if (-not (Test-Path -LiteralPath $gameRoot -PathType Container)) {
    throw "Game root does not exist: $gameRoot"
}

if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw "Game exe not found at expected path: $gameExe"
}

if (-not (Test-Path -LiteralPath $bepInExDir -PathType Container)) {
    throw "BepInEx not found in game directory: $bepInExDir. Install BepInEx IL2CPP first."
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $pluginDll (Join-Path $pluginDir "RisingFame.dll") -Force

Write-Host "Deployed to: $pluginDir\RisingFame.dll"
