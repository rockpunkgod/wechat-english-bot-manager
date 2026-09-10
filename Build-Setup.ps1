param(
    [string]$NodePath = 'node',
    [string]$DependenciesPath = '',
    [string]$NodeLicensePath = '',
    [string]$IsccPath = '',
    [switch]$SkipAppBuild,
    [switch]$TestInstaller
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
if (-not $IsccPath) {
    $portable = Join-Path $root 'build\tools\inno-6.7.3\ISCC.exe'
    $systemCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $portable) { $IsccPath = $portable }
    elseif (Test-Path -LiteralPath $systemCompiler) { $IsccPath = $systemCompiler }
    else { throw 'Install Inno Setup 6, or pass -IsccPath to ISCC.exe. See README.' }
}
if (-not $SkipAppBuild) {
    & (Join-Path $root 'Build.ps1') -NodePath $NodePath -DependenciesPath $DependenciesPath -NodeLicensePath $NodeLicensePath -ProgramName '微信英语Bot管理器-用量整合版.exe'
}
$app = Join-Path $root 'release\微信英语Bot管理器-用量整合版.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Application build is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion -ne ($version + '.0')) {
    throw 'Application version does not match package.json; rebuild the application.'
}
$payloadRoot = (Resolve-Path -LiteralPath (Join-Path $root 'build\payload')).Path
$longestEntry = (Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force |
    ForEach-Object { $_.FullName.Length - $payloadRoot.Length - 1 } | Measure-Object -Maximum).Maximum
# Engine adds 71 UTF-16 characters for separators, cache and staging directory.
$maxInstallPath = 259 - 71 - [int]$longestEntry
if ($maxInstallPath -lt 32) { throw 'Payload paths are too long for the desktop runtime.' }
$arguments = @('/Qp', ('/DAppVersion=' + $version), ('/DAppExe=' + $app), ('/DMaxInstallPath=' + $maxInstallPath))
if ($TestInstaller) {
    $testOutput = Join-Path $root 'build\setup-test'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $arguments += @('/DTestBuild=1', ('/O' + $testOutput))
}
& $IsccPath @arguments (Join-Path $root 'installer\manager.iss')
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
if ($TestInstaller) { $setup = Join-Path $testOutput ("WechatEnglishBotManager-TestSetup-$version-x64.exe") }
else { $setup = Join-Path $root ("release\WechatEnglishBotManager-Setup-$version-x64.exe") }
Get-Item -LiteralPath $setup | Select-Object FullName,Length
