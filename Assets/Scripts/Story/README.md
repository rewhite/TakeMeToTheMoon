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

DTO field names in `StoryData.cs` match `WebTest/README.md`. `OpeningScenes.json`
copies the fixed scenes from `WebTest/src/story.js`; update both when those change.
Unlike WebTest's advisory prompt alone, this controller rejects a non-ending
response once distance or the decision budget is exhausted, allowing retry.
Wellness reaching zero does not automatically end the story, matching WebTest.
