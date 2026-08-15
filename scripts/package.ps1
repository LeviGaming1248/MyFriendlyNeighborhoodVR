[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

& (Join-Path $PSScriptRoot 'build.ps1') -GameDir $GameDir -Configuration Release

$stagingRoot = Join-Path $repoRoot "artifacts\MFNVR-v$Version-mod-only"
$zipPath = "$stagingRoot.zip"
$managedOutput = Join-Path $repoRoot 'artifacts\managed'
$nativeDll = Join-Path $repoRoot 'native\build\Release\MFNOpenXR.dll'

if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    $nativeDll = Join-Path $repoRoot 'native\build\MFNOpenXR.dll'
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$pluginDir = Join-Path $stagingRoot 'BepInEx\plugins'
$configDir = Join-Path $stagingRoot 'BepInEx\config'
$nativeDir = Join-Path $stagingRoot 'My Friendly Neighborhood_Data\Plugins'
New-Item -ItemType Directory -Path $pluginDir, $configDir, $nativeDir -Force | Out-Null

$patchedCore = Join-Path $managedOutput 'MFNVR-patched.dll'
if (-not (Test-Path -LiteralPath $patchedCore -PathType Leaf)) {
    throw 'The patched MFNVR core is missing. Run scripts/build.ps1 before packaging.'
}
Copy-Item -LiteralPath $patchedCore -Destination (Join-Path $pluginDir 'MFNVR.dll')
Copy-Item -LiteralPath (Join-Path $managedOutput 'MFNVRConfig.dll') -Destination $pluginDir
Copy-Item -LiteralPath (Join-Path $managedOutput 'MFNVRRenderBridge.dll') -Destination $pluginDir
Copy-Item -LiteralPath $nativeDll -Destination $nativeDir

$defaultConfig = Join-Path $repoRoot 'config\MFNVR.cfg'
if (Test-Path -LiteralPath $defaultConfig -PathType Leaf) {
    Copy-Item -LiteralPath $defaultConfig -Destination $configDir
}

$archiveItems = Get-ChildItem -LiteralPath $stagingRoot -Force
Compress-Archive -LiteralPath $archiveItems.FullName -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"
