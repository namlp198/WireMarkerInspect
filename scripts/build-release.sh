#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_dir/build.ps1"
if command -v cygpath >/dev/null 2>&1; then ps_script="$(cygpath -w "$ps_script")"
elif command -v wslpath >/dev/null 2>&1; then ps_script="$(wslpath -w "$ps_script")"
fi

if command -v powershell.exe >/dev/null 2>&1; then shell=(powershell.exe)
elif command -v pwsh.exe >/dev/null 2>&1; then shell=(pwsh.exe)
else echo "Windows PowerShell is required for this Windows x64 build." >&2; exit 1
fi
exec "${shell[@]}" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$ps_script" -Configuration Release "$@"
