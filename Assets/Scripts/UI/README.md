# Narration Canvas

Drag `Assets/Prefabs/Narration Canvas.prefab` into the scene, or use
**GameObject > UI > Narration Canvas** to place it in front of the main camera.
The world-space canvas is 1.2 metres wide at its default scale, with an opaque
black panel, white centered text, and the existing dynamic Korean font.
Text wraps at the chosen Font Size (64 by default). `NarrationPanel` disables
Auto Size and fits the canvas height to the text plus its vertical padding,
both in edit mode and at runtime. Change Font Size on `Narration Text` to resize
the text and panel together. The default padding is 24 above and below, and
Minimum Height is 100 canvas units. Width remains fixed; long narration adds
lines. Split long narration into passages to keep the panel comfortable in VR.

Assign the root `NarrationPanel` component to a serialized field in your manager:

```csharp
using TakeMeToTheMoon.UI;

[SerializeField] private NarrationPanel narration;

// When new narration is available:
narration.SetText("달을 향한 우리의 이야기가 시작됩니다.");
narration.Show();
// narration.Hide();
// narration.Clear();
```

`SetText` does not change visibility. `Hide` only disables the panel child so the
root component remains available. The methods can also be called by UnityEvents.
This component owns presentation only; route story progression through
`StoryController` and supply text from its events when connecting the manager.
No input raycaster is needed for this display-only canvas.

## Countdown timer

`Assets/Prefabs/Countdown Timer.prefab` is a separate world-space badge with a
red gradient circular progress ring with EastSeaDokdo text, remaining whole seconds, and SEC label. Place it
above your panel; it does not include the dialog panel or a Continue button.
The initial transform assumes floor coordinates (Y 1.6, Z 2 metres); adjust Y for
an Eye Level rig. If nesting inside an existing scaled Canvas, use local scale 1
and position in that Canvas's units instead of applying the world-space scale twice.

Set Duration Seconds (default 15) and Start On Enable. It uses unscaled game time,
shows 0 when finished, and emits Completed once per run without hiding the UI or
advancing GameManager/StoryController. Disabling stops it; enabling resets it.
StartTimer restarts; PauseTimer/ResumeTimer control a running countdown;
ResetTimer restores the duration without starting. CompleteNow finishes a running
timer once, and can optionally be wired to a Continue button. Connect Completed
in the Inspector when a presentation action is needed. Story progression should
still go through StoryController's public methods.

The ring is a UI mesh, so no new image files are needed. Colors are editable on
Disc/Track/Progress and typography on Seconds/Unit. All graphics ignore raycasts.
Recreate a missing prefab using Tools > Game Flow > Create Countdown Timer Prefab.

On Progress, Color controls the top color; Bottom Color controls the bottom.
Gradient Enabled toggles the vertical gradient without changing countdown behavior.

If using OVROverlayCanvas on the timer, disable Dynamic Resolution to prevent
Play Mode from reducing its render texture viewport. The 1114 timer uses a fixed
1024 maximum texture size with redraw every frame for the animated ring.

## Shared panel background

`Assets/Prefabs/Panel Base.prefab` is a UI fragment for an existing Canvas, not a
standalone world-space Canvas. Drag it under a Canvas or select a Canvas and use
GameObject > UI > Panel Base. Resize the root RectTransform Width/Height, leaving
local scale at 1. Background stretches with it using Canvas Panel.png and 24px
sprite borders (Image.Type.Sliced). Content stretches with 24-unit padding for
text, icons and buttons. Background ignores raycasts; interactive children need
the existing Meta Canvas interaction setup. Create Prefab Variants for specific
panels to inherit the shared design. The source image includes noise, which
stretches with the center; it is not a separate tiled texture.

## Intro panel and primary action

`Intro Panel.prefab` recreates the Breaking News layout with nested `Panel Base`
and `Primary Button` prefab instances. Drag Intro Panel into the scene (it already
has a world-space Canvas). Its 1.5m wide layout defaults to Y=0 for an Eye Level
rig, Z=2; adjust the root for your scene. Text objects under Panel Base/Content
are editable independently. Rubik Dirt is used for display labels, and the TMP
project default is used for body text.

Wire IntroPanel.ContinueRequested to the intended action in the Inspector. It
fires once per enable cycle and disables the Continue button; it does not advance
GameManager or StoryController on its own. A new enable cycle resets the button.
Meta PointableCanvas and RayInteractable are included, but the scene must provide
its existing ray interaction rig and EventSystem with PointableCanvasModule.
No additional rig/EventSystem is created by this prefab.

Primary Button is a reusable UI button with hover/press tint, a temporary solid
orange background and separate text. Replace its background with the final
9-slice sprite when supplied; add a separate transparent rocket icon. Suggested
source assets: blank button PNG 512x128 (no text/icon), rocket PNG 128x128 with
transparent background. Rebuild missing assets through Tools > Game Flow >
Create Intro Panel Prefabs. Existing assets are preserved.

### Editable intro text and automatic height

Select one Intro Panel root. Its IntroPanel Inspector exposes Title and Body text
editors (stored directly in their TMP components, with Undo/prefab overrides).
You can also edit the child TMP fields or call SetTitle(string)/SetBody(string).
Keep Panel Base/Content and Title/Body/Hint/Prologue Badge child names intact;
the layout resolves these existing nodes to support previously placed instances.

Canvas width stays manual; Canvas height follows wrapped text at its chosen font
size in Edit and Play modes. Auto Size is disabled. Minimum Height, padding and
spacing are editable on IntroPanel. Footer moves below the body and stacks at
narrow widths. Panel Base and Meta interaction bounds stretch with the Canvas.
Set root Pivot Y to 1 if the top edge should stay fixed as height changes; the
existing center pivot expands around the center. Very long passages keep growing
(no scroll view or maximum height). Split long VR copy into passages as needed.

Tools > Game Flow > Validate Intro Panel Layout checks growth, shrinkage, narrow
wrapping and footer separation on an isolated prefab copy without saving changes.

## Step instruction banner

`Assets/Prefabs/Step Panel.prefab` is a standalone world-space Canvas using the
shared Panel Base and Rubik Dirt display font. Drag into the scene and position
it above selection UI. Defaults: width 1100 UI units, scale .0015, Y=0/Z=2 for an
Eye Level origin. Adjust Width manually; height follows wrapped text in Edit and
Play modes. Minimum Height and padding/spacing are available on StepPanel.

Edit Step Label, Progress Label and Instruction on the root StepPanel component;
these fields own the displayed copy (child TMP text is refreshed from them).
SetStepLabel(string), SetProgressLabel(string), SetProgress(current,total), and
SetInstruction(string) update it at runtime. Empty step text hides the badge.
The component displays caller-supplied progression only; it does not advance or
store a separate story run. All graphics ignore raycasts and require no Meta
interactable. Long text grows without a height cap. Use Pivot Y=1 for top-fixed
height changes. Tools > Game Flow > Validate Step Panel Layout checks growth,
shrinkage and empty badge handling without changing the source prefab.

## Automatic UI input selection

1114's EventSystem has UIInputModeSwitcher. At runtime it runs before normal
input components: Editor/desktop enables InputSystemUIInputModule and disables
PointableCanvasModule plus its exclusive mode; Android enables the Meta module
with exclusive mode and disables mouse UI. Leave both modules present/enabled in
the saved scene. No manual module toggling is needed before building.

For Quest Link/Meta simulation in the Editor, enable Use Meta Input In Editor on
the switcher before Play. Otherwise leave it off for mouse testing. The selection
is applied on Awake/OnEnable; Apply Input Mode in its component context menu can
reapply it. It does not change hand/controller rig or Canvas interaction setup.
Other scenes can use Tools > Game Flow > Configure Automatic UI Input, then save.

## Item selection instructions

`Assets/Prefabs/Item Selection Panel.prefab` matches the selection reference with
an olive-black textured panel, Rubik Dirt heading and East Sea Dokdo body text.
The 1114 scene contains a preplaced **Item Selection Panel**, enabled in Edit Mode
and assigned to GameManager's prologue Reveal Objects. It appears with the items
when the prologue finishes; no runtime instantiation is used. Move/scale the root
in the Hierarchy and edit **Title** / **Body** TMP children in Inspector. Its graphics
ignore raycasts and it has no raycaster, so it does not intercept inventory rays.
`Tools > Story > Create Item Selection Panel` creates a missing prefab/scene instance
and connects reveal without adding duplicates. It also renders a layout preview
at `/tmp/item-selection-panel-preview.png` for visual inspection.

### Choice card buttons

`Assets/Prefabs/Green Card Button.prefab` and `Blue Card Button.prefab` use the supplied 3x GreenCard/BlueCard artwork. The clickable root is 360x232 UI units. Its non-raycasting Background is 440x316, offset down 14 units to preserve the full 1320x948 image including its asymmetric shadow. Do not Set Native Size on the root or squeeze the full artwork into the body rectangle. Edit the TMP `Label` child and configure the root `Button > On Click` in the Inspector. Buttons include hover/pressed/disabled tint feedback; no extra shadow component is needed.

`Choice Cards.prefab` is an optional preconfigured World Space Canvas containing both button prefab instances and Meta ray Canvas interaction; place it in the Hierarchy or under an authored stage screen before play. Use `UIInputModeSwitcher` on the scene EventSystem. Individual button prefabs instead belong under an existing interactive Canvas. They contain no progression logic or runtime spawning: connect story choices to `StoryController.Choose(int)` with indices 0 and 1 where appropriate, and add the group to the stage's reveal targets. `Tools > Game Flow > Create Choice Card Buttons` creates missing prefabs and renders `/tmp/choice-cards-preview.png` without overwriting edited prefabs.

### Wellness bar

`Assets/Prefabs/Wellness Bar.prefab` is a non-interactive World Space Canvas (480x186 UI units) using `heart.png`, `Wellness Bar_BG.png`, and an editable Rubik Dirt numeric label. Keep the preplaced scene instance outside stage screen roots so it stays active across transitions; no runtime creation is needed. `WellnessBar` follows the scene StoryController's `Wellness` and `StateChanged` by default; the Controller field may be assigned explicitly, otherwise it finds the scene controller on enable. It never changes story state.

In 1114, `PalmWellnessDisplay` reads both assigned Meta `Hand` sources through IHand. Either palm can reveal the single bar; while both are up, it stays on the current hand. Lowering that hand or losing tracking transfers to the other ready hand without moving the panel across the space between hands. Turn the palm upward for 0.15 seconds to reveal the bar 22cm above the palm; turning down or losing high-confidence tracking hides it. Show/hide dot thresholds are 0.65/0.35 to prevent flicker. The bar follows the palm smoothly and faces the eye camera. The start screen hides the badge; after entering the prologue, GameManager permits palm-up visibility. Scale 0.00024 gives an 11.52cm-wide panel. CanvasGroup visibility leaves the existing wellness value and heartbeat component active and does not intercept rays. `Tools > Game Flow > Configure Palm Wellness` wires the preplaced bar and disables the previous HeadFollowingHUD. Verify gesture angles on Quest with hand tracking enabled.

The Heart child uses a two-beat scale pulse, with a pause between cycles. Defaults: 55 BPM at 100 wellness, 150 BPM at 0, 15% scale growth. Tune Healthy Bpm, Low Wellness Bpm and Pulse Amount. For a standalone demo, disable Follow Story and edit Preview Value (0–100), or invoke `SetValue(int)` from a UnityEvent. Animation runs in Play Mode; number previews update in the Inspector. Background and number stay stationary, and all graphics ignore raycasts. `Tools > Game Flow > Create Wellness Bar Prefab` creates the prefab and renders `/tmp/wellness-bar-preview.png`; `Validate Wellness Heartbeat (Play Mode)` tests a temporary instance and leaves story state unchanged.

### Inventory Continue

The preplaced `Item Selection/Inventory Continue` canvas contains a Primary Button prefab and Meta PointableCanvas ray interaction. `InventoryContinueButton` listens to StoryController.StateChanged and shows the button only when CanStart is true. Removing an item hides it again. Clicking refreshes the physical socket selection, starts the story through TryStartStory, and only on success calls GameManager.ShowMain. Keep this canvas under Item Selection so prologue reveal and stage transitions control its lifetime. Configure it in Edit Mode with `Tools > Story > Configure Inventory Continue Button`; no UI is spawned at runtime. The inventory Play Mode validation covers empty, partial, full, undocked, and replacement visibility.

### Stage 1 roommate choice

`Stage 1/Stage 1 Choice Panel` is a preplaced World Space Canvas with the situation header and existing green/blue card prefabs. GameManager stage 1 uses VideoEnded reveal. RoommateChoicePanel advances an unfinished story opening when revealed, then routes Wake/Leave to StoryController choices 0/1. Successful choice hides the panel; StoryController owns wellness, roommate state and Director requests. It does not independently advance GameManager stages. The shared video remains outside Stage 1. Configure via Tools > Game Flow > Configure Stage 1 Choice Panel. Validate Stage 1 Choices (Play Mode) tests isolated choice state then previews the actual reveal layout.

### Fuel instruction panel

`Assets/Prefabs/Fuel Instruction Panel.prefab` is a prefab-only World Space Canvas: editable title/instructions, THROWS LEFT counter, and three replaceable icon slots. FuelInstructionPanel is presentation only; the eventual minigame supplies `SetThrowsLeft(int)` (clamped 0–3). Used slots dim unless Used Fuel Artwork is provided. Assign Panel Artwork, Counter Artwork, Fuel Artwork and optional Used Fuel Artwork as Sprites; the counter uses Throws Panel.png, remaining attempts use Can (ready).png, and spent attempts use Can (used).png. The orange placeholder marks are hidden when artwork is assigned. No scene/stage or throw mechanics are connected yet. Tools > Game Flow > Create Fuel Instruction Panel Prefab creates it without replacing an existing prefab, applies the supplied artwork when available, validates text fit/count bounds and all four ready/used states, and renders `/tmp/fuel-panel-preview.png`.

Wellness badge background uses the supplied Wellness Bar-R/Y/G sprites: 0–29 red, 30–69 orange (Y artwork), 70–100 green. WellnessBar.Refresh updates the image alongside the number, including story events and Inspector preview.
