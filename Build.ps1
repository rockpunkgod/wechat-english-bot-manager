param([string]$NodePath = 'node', [string]$DependenciesPath = '', [string]$SkillPath = '')
$ErrorActionPreference = 'Stop'
$botSourceRoot = $PSScriptRoot
if ($DependenciesPath) { $env:BOT_BUILD_DEPS = $DependenciesPath }
if ($SkillPath) { $env:BOT_SKILL_SOURCE = $SkillPath }
$nodeCommand = Get-Command $NodePath -ErrorAction Stop
$env:BOT_NODE_SOURCE = $nodeCommand.Source
Push-Location $botSourceRoot
try {
    & $NodePath (Join-Path $botSourceRoot 'build.mjs')
    if ($LASTEXITCODE -ne 0) { throw '引擎打包失败。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payloadPath = Join-Path $botSourceRoot 'build\payload'
    $zipPath = Join-Path $botSourceRoot ('build\engine-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadPath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $releasePath = Join-Path $botSourceRoot 'release'
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    $compilerPath = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $programPath = Join-Path $releasePath '微信英语Bot管理器.exe'
    & $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32manifest:$botSourceRoot\app.manifest" "/out:$programPath" "/resource:$zipPath,engine.zip" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $botSourceRoot 'Manager.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Windows 程序编译失败。' }
    Get-Item -LiteralPath $programPath | Select-Object FullName,Length,LastWriteTime
} finally { Pop-Location }
