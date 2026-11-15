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
bash Tools/quest-command.sh stage3
bash Tools/quest-command.sh stage3-skip
bash Tools/quest-command.sh skip
bash Tools/quest-command.sh status
```
- `reset`: cancel presentation/video, invalidate pending Director replies, clear story history/inventory, reset wellness to 100, return props home and clear sockets, show Start.
- `prologue`: full reset, then replay Prologue.
- `stage1`: full reset, choose the first required available items as test inventory and start the real story, then play Stage 1. These are logical test items, not visually docked props.
- `stage3-skip`: helper shortcut that queues stage 3 then skip; the app waits for the transition to finish before revealing the minigame. Works from zsh with the `bash` invocation above. No new APK is required if `stage3` already works.
- `skip`: pause the current video and reveal current stage UI through GameManager; does not advance to the next stage.
- `status`: log presentation phase, stage, story phase, wellness and inventory count.

Raw command (works from any directory):
```sh
adb shell am broadcast --receiver-foreground -n com.takemetothemoon.quest/com.takemetothemoon.debug.MoonDebugReceiver --es command reset
adb logcat -s Unity MoonDebug
```

### Stage 3 fuel throwing minigame

`Stage 3/Stage 3 Fuel Minigame` is preplaced in 1114 and revealed by Stage 3's VideoEnded condition. Targets use three preplaced instances of Assets/Prefabs/3D Models/Target.prefab, scaled to approximately 55×45×9 cm (mass 0.2 kg) on invisible BoxCollider supports. Their motion is ordinary Rigidbody physics; a projectile-contacted target tilted at least 40 degrees counts as knocked down. Each throw can score only once. The target, support, item home and UI transforms are editable in the Hierarchy.

The game uses preplaced copies of the original Meta ray-grabbable inventory props, filtered against StoryController's remaining inventory. These copies disable inventory SnapInteractor and idle/home motion. Meta Grabbable samples the release velocity; a release below 0.15 m/s returns home without spending a throw. A standardized solid sphere collider provides consistent physical contact while existing ray colliders remain triggers. The preplaced spare prop is enabled only if inventory is empty.

StoryController owns the three attempts, pending-throw lock, hits, wellness and completion. Defaults are +10 on hit and −10 on miss, editable on FuelThrowGame. A fall below the play region, escape beyond 12 m or 6-second timeout resolves a miss. Thrown items deactivate after a short result delay and cannot be reused during that challenge. A new test run restores the preplaced props. If fewer than three props remain, the attempt count is capped to the available props so Continue cannot get stuck. Continue appears after all available throws resolve and asks StoryController to record the outcome and request checkpoint 4; it does not independently advance GameManager. Checkpoint 3's ordinary story choices are disabled because this minigame replaces that decision.

Stage 3 currently uses the existing stage 1 demo clip if it had no assigned video. Replace **Game Manager > Stages > Element 2 > Video Clip** with the intended stage 3 video. The shared video screen remains in the scene.

Tools > Game Flow > Configure Stage 3 Fuel Minigame builds the preplaced layout in Edit Mode (and repairs input/physical colliders on an existing layout). Validate Stage 3 Fuel State checks scoring, duplicates, limits and checkpoint progression. In Play Mode, Preview and Validate Stage 3 Fuel runs actual hit/miss trajectories and leaves a fresh preview. Direct Editor stage-3 previews seed temporary story inventory; Quest uses the actual checkpoint and inventory.

`stage3` is also available over ADB: it fully resets the run, seeds test inventory and checkpoint 3 through StoryController, and plays the stage 3 video. Follow with `skip` to reveal the fuel minigame. Rebuild/install the updated APK before using this new command.

Fuel results reuse the preplaced Prologue Emergency Pulse overlay: success flashes green, failure red, then fades out over 1.1 seconds (16% peak opacity). Configure Feedback Duration, Feedback Opacity, Success Color and Failure Color on that object. This is a single pulse per resolved throw, separate from the continuous prologue pulse. Stage transitions, minigame disable and resets clear feedback; no overlay objects are spawned.

Fuel minigame ray selection uses PullToHandProvider (Meta MoveTowardsTarget with the FAST 0.1-second travel preset): the selected point moves to the selecting hand/controller and follows it until release. A release within 0.2 seconds of selection returns the prop without spending an attempt, avoiding a throw caused solely by the initial pull motion. Inventory-selection props keep their existing distant dragging behavior.

Fuel results also show the preplaced Wellness Result Toast under Fuel Instructions for 2.2 seconds, with fade-in/out and the success/failure textures from Assets/Images. Those images contain baked-in +10/-10 text, matching the configured score amounts; wellness still clamps to 0–100. The toast never intercepts ray or mouse input.

Stage 3 floaty throws preserve release direction, amplify speed 2.8× with a 3.8 m/s minimum launch speed, add at least 1.1 m/s lift for forward/upward flicks, and apply 18% gravity with zero linear damping. Deliberate downward throws keep their downward direction. Tune these fields on FuelThrowItem; short holds under 0.2 seconds still cancel the initial ray pull.

Flight captures Meta VelocityThrow.WhenThrown at release, freezes projectile rotation, centres its mass at the physical sphere, and uses one explicit scaled-gravity force. It ignores further pointer events in flight; target collisions retain normal Rigidbody responses. This prevents wrist spin from making offset prop visuals corkscrew.

### Stage 1 Visko presentation

`StageViskoPresentation` connects the existing StoryController and GameManager to a persistent Visko client and preplaced Choice Cards/Narration UI. The scene has no Stage 1 local video or default screen prefab. Stage 2 keeps its authored presentation. Director provides `video_prompt`, narration, choices and `outcome_video_prompt`; no reference image is uploaded. Stage 1 uses OpeningScenes.json dorm/roommate prompts and choices; Director prepares the next checkpoint branches while the roommate choices are available.

The dorm opening, roommate scene and selected outcome reuse one Visko session/track with `set_prompt` morphs. The opening timer starts only after playback readiness. Roommate choices stay hidden until its video is ready, then wait 2.5 seconds and fade in over 0.4 seconds. Input is disabled while hidden/fading; choosing immediately hides the cards.

Playback readiness requires `prompt_accepted`, completed chunks (two on morphs to skip an in-flight old chunk; one on initial start), a 1.8-second playback buffer, decoded-frame progress and an unobscured Stage 1 screen. A texture alone is insufficient. These signals conservatively estimate the changed shot: Visko does not correlate prompt IDs with decoded video frames, so this is not exact semantic synchronization. Adjust the buffer after headset testing if needed.

Selecting a choice holds StoryController progression until that outcome boundary, then starts its 10-second timer. Both elapsed playback time and the next Director response are required to advance. Video stalls and headset suspension pause timing. Resume/retry can reconnect and replay the outcome without applying the choice twice. Server generation limits restart automatically. Stage exit retains a copied final frame before disposing the live track; other stages keep their authored presentation. Stage 1's old reveal list is empty so the legacy RoommateChoicePanel cannot advance the opening or expose duplicate cards.

Tools > Game Flow > Connect Stage 1 Visko updates the preplaced scene wiring while preserving existing UI transforms. Tools > Game Flow > Validate Visko Outcome Gate checks old-stream rejection, chunk/frame boundaries, card delay, stalled/transition input, the 10-second gate, Director retry and reset without contacting services.
