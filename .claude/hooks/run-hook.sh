#!/usr/bin/env bash
# .claude/hooks/run-hook.sh
# Cross-platform bash shim that invokes a .csx hook via dotnet-script.
#
# Usage (called from dispatcher scripts or directly):
#   bash .claude/hooks/run-hook.sh <hook-file.csx> [extra args...]
#
# Responsibilities:
#   1. Locate dotnet-script (install once per machine if missing).
#   2. Resolve the .csx path relative to .claude/hooks/.
#   3. Pass stdin through to the script (Claude Code hooks receive
#      their JSON payload on stdin).
#   4. Propagate the script's exit code so commit/push gates can block
#      the git operation by returning non-zero.
#
# Notes:
#   * Requires dotnet SDK (used for dotnet-script global tool).
#   * Set MEDIATORLITE_HOOK_VERBOSE=1 for verbose output.

SCRIPT_NAME="$1"
shift

VERBOSE="${MEDIATORLITE_HOOK_VERBOSE:-0}"
HOOKS_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HOOKS_DIR/../.." && pwd)"
SCRIPT_PATH="$HOOKS_DIR/$SCRIPT_NAME"

trace() { [ "$VERBOSE" = "1" ] && echo "[hook] $*" >&2; }

if [ ! -f "$SCRIPT_PATH" ]; then
    echo "Hook script not found: $SCRIPT_PATH" >&2
    exit 64
fi

# Ensure dotnet-script is on PATH.
export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v dotnet-script &>/dev/null; then
    if command -v dotnet &>/dev/null; then
        if dotnet tool list -g 2>/dev/null | grep -q 'dotnet-script'; then
            trace "dotnet-script installed but not on PATH; added ~/.dotnet/tools"
        else
            echo "[hook] Installing dotnet-script as a one-time global tool (required for .claude hooks)..." >&2
            dotnet tool install -g dotnet-script >&2 || {
                echo "[hook] dotnet-script install failed; hook '$SCRIPT_NAME' skipped." >&2
                exit 0
            }
        fi
    else
        echo "[hook] dotnet SDK not found; hook '$SCRIPT_NAME' skipped. Install .NET 10 SDK to enable hooks." >&2
        exit 0
    fi
fi

trace "Executing $SCRIPT_PATH"
cd "$REPO_ROOT"

if [ $# -gt 0 ]; then
    dotnet script "$SCRIPT_PATH" -- "$@"
else
    dotnet script "$SCRIPT_PATH"
fi
exit $?
