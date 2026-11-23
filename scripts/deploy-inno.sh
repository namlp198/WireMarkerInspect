#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.1.0}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_dir/verify-release.ps1"
if command -v cygpath >/dev/null 2>&1; then ps_script="$(cygpath -w "$ps_script")"
elif command -v wslpath >/dev/null 2>&1; then ps_script="$(wslpath -w "$ps_script")"
fi

if command -v powershell.exe >/dev/null 2>&1; then shell=(powershell.exe)
elif command -v pwsh.exe >/dev/null 2>&1; then shell=(pwsh.exe)
else echo "Windows PowerShell and Inno Setup 6 are required for deployment." >&2; exit 1
fi
exec "${shell[@]}" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$ps_script" -Version "$version" -RequireOcrAssets
