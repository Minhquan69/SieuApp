[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'iVMS.sln'
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

if (-not (Test-Path $vsWhere)) {
    throw 'Visual Studio Installer was not found. Install Visual Studio 2022 or Build Tools 2022 with .NET desktop development and the .NET Framework 4.8 Targeting Pack.'
}

$msBuild = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msBuild -or -not (Test-Path $msBuild)) {
    throw 'MSBuild was not found. Install the MSBuild component through Visual Studio Installer.'
}

Write-Host "Restoring NuGet packages with $msBuild"
& $msBuild $solution '-t:Restore' '-m' '-p:RestorePackagesConfig=true'
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed with exit code $LASTEXITCODE."
}

if (-not $SkipBuild) {
    Write-Host "Building $Configuration x64"
    & $msBuild $solution '-m' "-p:Configuration=$Configuration" '-p:Platform=x64'
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE. Review the MSBuild output above."
    }
}

Write-Host 'Setup completed successfully.'
