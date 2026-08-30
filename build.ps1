$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagesRoot = Join-Path $projectRoot 'packages'
$thirdPartyRoot = Join-Path $projectRoot 'third_party\LibreHardwareMonitor-v0.9.6'
$thirdPartyArchive = Join-Path $projectRoot 'third_party\LibreHardwareMonitor-v0.9.6.zip'
$nuget = Join-Path $env:TEMP 'turzx-nuget.exe'
$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "未找到 .NET Framework MSBuild：$msbuild"
}

if (-not (Test-Path -LiteralPath $nuget)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
}

& $nuget install (Join-Path $projectRoot 'packages.config') `
    -OutputDirectory $packagesRoot -NonInteractive
if ($LASTEXITCODE -ne 0) { throw "NuGet 依赖恢复失败：$LASTEXITCODE" }

if (-not (Test-Path -LiteralPath $thirdPartyRoot)) {
    if (-not (Test-Path -LiteralPath $thirdPartyArchive)) {
        throw "缺少 LibreHardwareMonitor 归档：$thirdPartyArchive"
    }
    New-Item -ItemType Directory -Path $thirdPartyRoot -Force | Out-Null
    Expand-Archive -LiteralPath $thirdPartyArchive -DestinationPath $thirdPartyRoot -Force
}

& $msbuild (Join-Path $projectRoot 'src\Turzx.Wpf\Turzx.Wpf.csproj') `
    /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "TURZX Pro 构建失败：$LASTEXITCODE" }

& $msbuild (Join-Path $projectRoot 'src\Turzx.SensorService\Turzx.SensorService.csproj') `
    /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "传感器服务构建失败：$LASTEXITCODE" }

$output = Join-Path $projectRoot 'artifacts\wpf'
$lhmLicense = Join-Path $output 'licenses\LibreHardwareMonitor-v0.9.6'
$wpfUiLicense = Join-Path $output 'licenses\WPF-UI-4.3.0'
New-Item -ItemType Directory -Path $lhmLicense,$wpfUiLicense -Force | Out-Null
Copy-Item (Join-Path $projectRoot 'third_party\LibreHardwareMonitor-LICENSE.txt') `
    (Join-Path $lhmLicense 'LICENSE') -Force
Copy-Item (Join-Path $projectRoot 'third_party\LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt') `
    (Join-Path $lhmLicense 'THIRD-PARTY-NOTICES.txt') -Force
Copy-Item (Join-Path $packagesRoot 'WPF-UI.4.3.0\LICENSE.md') $wpfUiLicense -Force
Copy-Item (Join-Path $packagesRoot 'WPF-UI.4.3.0\ThirdPartyNotices.txt') $wpfUiLicense -Force

Write-Host "正式版构建完成：$output\Turzx.ControlPanel.Wpf.exe"
