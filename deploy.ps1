$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = "D:\Users\luoxu\game\steam\steamapps\common\LongYinLiZhiZhuan"
$pluginDll = Join-Path $projectRoot "bin\Release\net6.0\RisingFame.dll"
$gameExe = Join-Path $gameRoot "LongYinLiZhiZhuan.exe"
$bepInExDir = Join-Path $gameRoot "BepInEx"
$pluginDir = Join-Path $bepInExDir "plugins"
$steamAppId = 3202030

# 1. Build
Write-Host "[1/4] Building..." -ForegroundColor Cyan
& (Join-Path $projectRoot ".tools\dotnet\dotnet.exe") build -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    throw "DLL not found after build: $pluginDll"
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

# 2. Kill game if running
$proc = Get-Process -Name "LongYinLiZhiZhuan" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "[2/4] Killing game process..." -ForegroundColor Yellow
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 3
} else {
    Write-Host "[2/4] Game not running" -ForegroundColor Green
}

# 3. Deploy
Write-Host "[3/4] Deploying DLL..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $pluginDll (Join-Path $pluginDir "RisingFame.dll") -Force
Write-Host "  -> $(Join-Path $pluginDir 'RisingFame.dll')"

# 4. Launch via Steam
Write-Host "[4/4] Launching game via Steam..." -ForegroundColor Cyan
Start-Process "steam://rungameid/$steamAppId"

Write-Host "`nDone! Game is starting." -ForegroundColor Green
