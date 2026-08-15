[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$assemblyPath = Join-Path $gameRoot 'My Friendly Neighborhood_Data\Managed\Assembly-CSharp.dll'
$bepInExPath = Join-Path $gameRoot 'BepInEx\core\BepInEx.dll'

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'Assembly-CSharp.dll was not found. GameDir must point to the My Friendly Neighborhood installation.'
}

if (-not (Test-Path -LiteralPath $bepInExPath -PathType Leaf)) {
    throw 'BepInEx 5 was not found in the game directory. Install BepInEx before building MFNVR.'
}

$dotnetHome = Join-Path $repoRoot '.dotnet'
$env:DOTNET_CLI_HOME = $dotnetHome

Write-Host 'Building managed MFNVR plugins...'
& dotnet build (Join-Path $repoRoot 'MFNVR.slnx') -c $Configuration "-p:MFNGameDir=$gameRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Managed build failed with exit code $LASTEXITCODE."
}

$patcherProject = Join-Path $repoRoot 'tools\CoreCameraPatcher\CoreCameraPatcher.csproj'
$patcherOutput = Join-Path $repoRoot 'tools\CoreCameraPatcher\bin\Release\CoreCameraPatcher.exe'
$unpatchedCore = Join-Path $repoRoot 'artifacts\managed\MFNVR.dll'
$patchedCore = Join-Path $repoRoot 'artifacts\managed\MFNVR-patched.dll'

Write-Host 'Building and applying the required MFNVR camera/render-bridge patch...'
& dotnet build $patcherProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Core camera patcher build failed with exit code $LASTEXITCODE."
}
if (Test-Path -LiteralPath $patchedCore) {
    Remove-Item -LiteralPath $patchedCore -Force
}
& $patcherOutput $unpatchedCore $patchedCore
if ($LASTEXITCODE -ne 0) {
    throw "Core camera patching failed with exit code $LASTEXITCODE."
}

$nativeSource = Join-Path $repoRoot 'native'
$nativeBuild = Join-Path $nativeSource 'build'

Write-Host 'Configuring native OpenXR bridge...'
& cmake -S $nativeSource -B $nativeBuild -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) {
    throw "Native CMake configuration failed with exit code $LASTEXITCODE."
}

Write-Host 'Building native OpenXR bridge...'
& cmake --build $nativeBuild --config $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Build complete.'
Write-Host "Managed output: $(Join-Path $repoRoot 'artifacts\managed')"
Write-Host "Install MFNVR-patched.dll as BepInEx\plugins\MFNVR.dll."
Write-Host "Native output:  $nativeBuild"
