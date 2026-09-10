param([Parameter(Mandatory=$true)][string]$PayloadPath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert-Case([bool]$Condition, [string]$Name) { if (-not $Condition) { throw ('FAILED: ' + $Name) }; $checks.Add($Name) }
foreach ($relative in @('legacy-handoff.ps1', 'handoff-policy.ps1', 'source\engine\legacy-handoff.ps1', 'source\engine\handoff-policy.ps1', 'source\Build.ps1', 'source\Export-Icon.ps1')) {
    $file = Join-Path $PayloadPath $relative
    $bytes = [IO.File]::ReadAllBytes($file)
    Assert-Case ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) ('UTF8 BOM: ' + $relative)
    $tokens = $null; $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Assert-Case (@($parseErrors).Count -eq 0) ('Windows PowerShell parses: ' + $relative)
}
. (Join-Path $PayloadPath 'handoff-policy.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('wechat-handoff-test-' + [Guid]::NewGuid().ToString('N'))
$project = Join-Path $fixtureRoot ('Bot ' + [char]0x6D4B + [char]0x8BD5)
New-Item -ItemType Directory -Path (Join-Path $project 'tools'), (Join-Path $project '.runtime') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $project 'package.json'), '{"name":"weixin-ilink-codex-bridge"}')
[IO.File]::WriteAllText((Join-Path $project 'tools\supervisor.mjs'), '// LOCAL FIXTURE ONLY')
function Action([string]$Exe, [string]$Arguments, [string]$Directory) { return [pscustomobject]@{ Execute=$Exe; Arguments=$Arguments; WorkingDirectory=$Directory } }
$known = Action 'powershell.exe' ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $project 'start-bridge.ps1') + '"') ''
$node = Action 'node.exe' ('"' + (Join-Path $project 'tools\supervisor.mjs') + '"') ''
Assert-Case (Test-BotTaskAction $known $project) 'known PowerShell launcher with Unicode and spaces'
Assert-Case (Test-BotTaskAction $node $project) 'known node supervisor'
Assert-Case (Test-BotTaskAction (Action 'powershell.exe' '-File start-bridge.ps1' $project) $project) 'relative script resolved against working directory'
Assert-Case (-not (Test-BotTaskAction (Action 'backup.exe' ('--source "' + $project + '"') '') $project)) 'backup action not selected'
Assert-Case (-not (Test-BotTaskAction (Action 'powershell.exe' ('-File "' + $project + '-copy\start-bridge.ps1"') '') $project)) 'similar prefix not selected'
Assert-Case (-not (Test-BotTaskAction (Action 'powershell.exe' '-File start-bridge.ps1' ($project + '-copy')) $project)) 'wrong working directory not selected'
Assert-Case (-not (Test-BotTaskAction (Action 'powershell.exe' ('-Command "& ' + $project + '\start-bridge.ps1"') '') $project)) 'arbitrary command not selected'
$processFixture = [pscustomobject]@{ Name='node.exe'; CommandLine=('"C:\Tools\node.exe" "' + (Join-Path $project 'tools\supervisor.mjs') + '"') }
Assert-Case (Test-BotProcess $processFixture $project) 'process identity uses exact launcher argument'
$processFixture.CommandLine += '.copy'
Assert-Case (-not (Test-BotProcess $processFixture $project)) 'process path suffix not selected'

# Mock every task/process operation in this isolated PowerShell process. No real task is queried or changed.
$global:BotHandoffTestEffects = New-Object 'System.Collections.Generic.List[string]'
$global:BotHandoffTestTasks = @()
$global:BotHandoffTestExportFailure = $false
$global:BotHandoffTestDisableFailure = ''
function Get-ScheduledTask { param($TaskName, $TaskPath)
    if (-not $TaskName) { return $global:BotHandoffTestTasks }
    $found = @($global:BotHandoffTestTasks | Where-Object { $_.TaskName -eq $TaskName -and $_.TaskPath -eq $TaskPath })
    if ($found.Count -ne 1) { throw 'Fixture task missing' }; return $found[0]
}
function Get-CimInstance { param($ClassName, $Filter, $ErrorAction) return @() }
function Export-ScheduledTask { param($TaskName, $TaskPath) if ($global:BotHandoffTestExportFailure) { throw 'Fixture export failure' }; return '<Task />' }
function Disable-ScheduledTask { param($TaskName, $TaskPath)
    $global:BotHandoffTestEffects.Add('disable:' + $TaskName)
    if ($global:BotHandoffTestDisableFailure -eq $TaskName) { $global:BotHandoffTestDisableFailure = ''; throw 'Fixture disable failure' }
}
function Stop-ScheduledTask { param($TaskName, $TaskPath) $global:BotHandoffTestEffects.Add('stop:' + $TaskName) }
function Enable-ScheduledTask { param($TaskName, $TaskPath) $global:BotHandoffTestEffects.Add('enable:' + $TaskName) }
function Start-ScheduledTask { param($TaskName, $TaskPath) $global:BotHandoffTestEffects.Add('start:' + $TaskName) }
function Task-Fixture([string]$Name, $Actions, [string]$State) { return [pscustomobject]@{ TaskName=$Name; TaskPath='\'; Actions=@($Actions); State=$State } }
$taskName = 'Bot-' + [char]0x82F1 + [char]0x8BED
$global:BotHandoffTestTasks = @(Task-Fixture $taskName @($known) 'Running')
$handoff = Join-Path $PayloadPath 'legacy-handoff.ps1'
$operation = [Guid]::NewGuid().ToString('N')
& $handoff -ProjectPath $project -OperationId $operation | Out-Null
$journal = Join-Path $project ('.runtime\handoff-backups\' + $operation + '\operation.json')
$saved = Get-Content -LiteralPath $journal -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Case ($saved.Phase -eq 'stopped' -and $saved.Tasks[0].Name -ceq $taskName) 'Unicode task names survive backup'
Assert-Case ($global:BotHandoffTestEffects.Count -eq 2) 'only exact known task stopped'
$global:BotHandoffTestEffects.Clear()
$failed = $false
try { & $handoff -ProjectPath $project -OperationId ([Guid]::NewGuid().ToString('N')) | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Count -eq 0) 'uncommitted handoff blocks another operation'
& $handoff -ProjectPath $project -OperationId $operation -Restore | Out-Null
Assert-Case ($global:BotHandoffTestEffects.Contains('enable:' + $taskName) -and $global:BotHandoffTestEffects.Contains('start:' + $taskName)) 'restore recovers enabled and running states'
$global:BotHandoffTestEffects.Clear()
$failed = $false
try { & $handoff -ProjectPath $project -OperationId ([Guid]::NewGuid().ToString('N')) -Restore | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Count -eq 0) 'missing operation backup cannot claim restore success'
$global:BotHandoffTestTasks = @(Task-Fixture $taskName @($known, (Action 'backup.exe' '--unrelated' '')) 'Running')
$failed = $false
try { & $handoff -ProjectPath $project -OperationId ([Guid]::NewGuid().ToString('N')) | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Count -eq 0) 'multi-action task aborts before mutation'
$global:BotHandoffTestTasks = @(Task-Fixture $taskName @($known) 'Running')
$global:BotHandoffTestExportFailure = $true; $failed = $false
try { & $handoff -ProjectPath $project -OperationId ([Guid]::NewGuid().ToString('N')) | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Count -eq 0) 'export failure never stops old task'
$global:BotHandoffTestExportFailure = $false
$global:BotHandoffTestTasks = @((Task-Fixture $taskName @($known) 'Running'), (Task-Fixture 'Second fixture task' @($node) 'Ready'))
$global:BotHandoffTestDisableFailure = 'Second fixture task'; $failed = $false
try { & $handoff -ProjectPath $project -OperationId ([Guid]::NewGuid().ToString('N')) | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Contains('enable:' + $taskName) -and $global:BotHandoffTestEffects.Contains('start:' + $taskName)) 'partial disable failure restores saved states'
$global:BotHandoffTestEffects.Clear()
$global:BotHandoffTestTasks = @(Task-Fixture $taskName @((Action 'backup.exe' '--changed' '')) 'Ready')
$failed = $false
try { & $handoff -ProjectPath $project -OperationId $operation -Restore | Out-Null } catch { $failed = $true }
Assert-Case ($failed -and $global:BotHandoffTestEffects.Count -eq 0) 'modified task rejected before restore'
[pscustomobject]@{ ok=$true; powershell=$PSVersionTable.PSVersion.ToString(); count=$checks.Count; cases=@($checks); fixtureRoot=$fixtureRoot } | ConvertTo-Json -Depth 3
