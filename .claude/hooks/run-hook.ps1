# .claude/hooks/run-hook.ps1
# Cross-platform shim that invokes a .csx hook via dotnet-script.
#
# Usage (called from .claude/hooks.json):
#   pwsh -NoProfile -File .claude/hooks/run-hook.ps1 <hook-file.csx> [extra args]
#
# Responsibilities:
#   1. Locate dotnet-script (install it on first run if missing).
#   2. Resolve the .csx path relative to .claude/hooks/.
#   3. Pipe the caller's stdin through to the script (Cursor hooks receive
#      their payload on stdin as JSON).
#   4. Propagate the script's exit code so commit/push gates can block the
#      git operation by returning non-zero.
#
# Notes:
#   * Requires PowerShell 7+ (pwsh). The Cursor-spawned shell on Windows is
#     usually Windows PowerShell 5.1, but Cursor invokes hooks with a
#     user-configurable command; we use pwsh for consistency.
#   * Stays quiet on success unless MEDIATORLITE_HOOK_VERBOSE=1.

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$ScriptName,

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'
$verbose = $env:MEDIATORLITE_HOOK_VERBOSE -eq '1'

function Write-Trace { param($m) if ($verbose) { Write-Host "[hook] $m" -ForegroundColor DarkGray } }

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $PSScriptRoot $ScriptName

if (-not (Test-Path $scriptPath)) {
    Write-Error "Hook script not found: $scriptPath"
    exit 64  # EX_USAGE
}

# Ensure dotnet-script is available (install once per machine).
$dotnetScript = Get-Command dotnet-script -ErrorAction SilentlyContinue
if (-not $dotnetScript) {
    Write-Trace 'dotnet-script not on PATH; attempting install'
    try {
        & dotnet tool list -g 2>$null | Out-Null
        $globalTools = (& dotnet tool list -g) -join "`n"
        if ($globalTools -notmatch 'dotnet-script') {
            Write-Host '[hook] Installing dotnet-script as a one-time global tool (required for .cursor hooks)...' -ForegroundColor Yellow
            & dotnet tool install -g dotnet-script 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "dotnet-script install failed (exit $LASTEXITCODE). Hook '$ScriptName' will be skipped."
                exit 0  # Don't block the user on tool install failures.
            }
        }
        # PATH refresh hack for the current process.
        $toolsDir = Join-Path $env:USERPROFILE '.dotnet\tools'
        if (Test-Path $toolsDir -and ($env:PATH -notlike "*$toolsDir*")) { $env:PATH = "$env:PATH;$toolsDir" }
    } catch {
        Write-Warning "dotnet SDK not detected; hook '$ScriptName' skipped. Install .NET 10 SDK to enable hooks."
        exit 0
    }
}

Write-Trace "Executing $scriptPath"
Set-Location $repoRoot

# Pass the hook JSON payload (stdin) straight through. dotnet script honors stdin.
if ($Rest -and $Rest.Count -gt 0) {
    & dotnet script $scriptPath -- @Rest
} else {
    & dotnet script $scriptPath
}
exit $LASTEXITCODE
