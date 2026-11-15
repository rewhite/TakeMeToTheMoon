# Game presentation flow

Start → Prologue → Stage 1 → Stage 2 → Stage 3 → Stage 4 → Stage 5 → Ending.
GameManager owns presentation only. StoryController remains the owner of inventory,
choices and story progression; connect the results of those interactions to the
presentation methods instead of storing a second story state here.

## Inspector

- **Shared Video**: one persistent SharedVideoPlayer component. On that component,
  assign the scene VideoPlayer and CurvedVideoScreen once. Keep them outside all
  screen roots. Stage prefabs do not contain video players.
- **Start Screen / End Screen**: screen prefab or preplaced scene root and skybox.
- **Prologue / Stages**: each entry has the settings below. The Stages list has five
  entries by default. Existing Main content is stage 1.
- **Transition Duration**: total black fade-out/fade-in time, default 1 second.
  Zero switches immediately. Transition Camera defaults to Camera.main.
  The assigned stereo URP shader covers camera-rendered objects/world-space UI,
  but not separate Quest compositor overlays or Screen Space Overlay canvases.

Each presentation entry has:

| Field | Meaning |
| --- | --- |
| Title | Inspector label |
| Screen Prefab | Hierarchy root to reuse, or Project prefab to spawn |
| Skybox | Background switched at full black; blank uses original scene skybox |
| Video Clip / Video URL | Clip takes precedence; both empty means no video |
| Video Fade Seconds | Fade-in duration for the common video surface |
| Reveal When | After Seconds, Video Ended, or Manual |
| Reveal After Seconds | Delay after actual video playback starts; without video, after screen transition |
| Reveal Objects | Optional scene objects enabled together when the condition fires |
| Revealed | One event per entry for stage-specific interaction/animation |

For objects inside a prefab, add **GameStageView** to its root and assign its
**Reveal Objects** locally. They are hidden on entry and shown at the configured
time. Do not assign the stage root itself or an ancestor of the manager/view.
Targets shown by a reveal are hidden when leaving the stage.

Reveal does **not** advance the stage. Connect a continue button or a successful
interaction event to **GameManager.NextStage()**. Story-specific choice handling
must go through StoryController before requesting the presentation change.
`ShowStage(1)` through `ShowStage(5)` can jump directly for presentation/testing.
`ShowMain()` remains an alias for `ShowStage(1)` for existing event bindings.
`RevealCurrentStage()` reveals manually. `ReturnToStart()` cancels video and timers.
Video-ended mode requires a video; use After Seconds or Manual for video-free stages.

## Existing scene and migration

The 1114 scene has been migrated. Prologue's prior clip, fade and timing were copied;
Main is stage 1; Stage 2–5 are empty editable prefabs under `Assets/Prefabs/Game Flow`.
Assign their media, backgrounds and interactions as the content becomes available.
No continue button or choice behavior is invented automatically.

For another scene, run **Tools > Game Flow > Configure Five Stages** with GameManager,
VideoPlayer and CurvedVideoScreen present. This assigns the shared component, creates
missing stage prefabs, disables autoplay/looping and removes the legacy PrologueManager
component after copying its settings. The old script remains for migration only.
The former automatic prologue advance is now a reveal event, consistent with stages.

## Validation

In Play Mode, run **Tools > Game Flow > Validate Five Stage Flow (Play Mode)**.
The isolated test checks stage order, ending, timed/video-event reveal, event deduplication,
and replay cleanup. It simulates the video-ended notification; it does not test decoding.
The shared prologue video was also played in the editor. Quest hardware remains untested.

### Gray overlay near scene geometry

The Meta Locomotor prefab includes WallPenetrationTunneling, which casts between logical and tracked head positions and renders a gray outer mask (RGB 0.547) when it detects a collider. In 1114 its detector, TunnelingEffect, and mesh renderer are disabled as scene overrides to remove this suspected cause of the reported Quest gray-out. This has not yet been confirmed on the device. Gameplay colliders and smooth-movement tunneling are unchanged. `Tools > Game Flow > Inspect Geometry Near Origin` logs nearby renderer bounds and camera settings; `Disable Wall Penetration Gray Mask` reapplies only these overlay overrides.

### Stage skybox images

Assign a Texture2D directly to Game Manager > Prologue or Stages > Skybox Image. A 360-degree equirectangular panorama (2:1) wraps correctly; ordinary photos stretch around the sphere. Skybox Image takes priority over the existing Skybox material. Skybox Rotation and Skybox Exposure adjust heading and brightness. Empty image uses the existing material/fallback. One runtime material is reused and released on disable; the Resources/StagePanoramaTemplate material retains the panoramic shader in Quest builds. No scene objects are spawned for this feature. `Tools > Game Flow > Validate Stage Panorama` checks the shader, texture and settings, fallback, and template isolation.

Skybox Image rotation/exposure update live in Play Mode for the current stage, including after reveal. For editing without Play Mode, select Game Manager, enable Skybox Image Preview > Live Preview, choose Preview Stage and adjust the image/rotation/exposure there. This edits the actual stage settings and uses a temporary preview material. Saving the scene, changing selection, or entering Play Mode restores the previous scene skybox and ends preview.

The shared video panel stays visible through prologue → stages 1–5. Stage transitions pause and detach old playback callbacks without hiding the surface or stopping/clearing its output. Empty-video stages retain the previous frame and immediately satisfy video-ended reveal; a replacement video reuses the same surface without fading it to transparency. Start/end screen cleanup still cancels and hides video.

### Prologue emergency pulse

The preplaced Prologue Emergency Pulse object is a collider-free inward sphere with a stereo-aware URP overlay shader. PrologueEmergencyPulse reads GameManager.Phase and pulses red at 0–16% opacity every 4 seconds using unscaled time, only during Prologue. It follows CenterEyeAnchor and renders below the black transition fade. Cycle Seconds, Peak Opacity and Emergency Color are editable. It turns off in start/main/end phases and on disable. Configure Prologue Emergency Pulse creates/links the scene object; Validate Prologue Emergency Pulse (Play Mode) checks phase gating, peak strength, and no collider.

### Wireless Quest demo commands

GameManager enables remote demo commands by default (`Enable Remote Debug Commands`).
The Android receiver requires sender permission `android.permission.DUMP` (ADB shell),
queues a bounded set of commands, and GameManager polls them on Unity's main thread.
No PC server is needed. Rebuild/install once to add this receiver; existing APKs do not have it.
Commands run while the app is running and updating; if the headset is asleep/backgrounded,
queued commands wait until Unity resumes. Broadcast result 0 means queued, not completed.
Read `[MoonDebug]` log messages for completion. Turning off the setting clears the queue.

USB setup (computer and Quest on the same Wi-Fi):
```sh
adb shell ip route
adb tcpip 5555
adb connect QUEST_IP:5555
```
Replace QUEST_IP with the headset Wi-Fi address shown after `src` in `ip route`.
Unplug USB after connecting. If both transports are listed, use
`export ANDROID_SERIAL=QUEST_IP:5555` to select the wireless device.
Wireless setup may need repeating after a headset restart.

From the repository root:
```sh
bash Tools/quest-command.sh reset
bash Tools/quest-command.sh prologue
bash Tools/quest-command.sh stage1
bash Tools/quest-command.sh skip
bash Tools/quest-command.sh status
```
- `reset`: cancel presentation/video, invalidate pending Director replies, clear story history/inventory, reset wellness to 100, return props home and clear sockets, show Start.
- `prologue`: full reset, then replay Prologue.
- `stage1`: full reset, choose the first required available items as test inventory and start the real story, then play Stage 1. These are logical test items, not visually docked props.
- `skip`: pause the current video and reveal current stage UI through GameManager; does not advance to the next stage.
- `status`: log presentation phase, stage, story phase, wellness and inventory count.

Raw command (works from any directory):
```sh
adb shell am broadcast --receiver-foreground -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver --es command reset
adb logcat -s Unity MoonDebug
```
