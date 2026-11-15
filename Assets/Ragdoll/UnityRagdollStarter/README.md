# Jointed Mannequin — Unity Ragdoll Starter

A segmented white mannequin with orange joints, based on the supplied visual reference. The model starts upright. The included Unity scripts are designed to make it go limp when grabbed, let its limbs hang and swing, and leave it physical after release.

**Start here:** copy the `JointedMannequin` folder from this archive's `Assets` folder into your Unity project's `Assets` folder. Then choose **Tools > Jointed Mannequin > Create Demo Scene**.

## Quest ray grab in TakeMeToTheMoon

Use **Tools > Jointed Mannequin > Add Quest Ray Ragdoll to Scene**, then position the preplaced object. The generated prefab is under this package's `Assets/JointedMannequin/Prefabs` folder. The existing scene must contain the Meta hand/controller RayInteractors (1114 already does).

Point at any body part and hold select/pinch. All 15 collider surfaces feed one Meta `Grabbable`. `JointedRagdollRayGrab` holds the head kinematic and smoothly moves it toward the selecting ray's hand/controller origin in FixedUpdate, while the remaining bodies swing under gravity. Contact and joint projection distances are adjusted to the authored scale at startup. Release drops it; `JointedRagdoll.ResetPose()` restores standing. No Inspector hand anchors or manual event wiring are needed. Do not add a root Rigidbody or a default grab transformer; the custom transformer drives an external physics anchor and leaves the root stationary.

The sections below describe the original standalone starter; this project's builder now includes Meta SDK ray integration and uses the package's nested model path.

## What is included

| Item | Purpose |
| --- | --- |
| `Assets/JointedMannequin/Models/JointedMannequin.fbx` | Binary FBX containing 15 separate named mesh sections, with materials. |
| `Editor/JointedMannequinBuilder.cs` | Unity menu commands that build the physics prefab and a demonstration scene. |
| `Scripts/JointedRagdoll.cs` | Standing, grab, drop, and reset behavior. |
| `Scripts/JointedRagdollGrabBridge.cs` | Optional Inspector event adapter for an existing hand/pinch system. |
| `Scripts/JointedRagdollMouseDemo.cs` | Desktop mouse-drag demonstration. |
| `Source/JointedMannequin.blend` | Editable Blender model. |
| `Source/build_mannequin.py` and `Source/ragdoll_spec.json` | Procedural modeling source and body/joint specification. |
| `MannequinPreview.png` | Render of the actual model, before Unity physics. |

The archive contains a **prefab builder**, not a prebuilt Unity prefab. The menu command creates and saves the prefab using your Unity project's own importer and material pipeline.

## Requirements and validation

- The C# source targets Unity **2022.3 or later, including Unity 6**. This is an intended compatibility target, not a claim of testing in every version.
- The core model and physics controller have no XR or hand-tracking package dependency.
- The builder selects materials for Built-in, URP, or HDRP. Lighting appearance will depend on your project. A custom render pipeline needs a matching shader in the builder.
- The optional mouse controls need **Active Input Handling = Input Manager (Old)** or **Both**. The core ragdoll and hand-event bridge do not use the legacy input backend.
- The model was exported as binary FBX 7400 and imported back into Blender 4.3.2. The roundtrip check passed for all **15 named mesh sections, 11,586 vertices, 23,032 triangles**, populated materials, and matching object pivots. The FBX is **488,220 bytes**.
- The Unity scripts received an independent source review. **Unity was not available in the creation environment, so Unity compilation, Play Mode, and headset interaction have not been tested.** Test the demo in your project before integrating it into gameplay.

## 1. Import the package

1. Extract `UnityRagdollStarter.zip`.
2. Open your Unity project.
3. Copy the archive's `Assets/JointedMannequin` folder into the project's existing `Assets` folder. The resulting model path must be `Assets/JointedMannequin/Models/JointedMannequin.fbx`.
4. Wait for Unity to import the FBX and compile the scripts.
5. Keep the `Editor` subfolder in place. Its scripts use Unity Editor APIs and are excluded from player builds by their location and compilation guard.

You do not need Blender installed to import the FBX. Keep the `Source` folder outside your Unity project's `Assets` folder unless you deliberately want to work with the Blender file there.

## 2. Create and try the demo

1. Choose **Tools > Jointed Mannequin > Create Demo Scene**.
2. Unity will offer to save changes to any currently modified scene.
3. The command creates a prefab if needed, then a new scene with a floor, camera, light, mannequin, and mouse controls. It saves the scene under `Assets/JointedMannequin/Demo`.
4. Press **Play**, then focus the **Game** view.
5. Hold the **left mouse button** on the torso or another body part and drag it upward. Release to drop it.
6. Use the **mouse wheel** to change grab distance, **R** to restore the standing pose, or **Space** to make it collapse. The on-screen buttons also reset or activate the ragdoll.

If mouse dragging is unavailable, check **Edit > Project Settings > Player > Other Settings > Configuration > Active Input Handling**. The optional mouse demo needs `Input Manager (Old)` or `Both`; Unity may require a restart after a change. You can instead leave your project's input setting as it is and connect your existing hand/controller input to the core ragdoll.

To create only the prefab, choose **Tools > Jointed Mannequin > Create Ragdoll Prefab**. It is saved in `Assets/JointedMannequin/Prefabs`. Rebuilding preserves earlier prefabs by creating a unique new filename. The demo uses the original `JointedMannequin.prefab` if it already exists; drag a newly generated variant into the scene if you want to test that variant.

## 3. Connect a real hand pinch

The mannequin provides the physical response. Your existing XR system provides a continuously tracked pinch pose and identifies which object the player selected. Installing these files alone does not enable hand tracking.

For a first version, use one hand and grab the chest:

1. Put the generated mannequin prefab in your scene.
2. Add **Jointed Ragdoll Grab Bridge** to a convenient scene GameObject.
3. Assign the mannequin instance to **Ragdoll**.
4. Assign your tracked pinch-position Transform to **Pinch Anchor**. This Transform must be outside the mannequin hierarchy and update as the hand moves.
5. Leave **Body To Grab** empty to use the chest. Leave **Contact Point** empty to use its center of mass; an actual selected surface point can be assigned for a more precise grip.
6. Connect the hand system's **select/grab started for this mannequin** event to `JointedRagdollGrabBridge.BeginPinch()`.
7. Connect the matching **select/grab ended** event to `JointedRagdollGrabBridge.EndPinch()`.

Use object-qualified selection events. Connecting a global raw pinch gesture would grab the mannequin even when the player was pinching somewhere else. The bridge does not detect gestures, perform reach checks, or choose a target.

For integration in your own C# input adapter, the core calls are:

```csharp
using JointedMannequin;

// The hand system has already selected this mannequin.
ragdoll.BeginGrab(trackedPinchTransform);

// On the matching release event:
ragdoll.EndGrab();
```

For a specific body part and surface contact:

```csharp
ragdoll.BeginGrab(selectedRigidbody, trackedPinchTransform, worldContactPoint);
```

`BeginGrab` returns whether the grab was accepted. The first active hand owns the grab; a second hand cannot take it over until release. The initial hand-to-contact offset is preserved so selection does not teleport the doll. If your input stack already moves grabbed objects, configure it to report selection without also moving this mannequin's root. The ragdoll controller must own the physical attachment.

## How the physics is arranged

The prefab has **15 sibling physics bodies and 14 body joints**. Each body contains its visual mesh and a simple box or capsule collider. Upper arms, forearms, thighs, and shins move independently. Orange joint spheres are part of the visible meshes; the invisible Unity joints enforce the connection and rotation limits.

Before grabbing, the body rigidbodies are kinematic and hold their authored standing pose. Grabbing enables dynamic physics and creates a temporary point attachment between the selected body and an invisible kinematic handle. The handle follows the tracked pinch in `FixedUpdate`, with a movement-speed cap to soften tracking jumps. The rest of the body responds to gravity and joint constraints. Releasing removes only this temporary hand attachment and preserves the bodies' simulated motion.

The character root stays stationary during simulation. Do not parent the entire ragdoll to a hand, move the whole root every frame, or add a single root Rigidbody to drive all sections. Place or rotate the root before Play Mode as needed; keep the physics root at scale `(1, 1, 1)` for the supplied tuning.

The model has separate rigid sections and **no skinning armature or Humanoid Avatar**. That is intentional for this toy style. You can inspect and reposition the named mesh parts in Blender. A continuous human skin/clothing model would require a weighted skeleton and a different visual setup.

## Starter limitations and tuning

- **Self-collisions are disabled by default.** The sections collide with the floor and other environment objects, but limbs can overlap one another. This reduces self-collision instability. Enabling selected self-collisions is a later tuning step; avoid overlapping adjacent colliders.
- **Standing is a held pose.** The doll does not physically balance or stand itself back up. `ResetPose()` restores the original pose and freezes it; it is an explicit reset rather than a get-up animation.
- **One hand at a time.** Two-handed pulling needs additional grab-ownership and constraint logic.
- **About 1.77 meters tall.** The geometry and physics values use meter-scale dimensions. For a small tabletop version, rebuild the geometry, colliders, anchors, and physics values consistently rather than non-uniformly scaling a live ragdoll.
- This starter uses approximate box/capsule colliders, so small surface gaps or slight visual penetration can occur. Contact and joint tuning should be checked at your game's intended speed and scale.
- A tracking jump is capped, so the doll can lag behind a very fast hand. Adjust **Maximum Handle Speed** on `JointedRagdoll` if your interaction needs it.
- Some projects need different damping, solver iterations, or collision layers. Start with the supplied settings, and change them only to address an observed issue in Play Mode.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No Tools > Jointed Mannequin menu | Wait for compilation and inspect Unity's Console for errors. Confirm the scripts were copied into `Assets/JointedMannequin`. |
| Model looks empty in the scene | Drag the FBX or generated prefab from the Project window into the Hierarchy, select it, and press F in Scene view. The FBX is verified to contain 15 meshes. |
| Model exists but is stiff | Use the generated prefab, not only the FBX. Enter Play Mode and call `BeginGrab` or `SetDynamic`. |
| It collapses before being grabbed | Ensure **Start Frozen** is enabled and no other script calls `SetDynamic`. |
| It stays rigid while held | Confirm another grab/animation script is not moving the whole model or writing its body transforms. |
| Pinching does nothing | Confirm the hand SDK is tracking, selecting this object, and invoking the bridge; verify its Ragdoll and Pinch Anchor references. |
| Pink materials | Run the prefab builder in your project's active render pipeline and use the resulting prefab. |
| Imported section does not match the layout | Use the supplied FBX unchanged. The builder expects its named parts, object origins, and proportions. |
| It will not stand after dropping | Use R, the reset button, or `ResetPose()`. A passive ragdoll does not balance itself. |

## Editing or regenerating the model

Open `Source/JointedMannequin.blend` in Blender. Preserve the 15 object names and origins if you want to use the existing builder unchanged. Colors are material assignments and do not require external texture files.

To rebuild the supplied model with Blender 4.3:

```bash
blender --background --python Source/build_mannequin.py -- /absolute/path/to/output
```

The script expects `ragdoll_spec.json` next to it. It generates the FBX, editable Blender file, model metrics, and a preview. Changes to body proportions or joint locations also require matching changes to `GetParts()` in the Unity editor builder.

## Technical references

- Unity: [Create a ragdoll](https://docs.unity3d.com/6000.0/Documentation/Manual/wizard-RagdollWizard.html)
- Unity: [Rigidbody.isKinematic](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-isKinematic.html)
- Unity: [Joint and ragdoll stability](https://docs.unity3d.com/6000.0/Documentation/Manual/RagdollStability.html)
- Blender: [FBX export operator](https://docs.blender.org/api/4.3/bpy.ops.export_scene.html)

These references explain the underlying physics and export concepts. This package uses its own segmented-model builder rather than Unity's skinned-character Ragdoll Wizard.
