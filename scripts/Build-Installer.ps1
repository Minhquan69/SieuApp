[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads'),
    [string]$GStreamerRoot,
    [switch]$KeepStage
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$projectRoot = Join-Path $repoRoot 'iVMS'
$installerRoot = Join-Path $repoRoot 'installer'
$stageRoot = Join-Path $installerRoot 'stage'
$appStage = Join-Path $stageRoot 'App'
$gstStage = Join-Path $stageRoot 'GStreamer'
$buildOutput = Join-Path $installerRoot 'build-output'
$gstRoot = if ($GStreamerRoot) { $GStreamerRoot } else { Join-Path $env:ProgramFiles 'gstreamer\1.0\msvc_x86_64' }
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

if (-not (Test-Path $gstRoot)) {
    throw "Không tìm thấy GStreamer x64 tại $gstRoot"
}
if (-not (Test-Path $vsWhere)) {
    throw 'Không tìm thấy Visual Studio Installer/vswhere.exe.'
}
$msBuild = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msBuild) {
    throw 'Không tìm thấy MSBuild.'
}
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Không tìm thấy Inno Setup 6 (ISCC.exe).'
}

if (-not (Test-Path (Join-Path $installerRoot 'prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'))) {
    throw 'Microsoft Edge WebView2 Runtime x64 is missing from installer\prerequisites.'
}

if (Test-Path $stageRoot) {
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    $resolvedInstaller = [IO.Path]::GetFullPath($installerRoot)
    if (-not $resolvedStage.StartsWith($resolvedInstaller, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Đường dẫn stage không an toàn: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
if (Test-Path $buildOutput) { Remove-Item -LiteralPath $buildOutput -Recurse -Force }
New-Item -ItemType Directory -Force $appStage, $gstStage, $buildOutput, $OutputDirectory | Out-Null

Write-Host 'Khôi phục NuGet và build Release x64...'
& $msBuild (Join-Path $repoRoot 'iVMS.sln') '-t:Restore' '-m' '-p:RestorePackagesConfig=true'
if ($LASTEXITCODE -ne 0) { throw "NuGet restore lỗi: $LASTEXITCODE" }
& $msBuild (Join-Path $projectRoot 'iVMS.csproj') '-t:Rebuild' '-m' '-p:Configuration=Release' '-p:Platform=x64' "-p:OutDir=$buildOutput\"
if ($LASTEXITCODE -ne 0) { throw "Build Release failed: $LASTEXITCODE" }
Copy-Item (Join-Path $buildOutput '*') $appStage -Recurse -Force
foreach ($runtimeFile in @('iVMS.exe', 'iVMS.exe.config', 'server_config.json')) {
    if (-not (Test-Path (Join-Path $appStage $runtimeFile))) { throw "Missing runtime file: $runtimeFile" }
}
foreach ($runtimeFile in @('NLog.config', 'Newtonsoft.Json.dll', 'gstreamer-sharp.dll')) {
    if (-not (Test-Path (Join-Path $appStage $runtimeFile))) { throw "Missing application dependency: $runtimeFile" }
}
if ($LASTEXITCODE -ne 0) { throw "Build Release lỗi: $LASTEXITCODE" }

Copy-Item (Join-Path $projectRoot 'icon.ico') $appStage -Force

# Only runtime files are needed. Development archives (.a/.lib) account for
# more than 3 GB and must not be shipped to client machines.
Write-Host 'Đóng gói GStreamer runtime x64...'
Copy-Item (Join-Path $gstRoot 'bin') $gstStage -Recurse -Force
$gstCore = Join-Path $gstStage 'bin\gstreamer-1.0-0.dll'
if (-not (Test-Path $gstCore)) { throw "Missing GStreamer core runtime: $gstCore" }
$pluginStage = Join-Path $gstStage 'lib\gstreamer-1.0'
New-Item -ItemType Directory -Force $pluginStage | Out-Null
Copy-Item (Join-Path $gstRoot 'lib\gstreamer-1.0\*.dll') $pluginStage -Force
if (-not (Get-ChildItem $pluginStage -Filter '*.dll' -ErrorAction SilentlyContinue)) {
    throw "No GStreamer plugins found in $pluginStage"
}

# Playback reads HLS playlists through souphttpsrc.  Its GIO modules provide
# the HTTP proxy/TLS backends and are not located in bin or the plugin folder.
# Without them, Live View (RTSP) can still work while recorded Playback waits
# forever for a playlist on a clean client machine.
$gioModulesSource = Join-Path $gstRoot 'lib\gio\modules'
if (Test-Path $gioModulesSource) {
    $gioModulesStage = Join-Path $gstStage 'lib\gio\modules'
    New-Item -ItemType Directory -Force $gioModulesStage | Out-Null
    Copy-Item (Join-Path $gioModulesSource '*') $gioModulesStage -Force
}
if (Test-Path (Join-Path $gstRoot 'libexec')) {
    Copy-Item (Join-Path $gstRoot 'libexec') $gstStage -Recurse -Force
}
if (Test-Path (Join-Path $gstRoot 'share')) {
    Copy-Item (Join-Path $gstRoot 'share') $gstStage -Recurse -Force
}

# The installed application carries GStreamer beside the executable, so it
# never depends on a drive/path that only exists on the build machine.
$appConfig = Join-Path $appStage 'iVMS.exe.config'
$configText = [IO.File]::ReadAllText($appConfig)
$configText = [Text.RegularExpressions.Regex]::Replace(
    $configText,
    '(<add key="GStreamerRoot_v3" value=")[^"]*("\s*/>)',
    '$1gstreamer$2'
)
[IO.File]::WriteAllText($appConfig, $configText, [Text.UTF8Encoding]::new($false))

Write-Host 'Biên dịch bộ cài...'
$iss = Join-Path $installerRoot 'iVista-VMS.iss'
& $iscc "/DStageDir=$stageRoot" "/DOutputDir=$OutputDirectory" `
    "/DPrerequisiteDir=$(Join-Path $installerRoot 'prerequisites')" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup lỗi: $LASTEXITCODE" }

$setup = Get-ChildItem $OutputDirectory -Filter 'iVMS-Setup-*-x64.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw 'Không tìm thấy file bộ cài sau khi build.' }

$hash = Get-FileHash $setup.FullName -Algorithm SHA256
Write-Host "Bộ cài: $($setup.FullName)"
Write-Host "Kích thước: $([math]::Round($setup.Length / 1MB, 1)) MB"
Write-Host "SHA256: $($hash.Hash)"

if (-not $KeepStage) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path $buildOutput) { Remove-Item -LiteralPath $buildOutput -Recurse -Force }

