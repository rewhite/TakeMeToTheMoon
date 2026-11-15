# Take Me To The Moon

A Unity demo for standalone Quest 3. The main scene is `Assets/Scenes/1114.unity`.

## Remote commands while the Quest app is running

Send commands from your computer over wireless ADB to reset the run or test a stage without closing the app.

### Initial setup

1. Enable developer mode on Quest and authorize USB debugging from your computer.
2. **Build and install an APK containing the remote command feature**, then launch it. Older APKs do not support these commands.
3. Enable **Enable Remote Debug Commands** on Game Manager. It is enabled by default.
4. Connect your computer and Quest to the same Wi-Fi network, and connect the headset over USB for initial setup.

```bash
adb devices
adb shell ip route
```

Find the headset's Wi-Fi IP address after `src` in the route output. Replace `QUEST_IP` below with that address.

```bash
adb tcpip 5555
adb connect QUEST_IP:5555
adb devices
```

Once the wireless connection appears as `device`, you can unplug USB. If USB and wireless connections both appear, select the wireless target:

```bash
export ANDROID_SERIAL=QUEST_IP:5555
```

You may need to repeat wireless setup after restarting the headset. Run `unset ANDROID_SERIAL` when switching back to USB.

### Run commands

Open a terminal in the project root, then run:

```bash
# Reset the entire run and return to the start screen
bash Tools/quest-command.sh reset

# Reset the entire run and replay the prologue
bash Tools/quest-command.sh prologue

# Prepare test inventory and enter stage 1
bash Tools/quest-command.sh stage1

# Stop the current video and reveal the current stage's panel
bash Tools/quest-command.sh skip

# Print the current state to the device log
bash Tools/quest-command.sh status
```

| Command | Behavior |
| --- | --- |
| `reset` | Clear story history, choices, and inventory; restore wellness to 100; return props home and clear sockets; show the start screen. |
| `prologue` | Reset the entire run, then play the prologue. |
| `stage1` | Reset the run, fill the logical inventory from Available Items in order (three items with the current settings), start the story, and play stage 1. This does not physically dock the props in the tray. |
| `skip` | Stop the current video and reveal the current stage's UI. This does not advance to the next stage. |
| `status` | Log the presentation phase, stage number, story phase, wellness, and inventory count. |

### Logs and troubleshooting

```bash
adb logcat -s Unity MoonDebug
```

- `result=0` and `Queued` mean the command was accepted. Check `[MoonDebug]` logs for completion.
- The app must be running. If the headset is asleep or the app is in the background, queued commands wait until Unity resumes.
- Commands received during a screen transition wait until the transition finishes.
- If you see `Open the app ... first`, check that the new APK is installed, the app is running, and remote commands are enabled on Game Manager.
- If you see `more than one device/emulator`, select the target with `ANDROID_SERIAL`.
- If you see `adb: command not found`, add your Android SDK's `platform-tools` directory to PATH.

### Run without the helper script

This command works from any directory. Replace the final `reset` with `prologue`, `stage1`, `skip`, or `status` as needed.

```bash
adb shell am broadcast --receiver-foreground -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver --es command reset
```

The Android receiver requires the sender's `android.permission.DUMP` permission, held by ADB shell, and dispatches commands on Unity's main thread. No separate PC server is required.

## Implementation documentation

- [Game flow, video, and remote commands](Assets/Scripts/Game/README.md)
- [Story and inventory](Assets/Scripts/Story/README.md)
- [UI and wellness](Assets/Scripts/UI/README.md)
- [Browser test bench](WebTest/README.md)
