param([Parameter(Mandatory=$true)][string]$SetupPath, [string]$TestRootParent = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$setup = Get-Item -LiteralPath $SetupPath
if ($setup.Name -notmatch '^WechatEnglishBotManager-TestSetup-\d+\.\d+\.\d+-x64\.exe$' -or
    $setup.DirectoryName -ine (Join-Path $repoRoot 'build\setup-test')) {
    throw 'Only the isolated test installer may be tested automatically.'
}
$testRoot = Join-Path $TestRootParent ('wb-setup-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$installRoot = Join-Path $testRoot 'install'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
function Run-Installer([string]$Path, [string[]]$Arguments, [switch]$ExpectFailure) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'Installer exceeded one minute; inspect its log before retrying.' }
    if ($ExpectFailure) {
        if ($process.ExitCode -eq 0) { throw 'Invalid installation path was unexpectedly accepted.' }
    } elseif ($process.ExitCode -ne 0) { throw ('Installer failed: ' + $process.ExitCode) }
}
$argsInstall = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $installRoot + '"'))
Run-Installer $setup.FullName ($argsInstall + ('/LOG="' + (Join-Path $testRoot 'install.log') + '"'))
$installedApp = Join-Path $installRoot '微信英语Bot管理器-用量整合版.exe'
$expectedHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'release\微信英语Bot管理器-用量整合版.exe')).Hash
if ((Get-FileHash -LiteralPath $installedApp).Hash -ne $expectedHash) { throw 'Installed application hash mismatch.' }
$selfTestRoot = Join-Path $testRoot 'run\data'
$appProcess = Start-Process -FilePath $installedApp -ArgumentList @('--self-test', ('"' + $selfTestRoot + '"')) -WindowStyle Hidden -PassThru
if (-not $appProcess.WaitForExit(60000)) { throw 'Installed application self-test timed out.' }
if ($appProcess.ExitCode -ne 0) { throw 'Installed application self-test failed.' }
$selfTest = Get-Content -LiteralPath (Join-Path $selfTestRoot 'self-test.json') -Raw | ConvertFrom-Json
if (-not $selfTest.ok) { throw 'Installed application did not pass its self-test.' }
# Synthetic sentinels only; no real Bot is started.
$sentinels = @('data\manager.json','data\bots\fixture\.runtime\usage.json','程序文件\engine-fixture\sentinel.txt')
foreach ($relative in $sentinels) {
    $file = Join-Path $installRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    [IO.File]::WriteAllText($file, 'INSTALLER-TEST-PRESERVE', [Text.UTF8Encoding]::new($false))
}
Run-Installer $setup.FullName ($argsInstall + ('/LOG="' + (Join-Path $testRoot 'upgrade.log') + '"'))
foreach ($relative in $sentinels) {
    if ([IO.File]::ReadAllText((Join-Path $installRoot $relative)) -ne 'INSTALLER-TEST-PRESERVE') { throw 'Upgrade changed data.' }
}
Run-Installer (Join-Path $installRoot 'uninstall\unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"'))
if (Test-Path -LiteralPath $installedApp) { throw 'Uninstall did not remove the installed application.' }
foreach ($relative in $sentinels) {
    if ([IO.File]::ReadAllText((Join-Path $installRoot $relative)) -ne 'INSTALLER-TEST-PRESERVE') { throw 'Uninstall changed retained data.' }
}
$longRoot = Join-Path $testRoot ('long-' + ('x' * 110))
Run-Installer $setup.FullName @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $longRoot + '"'),('/LOG="' + (Join-Path $testRoot 'long-path.log') + '"')) -ExpectFailure
if (Test-Path -LiteralPath (Join-Path $longRoot '微信英语Bot管理器-用量整合版.exe')) { throw 'Rejected path was modified.' }
[pscustomobject]@{ ok=$true; tests=@('fresh install matches application hash','installed application self-test','same-directory upgrade preserves data','uninstall preserves data and engine cache','overlong destination rejected before installation'); applicationCases=$selfTest.cases.Count; logs=$testRoot } | ConvertTo-Json
