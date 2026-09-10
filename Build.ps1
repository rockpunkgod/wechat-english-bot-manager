param([string]$NodePath = 'node', [string]$DependenciesPath = '', [string]$SkillPath = '', [string]$ProgramName = '微信英语Bot管理器.exe', [string]$NodeLicensePath = '')
$ErrorActionPreference = 'Stop'
$botSourceRoot = $PSScriptRoot
if ($DependenciesPath) { $env:BOT_BUILD_DEPS = $DependenciesPath }
if ($SkillPath) { $env:BOT_SKILL_SOURCE = $SkillPath }
if ($NodeLicensePath) { $env:BOT_NODE_LICENSE = $NodeLicensePath }
$nodeCommand = Get-Command $NodePath -ErrorAction Stop
$env:BOT_NODE_SOURCE = $nodeCommand.Source
Push-Location $botSourceRoot
try {
    & $NodePath (Join-Path $botSourceRoot 'build.mjs')
    if ($LASTEXITCODE -ne 0) { throw '引擎打包失败。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payloadPath = Join-Path $botSourceRoot 'build\payload'
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $botSourceRoot 'tests\Verify-Handoff.ps1') -PayloadPath $payloadPath
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell 接管兼容性回归测试失败，已停止打包。' }
    $zipPath = Join-Path $botSourceRoot ('build\engine-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadPath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $releasePath = Join-Path $botSourceRoot 'release'
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    $compilerPath = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if ([IO.Path]::GetFileName($ProgramName) -ne $ProgramName -or [IO.Path]::GetExtension($ProgramName) -ne '.exe') { throw '输出名称必须是当前发布目录中的 EXE 文件名。' }
    $programPath = Join-Path $releasePath $ProgramName
    $programIcon = Join-Path $botSourceRoot 'assets\bot-manager-v1.ico'
    $version = (Get-Content -LiteralPath (Join-Path $botSourceRoot 'package.json') -Raw | ConvertFrom-Json).version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
    $versionSource = Join-Path $botSourceRoot 'build\AssemblyInfo.g.cs'
    [IO.File]::WriteAllText($versionSource, ('using System.Reflection; [assembly: AssemblyTitle("Wechat English Bot Manager")] [assembly: AssemblyVersion("' + $version + '.0")] [assembly: AssemblyFileVersion("' + $version + '.0")]'), [Text.UTF8Encoding]::new($false))
    $csharpFiles = @('Manager.cs', 'UiTheme.cs', 'UsageData.cs', 'UsagePanel.cs', 'tests\UsageTests.cs', 'tests\HandoffTests.cs') | ForEach-Object { Join-Path $botSourceRoot $_ }
    $csharpFiles += $versionSource
    & $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$programIcon" "/win32manifest:$botSourceRoot\app.manifest" "/out:$programPath" "/resource:$zipPath,engine.zip" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $csharpFiles
    if ($LASTEXITCODE -ne 0) { throw 'Windows 程序编译失败。' }
    Get-Item -LiteralPath $programPath | Select-Object FullName,Length,LastWriteTime
} finally { Pop-Location }
