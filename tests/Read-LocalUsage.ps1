param([Parameter(Mandatory=$true)][string]$ProgramPath, [Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
[void][Reflection.Assembly]::LoadFrom($ProgramPath)
$settings = [WechatBotDesktop.Files]::Json.Deserialize([IO.File]::ReadAllText($ConfigPath), [WechatBotDesktop.Settings])
$snapshot = (New-Object WechatBotDesktop.UsageReader).Read($settings.Bots)
$totals = [WechatBotDesktop.UsageTotals]::From($snapshot, 0, (Get-Date))
$expectedCount = 0; $expectedTokens = [decimal]0; $expectedCost = [decimal]0; $fallbackCount = 0
foreach ($bot in $settings.Bots) {
    $file = Join-Path $bot.Root '.runtime\usage.json'
    if (Test-Path -LiteralPath $file) {
        $original = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        foreach ($row in $original.records) {
            $expectedCount++; $expectedTokens += [decimal]$row.totalTokens
            if ($null -ne $row.costUsd) { $expectedCost += [decimal]$row.costUsd }
            elseif ($row.model -eq 'gpt-5.6-luna' -and $null -ne $row.inputTokens -and $null -ne $row.outputTokens) {
                $cached = [Math]::Min([decimal]$row.inputTokens, [Math]::Max(0, [decimal]$row.cachedInputTokens))
                $expectedCost += (([decimal]$row.inputTokens - $cached) * [decimal]0.2 + $cached * [decimal]0.02 + [decimal]$row.outputTokens * [decimal]1.2) / [decimal]1000000
                $fallbackCount++
            }
        }
    }
}
if ($totals.Entries.Count -ne $expectedCount -or $totals.Total -ne $expectedTokens -or $totals.CostUsd -ne $expectedCost) { throw ('Native totals differ. native=' + $totals.Entries.Count + ',' + $totals.Total + ',' + $totals.CostUsd + '; original=' + $expectedCount + ',' + $expectedTokens + ',' + $expectedCost) }
[pscustomobject]@{ ok=$true; bots=$snapshot.Bots; savedRecords=$totals.Entries.Count; totalTokens=$totals.Total; costUsd=$totals.CostUsd; legacyRecordsWithFallbackCost=$fallbackCount; fxRate=$snapshot.Fx.Rate; warningCount=$snapshot.Warnings.Count; emptyBots=$snapshot.EmptyBots; readOnly=$true } | ConvertTo-Json
