#!/usr/bin/env bash
set -euo pipefail
command_name="${1:-status}"
case "$command_name" in
  reset|prologue|stage1|stage3|stage3-skip|skip|status) ;;
  *) echo 'Usage: bash Tools/quest-command.sh {reset|prologue|stage1|stage3|stage3-skip|skip|status}' >&2; exit 2 ;;
esac
send_command() {
  adb shell am broadcast --receiver-foreground \
    -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver \
    --es command "$1"
}
if [[ "$command_name" == "stage3-skip" ]]; then
  # Unity holds queued commands until the stage transition finishes.
  send_command stage3
  send_command skip
else
  send_command "$command_name"
fi
