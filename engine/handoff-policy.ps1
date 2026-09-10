# Pure matching / serialization helpers shared by handoff and isolated tests.
if (-not ('BotHandoffCommandLine' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BotHandoffCommandLine {
    [DllImport("shell32.dll", SetLastError=true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Split(string command) {
        int count; IntPtr memory = CommandLineToArgvW(command, out count);
        if (memory == IntPtr.Zero) throw new InvalidOperationException("Unable to parse command line.");
        try {
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size));
            return result;
        } finally { LocalFree(memory); }
    }
}
'@
}
function Get-BotCanonicalPath([string]$Value, [string]$WorkingDirectory) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim('"'))
    if (-not [IO.Path]::IsPathRooted($expanded)) {
        if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) { return '' }
        $expanded = Join-Path $WorkingDirectory $expanded
    }
    return [IO.Path]::GetFullPath($expanded).TrimEnd('\')
}
function Test-BotLauncher([string]$Executable, [string[]]$Tokens, [string]$WorkingDirectory, [string]$Project) {
    $program = [IO.Path]::GetFileName($Executable.Trim('"')).ToLowerInvariant()
    $candidate = ''; $expected = ''
    if ($program -in @('powershell.exe', 'pwsh.exe')) {
        for ($i = 0; $i -lt $Tokens.Count; $i++) {
            if ($Tokens[$i] -ieq '-File' -and $i -eq $Tokens.Count - 2) { $candidate = $Tokens[$i + 1]; break }
            if ($Tokens[$i] -in @('-NoProfile', '-NonInteractive', '-NoLogo')) { continue }
            if ($Tokens[$i] -in @('-ExecutionPolicy', '-WindowStyle') -and $i + 1 -lt $Tokens.Count) { $i++; continue }
            return $false
        }
        $expected = Join-Path $Project 'start-bridge.ps1'
    } elseif ($program -eq 'node.exe' -and $Tokens.Count -eq 1) {
        $candidate = $Tokens[0]; $expected = Join-Path $Project 'tools\supervisor.mjs'
    } else { return $false }
    if (-not $candidate) { return $false }
    try { return (Get-BotCanonicalPath $candidate $WorkingDirectory) -ieq [IO.Path]::GetFullPath($expected) } catch { return $false }
}
function Test-BotTaskAction($Action, [string]$Project) {
    $tokens = @([BotHandoffCommandLine]::Split('launcher.exe ' + [string]$Action.Arguments) | Select-Object -Skip 1)
    return Test-BotLauncher ([string]$Action.Execute) $tokens ([string]$Action.WorkingDirectory) $Project
}
function Test-BotProcess($Process, [string]$Project) {
    if ([string]::IsNullOrWhiteSpace([string]$Process.CommandLine)) { return $false }
    $tokens = @([BotHandoffCommandLine]::Split([string]$Process.CommandLine))
    if ($tokens.Count -lt 2) { return $false }
    return Test-BotLauncher ([string]$Process.Name) @($tokens | Select-Object -Skip 1) '' $Project
}
function Get-BotActionSignature($Task) {
    return (@($Task.Actions | ForEach-Object { [ordered]@{ Execute = [string]$_.Execute; Arguments = [string]$_.Arguments; WorkingDirectory = [string]$_.WorkingDirectory } }) | ConvertTo-Json -Compress -Depth 5)
}
function Write-BotJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($true)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, ($Path + '.previous'), $true) }
    else { [IO.File]::Move($temporary, $Path) }
}
