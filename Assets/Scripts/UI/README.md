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
