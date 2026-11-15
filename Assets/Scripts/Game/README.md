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
