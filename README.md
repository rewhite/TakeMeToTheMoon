# Take Me To The Moon

**Earth is burning. The last rockets are leaving. Who will you become on the way to the Moon?**

A VR survival story built for **Immerse the Bay 2026**, inspired by the theme “Take Me to the Moon.” Play on Meta Quest 3, make difficult choices, and watch AI-generated video bring their consequences to life.

## The idea

An emergency broadcast wakes you in a Stanford dorm room. Extreme heat is forcing humanity to evacuate Earth, and the final launches for the Moon leave within the hour. You can only carry three items. Your roommate is still asleep.

Do you wake them, or leave?

We wanted to explore more than whether someone survives. We wanted to ask **how they survive, and who they choose to help along the way**. The journey has fixed destinations, but AI adapts the situations and choices to the player's history.

## What you do

1. **Pack for the escape.** Choose three physical objects from your room and place them in your inventory tray.
2. **Make difficult decisions.** Decide what to do about your roommate, how to escape campus, and what to sacrifice as resources run low.
3. **Watch the consequences.** Your choice becomes a prompt for Visko, which generates video streamed onto a curved screen in front of you.
4. **Put your items to use.** At the launch pad, throw your carried objects at targets to help fuel the rocket.
5. **Discover your verdict.** Reach the Moon and see your final wellness, successful throws, decision history, and traveler type.

The journey moves from **Stanford dorm → campus → launch pad → low Earth orbit → Moon approach → lunar ending**. A final wellness score of **85 or more** earns admission as a Moon Citizen. Successful runs also receive a traveler type, such as The Guardian, The Redeemed, or The Survivor, based on their decisions.

## How we built it

| Tool | Role in the experience |
| --- | --- |
| **Unity / C#** | Runs the game on standalone Quest 3: story progression, physical interactions, mini-game, video playback, and results. |
| **Meta XR Interaction SDK** | Connects VR input to grabbing objects, inventory sockets, and UI selections. |
| **Figma** | Used to design the game flow and visual interface, then implement those designs as readable panels in Unity. |
| **OpenAI API** | Writes the next scenario, two choices, and video prompts using the player's previous decisions. |
| **Visko / WebRTC** | Generates and streams the story video into Unity, including changes prompted by a selected outcome. |
| **360° skyboxes** | Surround the curved video screen with environments that change across the journey. |

The core loop is:

**Make a choice → AI writes the outcome → Visko generates the video → watch and decide what comes next.**

Unity keeps the inventory, choices, and wellness in one story controller. While the current scene plays, it can prepare the next story beat and video in the background. The Quest app calls the AI services directly; a PC or Python server does not need to stay running during play.

The opening broadcast and final success/failure clips are prerecorded. The story scenes and generated lunar ending use Visko. This lets us combine a consistent introduction and conclusion with an adaptive journey.

## What made it challenging

- **Keeping live video continuous.** Generation, network delivery, and decoding take time. We retain the current frame and prepare the next stream before switching.
- **Making choices visible.** A written consequence must also become a clear visual action, with consistent characters and locations.
- **Designing for VR.** Panel size, distance, contrast, and placement matter when text sits over moving video and players interact with their hands.
- **Keeping the story connected.** Fixed checkpoints give the AI a structure, while decision history gives each new scene context.
- **Building within a hackathon.** We focused on a complete playable journey and tested on the headset, cutting ideas that did not improve the demo.

## What we learned

A smaller, complete experience was more valuable than a long list of unfinished mechanics. Testing in the headset helped us catch problems that looked fine on a monitor: UI blocking the video, unclear interactions, and transitions that took too long.

The most exciting part was connecting the whole loop: a physical choice in VR, a story that responds to it, and a generated video that lets the player see the result.

## What's next

We want to make visual consequences clearer, reduce waiting between scenes, and explore more journeys using the same interaction loop. One possibility is a classroom experience where students make decisions about climate and resources, then watch the consequences unfold.

## Try the project

This repository contains a **hackathon prototype for standalone Meta Quest 3**. It needs an internet connection and configured OpenAI and Visko API access for live generation.

- Open `Assets/Scenes/1114.unity` in Unity. Use the editor version recorded in [`ProjectSettings/ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt).
- Configure local keys through **Tools → Director → Local OpenAI Key** and **Tools → Visko → Local API Key**. Local key files are Git-ignored.
- Build and install the Android APK on Quest 3. Keep API keys out of source control.
- See the [game implementation guide](Assets/Scripts/Game/README.md) for setup and testing, and [game design](GAME_DESIGN.md) for the narrative concept. The design document also contains ideas beyond the current playable implementation.

---

## Development and demo reference

The original setup instructions and remote-command reference are preserved below.

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

# Prepare a launchpad test run and play the stage 3 video
bash Tools/quest-command.sh stage3

# Enter stage 3 and skip its video in one step (also works from zsh)
bash Tools/quest-command.sh stage3-skip

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
| `stage3` | Reset the run, seed test inventory and launchpad story state, then play stage 3. Send `skip` to reveal the fuel minigame immediately. |
| `stage3-skip` | Helper shortcut: queue `stage3`, then `skip`. Unity waits for the stage transition before skipping. Uses existing app commands; no additional APK rebuild needed if `stage3` already works. |
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

This command works from any directory. Replace the final `reset` with `prologue`, `stage1`, `stage3`, `skip`, or `status` as needed.

```bash
adb shell am broadcast --receiver-foreground -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver --es command reset
```

The Android receiver requires the sender's `android.permission.DUMP` permission, held by ADB shell, and dispatches commands on Unity's main thread. No separate PC server is required.

## Implementation documentation

- [Game flow, video, and remote commands](Assets/Scripts/Game/README.md)
- [Story and inventory](Assets/Scripts/Story/README.md)
- [UI and wellness](Assets/Scripts/UI/README.md)
- [Browser test bench](WebTest/README.md)

To enter stage 3 and skip its video directly from bash or zsh:
```sh
for cmd in stage3 skip; do adb shell am broadcast --receiver-foreground -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver --es command "$cmd" || break; done
```
`stage3-skip` is a helper-script shortcut, not an Android receiver command.
