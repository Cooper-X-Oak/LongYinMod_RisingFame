$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolsDir = Join-Path $projectRoot ".tools"
$dotnetDir = Join-Path $toolsDir "dotnet"
$dotnetExe = Join-Path $dotnetDir "dotnet.exe"
$installScript = Join-Path $toolsDir "dotnet-install.ps1"

New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null

if (-not (Test-Path $installScript)) {
    Invoke-WebRequest -UseBasicParsing -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
}

if (-not (Test-Path $dotnetExe)) {
    powershell -ExecutionPolicy Bypass -File $installScript -Version "8.0.419" -InstallDir $dotnetDir -NoPath
}

& $dotnetExe restore (Join-Path $projectRoot "LongYinLiteMod.csproj")
& $dotnetExe build (Join-Path $projectRoot "LongYinLiteMod.csproj") -c Release
