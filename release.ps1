<#
.SYNOPSIS
    Builds RPMusicPlayer and packages it into a release zip.

.DESCRIPTION
    Compiles in Release and produces a zip laid out for distribution:

        RPMusicPlayer/
        ├── README.md
        ├── LICENSE
        └── GameData/
            └── RPMusicPlayer/
                ├── RPMusicPlayer.dll
                ├── RPMusicPlayer.cfg
                └── Music/
                    └── README.txt

    Users can unzip this anywhere and copy the GameData folder into their KSP
    install, while the README and LICENSE travel alongside it.

.PARAMETER Version
    Version string used in the zip filename. When given, the zip is named
    RPMusicPlayer-<Version>.zip instead of RPMusicPlayer.zip.

.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.

.EXAMPLE
    ./release.ps1
    ./release.ps1 -Version 1.0.0
#>
param(
    [string]$Version = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'RPMusicPlayer.csproj'

$zipName = if ($Version) { "RPMusicPlayer-$Version.zip" } else { 'RPMusicPlayer.zip' }
$zipPath = Join-Path $projectRoot $zipName

Write-Host "Building $Configuration..." -ForegroundColor Cyan
& dotnet build $projectFile -c $Configuration -v:m
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

$dll = Join-Path $projectRoot "bin\$Configuration\RPMusicPlayer.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build output not found: $dll"
}

# Stage the layout in a temporary release folder, then compress it.
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) "RPMusicPlayer-release-$([guid]::NewGuid().ToString('N'))"
$package = Join-Path $stageRoot 'RPMusicPlayer'

try {
    $pluginDir = Join-Path $package 'GameData\RPMusicPlayer'

    New-Item -ItemType Directory -Force -Path (Join-Path $pluginDir 'Plugins') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $pluginDir 'Music') | Out-Null

    Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir 'Plugins\RPMusicPlayer.dll') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RPMusicPlayer\RPMusicPlayer.cfg') -Destination $pluginDir -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RPMusicPlayer\Music\README.txt') -Destination (Join-Path $pluginDir 'Music\README.txt') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $package -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $package -Force

    Write-Host "Packing $zipName..." -ForegroundColor Cyan
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath

    Write-Host 'Done.' -ForegroundColor Green
    Write-Host "Zip: $zipPath"
} finally {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
