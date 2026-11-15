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
