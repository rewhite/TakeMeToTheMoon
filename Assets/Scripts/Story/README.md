# Unity Story Controller

`StoryController` manages the WebTest story state independently of the screen,
Meta interactions, and network services. It does not make AI or Visko API calls.

## Quick preview

1. Add `Assets/Prefabs/Story Controller.prefab` to a scene, or use
   **GameObject > Story > Story Controller**.
2. Enter Play Mode, select the controller, and select 3 of the 5 items in
   **Story Preview**. Press **Start Story**.
3. The opening advances after 10 seconds (or **Skip Opening Wait**). Choose
   whether to wake the roommate; observe inventory, distance and wellness.
4. The controller waits for a Director response. To test locally, assign
   `Assets/Story/PreviewEnding.json` to **Next Scene JSON** and press
   **Submit Preview Scene**. The ending appears after the 6-second outcome dwell.

The preview ending is only supplied manually; it is not an automatic AI fallback.
Scene placement, models and world-space choice UI can be added separately.

## Physical item selection (Meta XR)

- Existing `Grabbable` / `SnapInteractor` components continue to handle grabbing.
- Add `StoryItem` to each grabbed object's root, above its `SnapInteractor`, and
  assign an asset from `Assets/Story/Items`.
- Add **one** `StorySocketSelection` to the controller object. Assign the controller
  and the `SnapInteractable` sockets used for inventory selection (normally 3).
- During item selection, this adapter makes the inventory match the occupied
  sockets. Removing an item deselects it. Duplicate item types count only once;
  unknown items and selections above the limit are ignored. The adapter does not
  physically eject rejected objects or change Meta's filters.
- Wire the confirm button to `StorySocketSelection.StartStory()`. This refreshes
  sockets before confirming, including undocks in the same frame. With active
  sockets configured, they own selection; Inspector toggles will be overwritten.
- After confirmation, the inventory is locked; moving physical props no longer
  changes it. Item use consumes the logical inventory entry, not the physical prop.

For button-only selection, omit the socket adapter and bind
`StoryController.SelectItem(item)`, `DeselectItem(item)`, and `StartStory()`.
Create new item types through **Create > Story > Item** and add them to the
controller's **Available Items**. `Director Name` is the stable English name used
by `uses_item`; `Display Name` can be localized independently.

## Story and presentation integration

Flow: `SelectingItems → Opening → AwaitingChoice → WaitingForNextScene →
AwaitingChoice / Ended`.

- `StateChanged`: refresh HUD, inventory, and button availability.
- `ScenePresented(StoryScene)`: narration, choices, and video/audio prompts.
  Respect `transition` (`cut` / `morph`). An ending disables further choices.
- `Choose(index)`: apply an available choice exactly once. Roommate choices do
  not count toward the decision limit. Other choices advance the decision count.
  Distance cannot go below zero, wellness stays within 0–100, and `uses_item`
  consumes an owned item by case-insensitive Director name.
- `OutcomePresented(StoryChoice)`: show outcome narration and morph to
  `outcome_video_prompt` while the next scene is requested.
- `NextSceneRequested(int requestId)`: the future Director client calls
  `GetDirectorInput()` and sends it to the AI service. On Unity's main thread,
  return the scene with `SubmitNextScene(requestId, scene)` or call
  `ReportDirectorError(requestId, message)`.
- `RetryNextScene()`: request again without reapplying the outcome. Keep the
  original request ID with each asynchronous request: reset/retry invalidates
  late responses. Accepted scenes wait until the outcome dwell has elapsed.
- `ResetStory()` clears the run and returns to item selection. Disabling or
  enabling the controller also resets it. Timers use unscaled time.

### Checkpoints

The story follows `StoryController.Checkpoints`: 1 dorm (fixed `OpeningScenes.json`),
2 Memorial Church, 3 launch pad, 4 low orbit, 5 approaching the Moon (one Director beat each),
6 Moon landing (ending). `Checkpoint` holds the current one and `CheckpointEntered(int)` fires
when a scene at a new checkpoint is presented: connect it to `GameManager.ShowStage(int)` for
1-5 and the ending screen for 6. The Director input carries `checkpoint_number`,
`checkpoint` and `next_checkpoint`; `must_end` is set when the next beat is the landing.
Director scenes may include `arrival_video_prompt` (the journey into the checkpoint), which
`ViskoStoryBridge` plays before morphing to `video_prompt`.

DTO field names in `StoryData.cs` match `WebTest/README.md`. `OpeningScenes.json`
is also loaded by `WebTest/src/story.js` (inlined by its Vite config), so edit it only here.
Unlike WebTest's advisory prompt alone, this controller rejects a non-ending
response once distance or the decision budget is exhausted, allowing retry.
Wellness reaching zero does not automatically end the story, matching WebTest.

## Single-area inventory tray

The 1114 scene uses three independent `SnapInteractable` sockets under `Inventory Tray/Position 1–3`, one per placeholder. Each has its own BoxCollider (compact scene layout: 0.22 × 0.28 × 0.24m) and accepts one item. `StoryInventorySlot` supplies that slot's pose; `StoryInventoryTray` coordinates eligibility, occupancy and bob timing. Release an item in a specific slot to dock there, in any order. Occupied slots and gaps reject drops without redirecting to a different empty slot. `StorySocketSelection` reads all three sockets into StoryController. The old root socket and collider are removed. Use `Tools > Story > Configure Three Inventory Sockets` to migrate an existing tray.
LunchBag uses the existing `Mystery paper bag` story definition (Director contract
unchanged). Scene model names do not change the inventory IDs.

- The tray and five props are preplaced and visible in Edit Mode, assigned to the
  prologue's `Reveal Objects` with `Video Ended`. GameManager hides them on entering
  Play Mode and enables the same objects after the video; it does not spawn them.
  Use `Tools > Story > Show Inventory Layout In Editor` to reveal them for editing.
  The controller stays outside those objects.
- Move **Inventory Tray** to position the complete tray. Keep its scale at one;
  resize **Base Visual** and each **Position 1–3 > Box Collider** separately. Move the position transforms to move their socket and placeholder together.
- Adjust each prop's **Tray Snap Anchor** to tune its bottom alignment/orientation.
- `Tools > Story > Configure Inventory Tray` creates the setup in an unconfigured
  1114 scene, preserving existing prop transforms. Re-running leaves it intact.
- In Play Mode, `Tools > Story > Validate Inventory Tray (Play Mode)` exercises
  real Meta pointer release/snap events with synthetic grabs. Exit Play Mode
  afterwards to restore the scene. This does not replace Quest hand tracking QA.
- Confirm selection through `StorySocketSelection.StartStory()`; physical movement
  after story confirmation does not alter the locked story inventory.

### Inventory sounds

`Tools > Story > Configure Inventory Audio` wires the installed Meta sample clips
into preplaced child objects: **Grab Audio** on each prop and **Dock Audio** on the
tray. Edit their **AudioTrigger > Audio Clips / Volume** in Inspector. AudioSource
uses Unity 3D attenuation, no external spatializer required, with autoplay off.
`StoryItemGrabAudio` excludes the SnapInteractor's own pointer so docking does not
play another grab sound. `StoryInventoryTray.ItemDocked` fires only for an accepted
free position; releasing outside or onto a full tray stays silent. No audio objects
are instantiated at runtime.

### Ray grabbing

The five inventory props use **Inventory Ray Grab** children: Meta RayInteractable,
ColliderSurface and MoveFromTargetAtAnchorProvider, matching the scene's existing
ray-grab props. Aim the rig's hand/controller ray, hold its select gesture/button,
move the ray to carry the object at its grabbed distance, then release over the
tray. Direct GrabInteractable and HandGrabInteractable inputs on these props are
disabled; Grabbable, SnapInteractor and audio remain shared. This is ray dragging,
not a distance-grab pull-to-hand. All components are saved in the scene.
`Tools > Story > Configure Inventory Ray Grab` reapplies this setup without duplicates.

### Drop ghost preview

`StoryInventoryGhost` on Inventory Tray previews the first available snap position
while a held item hovers in an empty slot’s drop zone. It queries that socket’s pose
logic as actual docking, including each prop's Tray Snap Anchor offset and rotation.
It hides on release, exit, full capacity, selection end or tray disable. If two
items hover simultaneously, only the one nearest its destination is previewed.
No inventory selection is made by the preview.

All five mesh-only ghosts are preplaced under **Inventory Tray > Ghost Previews**;
they contain no colliders, grab components, or audio. Runtime only positions and
shows/hides them. Change **Assets/Materials/Inventory Ghost.mat > Base Map color / alpha**
to tune tint and transparency. The setup menu is **Tools > Story > Configure Inventory
Ghost Preview**. Meshes reflect the prop hierarchy when configured; replacing a
model later requires rebuilding its corresponding preplaced ghost visuals.

### Opaque black glass tray

**Inventory Tray > Base Visual** uses `Assets/Materials/Inventory Black Glass.mat`
(URP Lit, Opaque, smoothness 0.94). A preplaced **Glass Rim** mesh supplies the thin
olive edge with `Inventory Glass Rim.mat`. Edit Base Map color and Smoothness on
these materials to tune the finish; keep Surface Type Opaque. Lighting/environment
reflections determine the visible highlights. `Tools > Story > Apply Black Glass
Tray` applies the assets without changing tray transforms, colliders or snap poses.

### Empty slot placeholders

`Tools > Story > Configure Empty Slot Placeholders` authors three dashed ivory spheres with a `+` under the tray’s `Position 1/2/3 > Empty Slot Placeholder`. These objects and their shared mesh/materials are created in the Editor, never spawned during play. `StoryInventoryPlaceholders` shows only empty positions during item selection, hides the position currently displaying the item ghost, and restores the placeholder when an item is removed. The plus faces the main camera. Adjust each placeholder root scale for size, and `Inventory Placeholder Outline` / `Inventory Placeholder Plus` materials for color and opacity. The placeholders have no colliders and cannot intercept ray grabs.

### Selection group

In scene 1114, `Item Selection` is the preplaced parent of the five props, `Inventory Tray` (including ghosts/placeholders/audio), and `Item Selection Panel`. Move this parent to reposition the whole selection layout. GameManager's prologue `revealObjects` references this parent, with `revealWhen = VideoEnded`: it hides at startup and activates after the prologue ends, without Instantiate. Keep Story Controller, video, and XR rig outside this group. `Tools > Story > Group Item Selection Objects` preserves world transforms and updates the reveal reference.

### Item display motion

The five props have `StoryItemDisplayMotion`: idle props lean right 12 degrees and revolve once every 20 seconds. External ray selects pause idle animation immediately; Meta socket selection stops rotation and uses the tray's snap pose to bob the whole prop upward 0–2.5cm over a 3-second cycle. This keeps renderers, colliders and grab/snap anchors together, without changing the three authored slot transforms or ghost destinations. Inspector fields control tilt, rotation speed, bob height and period per item. Motion runs only in Play Mode. `Tools > Story > Configure Item Display Motion` assigns existing scene props without spawning anything at runtime.

### Item home positions

`Item Selection/Item Home Positions` contains five preplaced `… Home` transforms. Each prop's `StoryItemDisplayMotion.Home` sets its starting position and return destination. Move these transforms in the Editor to change the home layout; move the entire Item Selection parent to move everything together. An external grab pauses motion; release outside the tray (including removal from a tray slot or rejection by a full tray) returns the prop smoothly home and resumes its idle rotation. A 0.2-second release grace lets Meta accept a valid tray drop first. Occupied tray items retain socket bobbing. Homes do not count toward StoryController inventory and require no extra sockets or runtime spawning. Return Speed is configurable per prop.

### Compact tray layout

`Tools > Story > Apply Compact Inventory Layout` keeps the oval black-glass design but sets the visual to 84×38cm, 2.4cm thick, with slot centers 25cm apart. Empty markers use 75% scale and each slot collider is 22cm wide, leaving 3cm gaps. Only the tray visual, slot positions, marker scale and slot colliders change; prop/home transforms and overall Item Selection transform are preserved.

## OpenAI Director client

`DirectorClient` answers `NextSceneRequested` by sending `GetDirectorInput()` to the OpenAI
Responses API (structured JSON output with the WebTest Scene schema) and returning the result
through `SubmitNextScene`. It runs on Quest directly through `ReactorHttps`; no PC or server.

1. **Tools > Director > Local OpenAI Key** stores the key in Git-ignored
   `Assets/Resources/OpenAILocalConfig.json` (included only in local builds).
2. Add `DirectorClient` next to `StoryController` and assign **System Prompt**
   (`Assets/Story/DirectorSystemPrompt.txt`, the WebTest Director prompt; edit the story rules there).
3. Defaults: model `gpt-5.5`, reasoning `low`, one automatic retry. After that the error goes to
   `ReportDirectorError`; call `StoryController.RetryNextScene()` from a retry button.
4. **Mock Mode** returns canned scenes without a key or network, for testing the flow.

The console logs each scene's location and latency (about 10–15 s; it overlaps the outcome dwell).

## Visko story bridge

`ViskoStoryBridge` plays the story on the live Visko stream: `ScenePresented` cuts (new place)
or morphs (same place) to `video_prompt`; `OutcomePresented` morphs to `outcome_video_prompt`
while the Director writes the next beat. A shared style line is appended to every prompt, and
`generation_complete` restarts generation so the picture keeps running.

- Assign StoryController, the ViskoStreamClient (`Assets/Prefabs/Visko Stream.prefab`, key via
  **Tools > Visko > Local API Key**) and SharedVideoPlayer. The bridge sets the client to
  external-screen mode and disables its own auto-connect/auto-start.
- **Connect On Start** opens the paid session immediately. To shorten paid time, disable it and
  wire the Start button to `ViskoStoryBridge.Connect()`: placement takes ~10–30 s, so connecting
  during the prologue makes the stream ready by the first scene. Prompts sent before the session
  is ready are kept and sent on connect.
- The stream appears on the shared curved screen through `SharedVideoPlayer.ShowExternal`. Stage
  clips (the prologue) still take precedence; stages without a clip show the live stream.
