using System.Collections;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TakeMeToTheMoon.Story
{
    /// <summary>
    /// Runtime glue for the playable story: Start → prologue → timed item grab → dorm → Director
    /// checkpoints, ending at <see cref="lastCheckpoint"/>. Connects GameManager, StoryController,
    /// the Visko bridge, the choice cards and the narration panel without owning any story state.
    /// Configure with Tools > Story > Configure AI Story Flow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryFlowConnector : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private StoryController story;
        [SerializeField] private StorySocketSelection selection;
        [SerializeField] private ViskoStoryBridge visko;

        [Header("Item grab")]
        [Tooltip("Off when the scene's own UI (InventoryContinueButton) starts the story and opens stage 1.")]
        [SerializeField] private bool handleItemGrab;
        [Tooltip("Parent revealed after the prologue (tray, props, panel). The grab timer starts when it appears.")]
        [SerializeField] private GameObject itemSelectionRoot;
        [SerializeField] private CountdownTimer grabTimer;
        [Tooltip("Start this many seconds after all required items are docked, without waiting for the timer.")]
        [SerializeField, Min(0)] private float startWhenFullAfter = 2f;

        [Header("Choices and narration")]
        [SerializeField] private GameObject choiceRoot;
        [SerializeField] private Button[] choiceButtons = new Button[0];
        [SerializeField] private TMP_Text[] choiceLabels = new TMP_Text[0];
        [SerializeField] private NarrationPanel narration;
        [Tooltip("Seconds the scene plays clean before the choices fade in (after any arrival journey).")]
        [SerializeField, Min(0)] private float choicesDelaySeconds = 2.5f;
        [SerializeField, Min(0)] private float choicesFadeSeconds = .4f;

        [Header("Scope")]
        [Tooltip("First checkpoint whose choices, narration and stage change this connector handles. " +
                 "Earlier checkpoints use the scene's own UI (e.g. RoommateChoicePanel at the dorm).")]
        [SerializeField, Range(1, 6)] private int firstAiCheckpoint = 1;
        [Tooltip("The demo stops at this checkpoint: its scene plays, but no choices are offered.")]
        [SerializeField, Range(1, 6)] private int lastCheckpoint = 6;
        [SerializeField] private string endOfDemoText = "To be continued...";
        [Tooltip("Retry the Director automatically after an error, after this many seconds.")]
        [SerializeField, Min(1)] private float retryAfterSeconds = 3f;

        [Header("Debug")]
        [Tooltip("Show a status line above the narration panel in the headset (game, story, Visko, Director).")]
        [SerializeField] private bool showStatusInHeadset;
        [Tooltip("Editor/desktop keys: Space start, G grab 3 items and continue, 1/2 choose, R retry, F1 status line.")]
        [SerializeField] private bool keyboardShortcuts = true;
        [SerializeField] private DirectorClient director;
        [SerializeField] private Visko.ViskoStreamClient viskoClient;

        private bool selectionWasVisible, storyStarted;
        private float fullSince = -1;
        private Coroutine retry;
        private TextMesh statusText;
        private string lastStatus;
        private float waitingSince;
        private Coroutine reveal, choicesReveal;
        private CanvasGroup choiceGroup;

        private void OnEnable()
        {
            if (gameManager != null) gameManager.StateChanged.AddListener(OnGameStateChanged);
            if (grabTimer != null) grabTimer.Completed.AddListener(StartStoryNow);
            if (story != null)
            {
                story.CheckpointEntered.AddListener(OnCheckpointEntered);
                story.ScenePresented.AddListener(OnScenePresented);
                story.OutcomePresented.AddListener(OnOutcomePresented);
                story.ErrorRaised.AddListener(OnError);
            }
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int index = i;
                if (choiceButtons[i] != null) choiceButtons[i].onClick.AddListener(() => Choose(index));
            }
            ShowChoices(false);
            ShowNarration(null);
        }

        private void OnDisable()
        {
            if (gameManager != null) gameManager.StateChanged.RemoveListener(OnGameStateChanged);
            if (grabTimer != null) grabTimer.Completed.RemoveListener(StartStoryNow);
            if (story != null)
            {
                story.CheckpointEntered.RemoveListener(OnCheckpointEntered);
                story.ScenePresented.RemoveListener(OnScenePresented);
                story.OutcomePresented.RemoveListener(OnOutcomePresented);
                story.ErrorRaised.RemoveListener(OnError);
            }
            foreach (Button button in choiceButtons) if (button != null) button.onClick.RemoveAllListeners();
        }

        private void OnGameStateChanged()
        {
            Debug.Log($"[StoryFlow] Game phase {gameManager.Phase}, stage {gameManager.CurrentStageNumber}");
            switch (gameManager.Phase)
            {
                case GamePhase.Prologue:
                    // Visko placement takes ~10-30 s: warm the session while the prologue plays.
                    if (visko != null) visko.Connect();
                    break;
                case GamePhase.StartScreen:
                    storyStarted = false;
                    if (story != null) story.ResetStory();
                    if (visko != null) visko.Disconnect();
                    ShowChoices(false);
                    ShowNarration(null);
                    break;
            }
        }

        private void Update()
        {
            HandleShortcuts();
            // Debug display must never break the flow below.
            try { UpdateStatus(); }
            catch (System.Exception e) { showStatusInHeadset = false; Debug.LogException(e, this); }
            if (!handleItemGrab || storyStarted || itemSelectionRoot == null) return;
            bool visible = itemSelectionRoot.activeInHierarchy;
            if (visible && !selectionWasVisible) BeginGrab();
            selectionWasVisible = visible;
            if (!visible || story == null) return;

            bool full = story.Inventory.Count >= story.RequiredItemCount;
            if (!full) { fullSince = -1; return; }
            if (fullSince < 0) fullSince = Time.unscaledTime;
            else if (Time.unscaledTime - fullSince >= startWhenFullAfter) StartStoryNow();
        }

        private void BeginGrab()
        {
            fullSince = -1;
            Debug.Log("[StoryFlow] Item selection visible; grab timer starting.");
            if (grabTimer == null) return;
            grabTimer.gameObject.SetActive(true);
            grabTimer.StartTimer();
        }

        /// <summary>Ends the grab and starts the dorm scene with whatever is docked.</summary>
        public void StartStoryNow()
        {
            if (storyStarted || story == null || story.Phase != StoryPhase.SelectingItems) return;
            if (selection != null) selection.StartStoryWithDockedItems();
            else story.TryStartStory(true);
            storyStarted = story.Phase != StoryPhase.SelectingItems;
            if (!storyStarted) return;
            if (grabTimer != null) { grabTimer.PauseTimer(); grabTimer.gameObject.SetActive(false); }
            Debug.Log($"[StoryFlow] Story started with {story.Inventory.Count} item(s).");
        }

        private void OnCheckpointEntered(int checkpoint)
        {
            Debug.Log($"[StoryFlow] Checkpoint {checkpoint}: {StoryController.Checkpoints[checkpoint - 1]}");
            if (gameManager == null || checkpoint < firstAiCheckpoint) return;
            // Without handleItemGrab, the scene's Continue button already opens stage 1.
            if (checkpoint == 1 && !handleItemGrab) return;
            if (checkpoint >= StoryController.EndingCheckpoint) { gameManager.ShowEnding(); return; }
            // The live stream continues across checkpoints, so change stage without fading to black,
            // at the moment the checkpoint's own shot starts (after its arrival journey).
            bool arriving = story.CurrentScene != null && !string.IsNullOrWhiteSpace(story.CurrentScene.arrival_video_prompt);
            if (arriving && visko != null) StartCoroutine(ShowStageLater(checkpoint, visko.ArrivalSeconds));
            else gameManager.ShowStageSeamless(checkpoint);
        }

        private IEnumerator ShowStageLater(int checkpoint, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (story != null && story.Checkpoint == checkpoint) gameManager.ShowStageSeamless(checkpoint);
        }

        private void OnScenePresented(StoryScene scene)
        {
            ShowChoices(false);
            if (story.Checkpoint < firstAiCheckpoint) return;
            // The launchpad's preplaced fuel game owns its UI and Continue action.
            if (story.Checkpoint == 3)
            {
                ShowNarration(null);
                return;
            }
            bool demoEnd = story.Checkpoint >= lastCheckpoint;
            string sceneText = demoEnd && !scene.is_ending ? scene.narration + "  " + endOfDemoText : scene.narration;
            bool arriving = !string.IsNullOrWhiteSpace(scene.arrival_video_prompt) && visko != null;
            // During the arrival journey, subtitle the escape itself; then the checkpoint's line.
            ShowNarration(arriving && !string.IsNullOrWhiteSpace(scene.arrival_narration) ? scene.arrival_narration : sceneText);
            if (arriving) reveal = StartCoroutine(SwapNarration(visko.ArrivalSeconds, sceneText));
            if (demoEnd || scene.is_ending || scene.choices == null || scene.choices.Length == 0) return;
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                bool used = i < scene.choices.Length;
                if (choiceButtons[i] != null)
                {
                    choiceButtons[i].gameObject.SetActive(used);
                    choiceButtons[i].interactable = used && story.CanChoose(i);
                }
                if (used && i < choiceLabels.Length && choiceLabels[i] != null) choiceLabels[i].text = scene.choices[i].label;
            }
            // Let the scene play clean first: after the arrival journey (if any), then a short beat.
            float delay = choicesDelaySeconds +
                (!string.IsNullOrWhiteSpace(scene.arrival_video_prompt) && visko != null ? visko.ArrivalSeconds : 0);
            choicesReveal = StartCoroutine(RevealChoices(delay));
        }

        private IEnumerator SwapNarration(float delay, string text)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (story != null && story.Phase != StoryPhase.WaitingForNextScene) ShowNarration(text);
        }

        private IEnumerator RevealChoices(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            choicesReveal = null;
            if (story == null || story.Phase != StoryPhase.AwaitingChoice) yield break;
            ShowChoices(true);
            if (choiceGroup == null) yield break;
            for (float t = 0; t < choicesFadeSeconds; t += Time.unscaledDeltaTime)
            {
                choiceGroup.alpha = t / choicesFadeSeconds;
                yield return null;
            }
            choiceGroup.alpha = 1;
        }

        private void OnOutcomePresented(StoryChoice choice)
        {
            ShowChoices(false);
            ShowNarration(choice.outcome_narration);
        }

        private void Choose(int index)
        {
            waitingSince = Time.unscaledTime;
            if (story == null || !story.CanChoose(index)) { Debug.Log($"[StoryFlow] Choice {index} ignored (phase {story?.Phase})."); return; }
            Debug.Log($"[StoryFlow] Chose {index}: {story.CurrentScene.choices[index].label}");
            story.Choose(index);
        }

        private void OnError(string message)
        {
            Debug.LogWarning("[StoryFlow] " + message);
            if (story.Phase != StoryPhase.WaitingForNextScene) return;
            if (retry != null) StopCoroutine(retry);
            retry = StartCoroutine(RetryLater());
        }

        private IEnumerator RetryLater()
        {
            yield return new WaitForSecondsRealtime(retryAfterSeconds);
            retry = null;
            if (story != null && story.Phase == StoryPhase.WaitingForNextScene) story.RetryNextScene();
        }

        private void OnApplicationPause(bool paused) => Debug.Log("[StoryFlow] Application " + (paused ? "paused" : "resumed"));
        private void OnApplicationFocus(bool focus) => Debug.Log("[StoryFlow] Application focus " + focus);

        private void UpdateStatus()
        {
            if (!showStatusInHeadset)
            {
                if (statusText != null && statusText.gameObject.activeSelf) statusText.gameObject.SetActive(false);
                return;
            }
            if (statusText != null && !statusText.gameObject.activeSelf) statusText.gameObject.SetActive(true);
            if (statusText == null)
            {
                Transform anchor = narration != null ? narration.transform : transform;
                var go = new GameObject("Story Status (Debug)");
                go.transform.SetParent(anchor, false);
                statusText = go.AddComponent<TextMesh>();
                statusText.anchor = TextAnchor.LowerCenter;
                statusText.alignment = TextAlignment.Center;
                statusText.fontSize = 48;
                statusText.characterSize = .01f;
                statusText.color = Color.yellow;
            }
            if (narration != null)
            {
                // Float just above the narration panel, facing the same way, whatever its canvas scale.
                Transform anchor = narration.transform;
                statusText.transform.SetPositionAndRotation(anchor.position + anchor.up * .25f, anchor.rotation);
                statusText.transform.localScale = new Vector3(1 / Mathf.Max(1e-4f, anchor.lossyScale.x),
                    1 / Mathf.Max(1e-4f, anchor.lossyScale.y), 1 / Mathf.Max(1e-4f, anchor.lossyScale.z));
            }
            string wait = story != null && story.Phase == StoryPhase.WaitingForNextScene
                ? $" {Time.unscaledTime - waitingSince:0}s" : "";
            string status = $"Game: {(gameManager != null ? gameManager.Phase + " " + gameManager.CurrentStageNumber : "-")}" +
                            $" | Story: {(story != null ? story.Phase + " CP" + story.Checkpoint + " items " + story.Inventory.Count : "-")}{wait}" +
                            $"\nVisko: {(viskoClient != null ? viskoClient.Status : "-")}" +
                            $" | Director: {(director != null ? (director.MockMode ? "MOCK " : "") + (director.Busy ? "writing" : "idle") : "-")}" +
                            (story != null && !string.IsNullOrEmpty(story.LastError) ? "\nError: " + story.LastError : "");
            statusText.text = status;
            // Log on change, without the seconds counter (it would log every second).
            string key = wait.Length > 0 ? status.Replace(wait, "") : status;
            if (key != lastStatus) Debug.Log("[StoryFlow] Status " + key.Replace("\n", " || "));
            lastStatus = key;
        }

        private void ShowChoices(bool visible)
        {
            if (!visible)
            {
                if (reveal != null) { StopCoroutine(reveal); reveal = null; }
                if (choicesReveal != null) { StopCoroutine(choicesReveal); choicesReveal = null; }
            }
            if (choiceRoot == null) return;
            if (choiceGroup == null)
            {
                choiceGroup = choiceRoot.GetComponent<CanvasGroup>();
                if (choiceGroup == null) choiceGroup = choiceRoot.AddComponent<CanvasGroup>();
            }
            choiceGroup.alpha = visible && choicesFadeSeconds > 0 ? 0 : 1;
            choiceRoot.SetActive(visible);
        }

        // Headset-free testing in the Editor or a desktop build. Mirrors what the UI buttons do.
        private void HandleShortcuts()
        {
#if ENABLE_INPUT_SYSTEM && (UNITY_EDITOR || UNITY_STANDALONE)
            var keys = Keyboard.current;
            if (!keyboardShortcuts || keys == null) return;
            if (keys.f1Key.wasPressedThisFrame) showStatusInHeadset = !showStatusInHeadset;
            if (keys.spaceKey.wasPressedThisFrame && gameManager != null && gameManager.Phase == GamePhase.StartScreen)
            {
                Debug.Log("[StoryFlow] Key Space: start");
                gameManager.StartGame();
            }
            if (keys.gKey.wasPressedThisFrame && story != null && story.Phase == StoryPhase.SelectingItems)
            {
                foreach (StoryItemDefinition item in story.AvailableItems)
                {
                    if (story.Inventory.Count >= story.RequiredItemCount) break;
                    story.TrySelectItem(item);
                }
                Debug.Log($"[StoryFlow] Key G: start with {story.Inventory.Count} item(s)");
                if (story.TryStartStory(true) && !handleItemGrab && gameManager != null) gameManager.ShowMain();
            }
            if (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame) Choose(0);
            if (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame) Choose(1);
            if (keys.rKey.wasPressedThisFrame && story != null && story.Phase == StoryPhase.WaitingForNextScene)
                story.RetryNextScene();
#endif
        }

        private void ShowNarration(string text)
        {
            if (narration == null) return;
            if (string.IsNullOrWhiteSpace(text)) { narration.Hide(); return; }
            narration.SetText(text);
            narration.Show();
        }
    }
}
