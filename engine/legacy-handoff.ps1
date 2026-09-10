param([Parameter(Mandatory=$true)][string]$ProjectPath, [Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$OperationId, [switch]$Restore)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
. (Join-Path $PSScriptRoot 'handoff-policy.ps1')
$resolvedBotProject = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\')
if ($resolvedBotProject -eq [IO.Path]::GetPathRoot($resolvedBotProject).TrimEnd('\')) { throw '不允许接管磁盘根目录。' }
$botPackage = Get-Content -LiteralPath (Join-Path $resolvedBotProject 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($botPackage.name -ne 'weixin-ilink-codex-bridge') { throw '目标不是已知微信 Bot 项目。' }
if (-not (Test-Path -LiteralPath (Join-Path $resolvedBotProject 'tools\supervisor.mjs'))) { throw '找不到旧版启动程序。' }
$handoffPath = Join-Path $resolvedBotProject '.runtime\desktop-handoff.json'
$handoffLock = [IO.File]::Open((Join-Path $resolvedBotProject '.runtime\desktop-handoff.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
$journalPath = Join-Path $resolvedBotProject ('.runtime\handoff-backups\' + $OperationId + '\operation.json')
function Restore-BotTasks($Saved) {
    foreach ($savedTask in @($Saved.Tasks)) {
        $currentTask = Get-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path
        if ((Get-BotActionSignature $currentTask) -cne $savedTask.Signature) { throw ('旧任务已被修改，停止自动恢复：' + $savedTask.Name) }
    }
    foreach ($savedTask in @($Saved.Tasks)) {
        if ($savedTask.Enabled) { Enable-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path | Out-Null }
        else { Disable-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path | Out-Null }
        if ($savedTask.Running) { Start-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path }
    }
}
function Save-BotHandoff($Value) { Write-BotJson $journalPath $Value; Write-BotJson $handoffPath $Value }
if ($Restore) {
    if (-not (Test-Path -LiteralPath $journalPath)) { throw '缺少本次接管备份，未执行恢复。' }
    $savedHandoff = Get-Content -LiteralPath $journalPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($savedHandoff.Schema -ne 2 -or $savedHandoff.OperationId -cne $OperationId -or $savedHandoff.Project -ine $resolvedBotProject -or $savedHandoff.Phase -notin @('prepared', 'stopped', 'committed', 'restore-needed', 'restored')) { throw '接管备份身份或状态不匹配，未执行恢复。' }
    try { Restore-BotTasks $savedHandoff; $savedHandoff.Phase = 'restored'; Save-BotHandoff $savedHandoff }
    catch { $savedHandoff.Phase = 'restore-needed'; Save-BotHandoff $savedHandoff; throw }
    Write-Output '旧计划任务设置已恢复。手动启动且没有计划任务的旧进程需手动启动。'
    return
}
if (Test-Path -LiteralPath $handoffPath) {
    $previousHandoff = Get-Content -LiteralPath $handoffPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($previousHandoff.Schema -eq 2 -and $previousHandoff.Phase -in @('prepared', 'stopped', 'restore-needed')) { throw '上次接管尚未完成恢复，请保留备份并先检查状态。' }
}
# Complete all read-only checks and exports before the first stop/disable operation.
$matchedTasks = @()
foreach ($candidateTask in @(Get-ScheduledTask)) {
    $actions = @($candidateTask.Actions)
    $matches = @($actions | Where-Object { Test-BotTaskAction $_ $resolvedBotProject })
    if ($matches.Count -eq 0) { continue }
    if ($actions.Count -ne 1) { throw ('旧任务包含多个动作，需先人工确认：' + $candidateTask.TaskName) }
    $matchedTasks += $candidateTask
}
$oldProcesses = @(Get-CimInstance Win32_Process | Where-Object { Test-BotProcess $_ $resolvedBotProject })
$taskStates = @($matchedTasks | ForEach-Object {
    [pscustomobject]@{ Name=$_.TaskName; Path=$_.TaskPath; Enabled=($_.State -ne 'Disabled'); Running=($_.State -eq 'Running'); Signature=(Get-BotActionSignature $_) }
})
$handoffBackupDir = Split-Path -Parent $journalPath
if (Test-Path -LiteralPath $handoffBackupDir) { throw '本次操作编号已存在，未覆盖原备份。' }
New-Item -ItemType Directory -Path $handoffBackupDir -Force | Out-Null
$handoffNumber = 0
foreach ($task in $matchedTasks) {
    $taskXml = Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    [IO.File]::WriteAllText((Join-Path $handoffBackupDir ('task-' + $handoffNumber + '.xml')), $taskXml, (New-Object Text.UTF8Encoding($true)))
    $handoffNumber++
}
$handoff = [pscustomobject]@{ Schema=2; OperationId=$OperationId; Project=$resolvedBotProject; Tasks=$taskStates; At=(Get-Date).ToString('o'); Phase='prepared' }
Save-BotHandoff $handoff
try {
    foreach ($task in $matchedTasks) {
        $currentTask = Get-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
        if ((Get-BotActionSignature $currentTask) -cne (Get-BotActionSignature $task)) { throw '任务在接管期间发生变化，已中止。' }
        Disable-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath | Out-Null
        Stop-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    }
    foreach ($oldProcess in $oldProcesses) {
        $stillSame = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $oldProcess.ProcessId) -ErrorAction SilentlyContinue
        if ($stillSame -and $stillSame.CreationDate -eq $oldProcess.CreationDate -and (Test-BotProcess $stillSame $resolvedBotProject)) {
            & (Join-Path $env:SystemRoot 'System32\taskkill.exe') /PID ([string]$oldProcess.ProcessId) /T /F | Out-Null
            if ($LASTEXITCODE -ne 0 -and (Get-Process -Id $oldProcess.ProcessId -ErrorAction SilentlyContinue)) { throw '选中的旧版进程仍在运行。' }
        }
    }
    $handoff.Phase = 'stopped'; Save-BotHandoff $handoff
    Write-Output '选中项目的旧启动器已停止，账号和用量文件保持原位。'
} catch {
    $originalError = $_
    try { Restore-BotTasks $handoff; $handoff.Phase = 'restored'; Save-BotHandoff $handoff }
    catch { $handoff.Phase = 'restore-needed'; Save-BotHandoff $handoff; throw ('接管中止，恢复未完成。请保留 operation.json：' + $_.Exception.Message) }
    throw $originalError
}
} finally { $handoffLock.Dispose() }
