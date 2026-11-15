using System.Collections;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        [Header("Scope")]
        [Tooltip("The demo stops at this checkpoint: its scene plays, but no choices are offered.")]
        [SerializeField, Range(1, 6)] private int lastCheckpoint = 3;
        [SerializeField] private string endOfDemoText = "To be continued...";
        [Tooltip("Retry the Director automatically after an error, after this many seconds.")]
        [SerializeField, Min(1)] private float retryAfterSeconds = 3f;

        private bool selectionWasVisible, storyStarted;
        private float fullSince = -1;
        private Coroutine retry;

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
            if (storyStarted || itemSelectionRoot == null) return;
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
            if (gameManager == null) return;
            if (checkpoint >= StoryController.EndingCheckpoint) gameManager.ShowEnding();
            else gameManager.ShowStage(checkpoint);
        }

        private void OnScenePresented(StoryScene scene)
        {
            bool demoEnd = story.Checkpoint >= lastCheckpoint;
            ShowNarration(demoEnd && !scene.is_ending ? scene.narration + "\n\n" + endOfDemoText : scene.narration);
            if (demoEnd || scene.is_ending || scene.choices == null || scene.choices.Length == 0)
            {
                ShowChoices(false);
                return;
            }
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
            ShowChoices(true);
        }

        private void OnOutcomePresented(StoryChoice choice)
        {
            ShowChoices(false);
            ShowNarration(choice.outcome_narration);
        }

        private void Choose(int index)
        {
            if (story == null || !story.CanChoose(index)) return;
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

        private void ShowChoices(bool visible)
        {
            if (choiceRoot != null) choiceRoot.SetActive(visible);
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
