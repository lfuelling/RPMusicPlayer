<#
.SYNOPSIS
    Builds RPMusicPlayer and copies it into a KSP install.

.DESCRIPTION
    Compiles in Release and deploys the plugin dll, the configuration file and the
    music folder into the KSP GameData folder. The music folder is only created, never
    overwritten, so your music is left alone.

.PARAMETER KspDir
    Path to the KSP install. Defaults to the same location the project builds against.

.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -KspDir "D:\Games\KSP" -Configuration Debug
#>
param(
    [string]$KspDir = $env:KSP_DIR,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'RPMusicPlayer.csproj'

if ([string]::IsNullOrWhiteSpace($KspDir)) {
    # Fall back to whatever the project itself is configured with.
    $KspDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
}

if (-not (Test-Path -LiteralPath $KspDir)) {
    throw "KSP folder not found: $KspDir. Pass -KspDir, or set the KSP_DIR environment variable."
}

Write-Host "Building $Configuration..." -ForegroundColor Cyan
& dotnet build $projectFile -c $Configuration -v:m
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

$dll = Join-Path $projectRoot "bin\$Configuration\RPMusicPlayer.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build output not found: $dll"
}

$gameData = Join-Path $KspDir 'GameData'
$target = Join-Path $gameData 'RPMusicPlayer'

Write-Host "Deploying to $target" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path (Join-Path $target 'Plugins') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $target 'Music') | Out-Null

Copy-Item -LiteralPath $dll -Destination (Join-Path $target 'Plugins\RPMusicPlayer.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RPMusicPlayer\RPMusicPlayer.cfg') -Destination $target -Force

Write-Host 'Done.' -ForegroundColor Green
Write-Host "Plugin:   $(Join-Path $target 'Plugins\RPMusicPlayer.dll')"
Write-Host "Music:    $(Join-Path $target 'Music')"