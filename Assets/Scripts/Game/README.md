# Simple game flow

Add **GameObject > Story > Game Manager** to the scene, or drag in
`Assets/Prefabs/Game Flow/Game Manager.prefab`.

Only GameManager is required: Start Screen → Main → End Screen.
No GameStage or StoryController is required for this presentation flow.

- Assign Start Screen / Main / End Screen Skybox materials. Null uses the original scene skybox.
- Start Screen, Main, and End Screen prefabs are assigned on the manager prefab.
  Main starts as an empty root: place main-only background objects inside it.
  Existing scene instances with a Main Prefab override may need Main assigned manually.
- Keep video, narration, XR rig, and service objects in the scene. The manager
  does not create, disable, reset, or reconnect them.
- Buttons can call StartGame(), ShowEnding(), ReturnToStart(), or NextStage().
  GameFlowScreen only binds buttons inside spawned screen prefabs; it owns no state.
- In Play Mode, use the manager Inspector buttons to preview all three states.
- Existing start/end canvases use standard UI Buttons. Quest interaction requires
  the scene's Meta XR UI input setup. Start Screen uses the transparent Logo, Start Button, and Hint sprites from
  `Assets/Images/Main`, with a clickable START button and no background panel.
- StoryController remains available for inventory, choices and Director logic;
  this manager does not start/reset it. Integrations should use its public API.
- StateChanged reports presentation changes. No choice timer or choice UI is included.
- Drag preplaced Start/Main/End roots from the Hierarchy into the manager's three
  screen fields (still named Prefab for compatibility). These are reused with
  SetActive: positions, rotation, scale, and parenting stay unchanged. At startup
  the manager hides all assigned scene screens, then shows Start. Keep the manager
  outside these screen roots, and use separate roots for each screen.
- Project prefab assets are still instantiated and destroyed when switching.
  Content Root affects only these spawned assets. Disabling the manager hides
  scene screens or destroys owned instances and restores the original skybox.

The setup menu does not create StoryController or modify existing video objects.

Use **Tools > Game Flow > Apply Start Screen Artwork** to rebuild the start Canvas
from these three images. This replaces its Canvas children. Mouse testing needs
an EventSystem with InputSystemUIInputModule. Assign a manually placed screen from the Hierarchy to reuse it without a clone.
Unassigned preview copies remain independent and should be disabled during play.

## Prologue

Run **Tools > Game Flow > Configure Prologue** in a scene with GameManager,
VideoPlayer and CurvedVideoScreen. This attaches PrologueManager to the persistent
GameManager, assigns the video/screen, and turns off Play On Awake and looping.
Keep these objects active and outside the screen roots GameManager toggles.
START now enters Prologue before Main; without an assigned manager the old flow remains.

- **Fade In Seconds**: transparent-to-visible fade (default 2 seconds); floor glow fades with it.
- **Advance When Video Ends**: checked waits for the non-looping clip to end.
- **Duration Seconds**: used only when the checkbox is off; measured from playback start, including fade time.
- **Next Scene Name**: blank shows Main in the current scene; otherwise load a scene included in the Build Profile.
- **Completed**: one event per completed prologue. Returning to Start cancels without firing it.

The video starts loading/playing immediately on START; fade and timer begin when
playback actually starts. A replay starts from the beginning. Video errors and
unavailable scene names log an error and fall back to Main. The transparency uses
the project's URP Unlit video material, cloned at runtime without editing the asset.
