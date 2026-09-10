param([Parameter(Mandatory=$true)][string]$ProjectPath, [switch]$Restore)
$ErrorActionPreference = 'Stop'
$resolvedBotProject = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\')
$botPackage = Get-Content -LiteralPath (Join-Path $resolvedBotProject 'package.json') -Raw | ConvertFrom-Json
if ($botPackage.name -ne 'weixin-ilink-codex-bridge') { throw '目标不是已知微信 Bot 项目。' }
$legacySupervisor = Join-Path $resolvedBotProject 'tools\supervisor.mjs'
if (-not (Test-Path -LiteralPath $legacySupervisor)) { throw '找不到旧版启动程序。' }
$handoffPath = Join-Path $resolvedBotProject '.runtime\desktop-handoff.json'
if ($Restore) {
    if (Test-Path -LiteralPath $handoffPath) {
        $savedHandoff = Get-Content -LiteralPath $handoffPath -Raw | ConvertFrom-Json
        foreach ($savedTask in @($savedHandoff.Tasks)) {
            if ($savedTask.Enabled) { Enable-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path | Out-Null }
            if ($savedTask.Running) { Start-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path }
        }
    }
    exit 0
}
$matchedTasks = @(Get-ScheduledTask | Where-Object {
    $candidateActions = @($_.Actions)
    @($candidateActions | Where-Object {
        $actionText = ([string]$_.Execute) + ' ' + ([string]$_.Arguments) + ' ' + ([string]$_.WorkingDirectory)
        $actionText.IndexOf($resolvedBotProject, [StringComparison]::OrdinalIgnoreCase) -ge 0
    }).Count -gt 0
})
$taskStates = @($matchedTasks | ForEach-Object {
    [pscustomobject]@{Name=$_.TaskName;Path=$_.TaskPath;Enabled=($_.State -ne 'Disabled');Running=($_.State -eq 'Running')}
})
$handoffBackupDir = Join-Path $resolvedBotProject ('.runtime\handoff-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $handoffBackupDir -Force | Out-Null
$handoffNumber = 0
foreach ($task in $matchedTasks) {
    $taskXml = Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    [IO.File]::WriteAllText((Join-Path $handoffBackupDir ('task-' + $handoffNumber + '.xml')), $taskXml)
    $handoffNumber++
}
[IO.File]::WriteAllText($handoffPath, ([pscustomobject]@{Project=$resolvedBotProject;Tasks=$taskStates;At=(Get-Date).ToString('o')} | ConvertTo-Json -Depth 4))
try {
    foreach ($task in $matchedTasks) {
        Disable-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath | Out-Null
        Stop-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    }
    $oldProcesses = @(Get-CimInstance Win32_Process | Where-Object {
        ($_.Name -eq 'node.exe' -and ([string]$_.CommandLine).IndexOf($legacySupervisor, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($_.Name -in @('powershell.exe','pwsh.exe') -and ([string]$_.CommandLine).IndexOf((Join-Path $resolvedBotProject 'start-bridge.ps1'), [StringComparison]::OrdinalIgnoreCase) -ge 0)
    })
    foreach ($oldProcess in $oldProcesses) {
        $stillSame = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $oldProcess.ProcessId) -ErrorAction SilentlyContinue
        if ($stillSame -and $stillSame.CreationDate -eq $oldProcess.CreationDate) {
            & (Join-Path $env:SystemRoot 'System32\taskkill.exe') /PID ([string]$oldProcess.ProcessId) /T /F | Out-Null
            if ($LASTEXITCODE -ne 0 -and (Get-Process -Id $oldProcess.ProcessId -ErrorAction SilentlyContinue)) { throw '旧版进程仍在运行。' }
        }
    }
} catch {
    foreach ($savedTask in $taskStates) {
        if ($savedTask.Enabled) { Enable-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path -ErrorAction SilentlyContinue | Out-Null }
        if ($savedTask.Running) { Start-ScheduledTask -TaskName $savedTask.Name -TaskPath $savedTask.Path -ErrorAction SilentlyContinue }
    }
    throw
}
