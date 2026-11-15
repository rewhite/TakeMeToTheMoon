#!/usr/bin/env bash
set -euo pipefail
command_name="${1:-status}"
case "$command_name" in
  reset|prologue|stage1|skip|status) ;;
  *) echo 'Usage: bash Tools/quest-command.sh {reset|prologue|stage1|skip|status}' >&2; exit 2 ;;
esac
adb shell am broadcast --receiver-foreground \
  -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver \
  --es command "$command_name"
