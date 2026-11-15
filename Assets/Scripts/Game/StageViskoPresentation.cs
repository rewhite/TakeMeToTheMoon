using System;
using TakeMeToTheMoon.Story;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Visko;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Selected-stage presentation only. StoryController owns all decisions and progression.</summary>
    public sealed class StageViskoPresentation : MonoBehaviour
    {
        [SerializeField, Range(1, 5)] private int targetStage = 1;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private StoryController story;
        [SerializeField] private ViskoStreamClient viskoClient;
        [SerializeField] private SharedVideoPlayer sharedVideo;
        [SerializeField] private GameObject choiceRoot;
        [SerializeField] private Button[] choiceButtons;
        [SerializeField] private TMP_Text[] choiceLabels;
        [SerializeField] private NarrationPanel narration;
        [SerializeField, Min(0)] private float outcomeVideoSeconds = 10;
        private int playbackVersion, outcomeRequest;
        private bool awaitingTexture, outcomePlayback, retryVideo;
        private string prompt;

        private void Awake()
        {
            viskoClient.connectOnStart = viskoClient.autoStart = false;
            viskoClient.externalScreen = true;
        }
        private void OnEnable()
        {
            story.CheckpointEntered.AddListener(Checkpoint);
            story.ScenePresented.AddListener(Scene);
            story.OutcomePresented.AddListener(Outcome);
            story.ErrorRaised.AddListener(DirectorError);
            gameManager.StateChanged.AddListener(GameState);
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int index = i;
                choiceButtons[i].onClick.AddListener(ChooseActions(index));
            }
            ShowChoices(false);
            narration.Hide();
        }
        private UnityEngine.Events.UnityAction[] actions;
        private UnityEngine.Events.UnityAction ChooseActions(int index)
        {
            if (actions == null) actions = new UnityEngine.Events.UnityAction[choiceButtons.Length];
            return actions[index] = () => Choose(index);
        }
        private void OnDisable()
        {
            story.CheckpointEntered.RemoveListener(Checkpoint);
            story.ScenePresented.RemoveListener(Scene);
            story.OutcomePresented.RemoveListener(Outcome);
            story.ErrorRaised.RemoveListener(DirectorError);
            gameManager.StateChanged.RemoveListener(GameState);
            for (int i = 0; i < choiceButtons.Length; i++)
                if (actions != null) choiceButtons[i].onClick.RemoveListener(actions[i]);
            StopVideo();
            ShowChoices(false);
        }
        private void Checkpoint(int checkpoint)
        {
            if (checkpoint < 2) return; // InventoryContinueButton already opens Stage 1.
            if (checkpoint == StoryController.EndingCheckpoint) gameManager.ShowEnding();
            else gameManager.ShowStage(checkpoint);
        }
        private void GameState()
        {
            if (gameManager.Phase == GamePhase.Main && gameManager.CurrentStageNumber == targetStage) return;
            StopVideo();
            ShowChoices(false);
            narration.Hide();
        }
        private void Scene(StoryScene scene)
        {
            if (story.Checkpoint != targetStage) return;
            if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
            outcomePlayback = false;
            ShowChoices(false);
            narration.SetText("Connecting video...");
            narration.Show();
            Play(scene.video_prompt);
        }
        private void Outcome(StoryChoice choice)
        {
            if (story.Checkpoint != targetStage) return;
            outcomeRequest = story.PendingRequestId;
            story.HoldOutcomeForVideo(outcomeRequest);
            outcomePlayback = true;
            ShowChoices(false);
            narration.SetText("Preparing your choice...");
            narration.Show();
            Play(choice.outcome_video_prompt);
        }
        private async void Play(string nextPrompt)
        {
            int version = ++playbackVersion;
            prompt = nextPrompt;
            retryVideo = false;
            awaitingTexture = false;
            sharedVideo.HideExternal();
            try
            {
                // New track prevents a frame from the previous shot satisfying the outcome gate.
                await viskoClient.DisconnectAsync();
                if (version != playbackVersion || !isActiveAndEnabled) return;
                await viskoClient.Connect();
                if (version != playbackVersion || !isActiveAndEnabled) return;
                if (!viskoClient.SessionReady) { VideoError(viskoClient.Status); return; }
                viskoClient.SetPrompt(prompt);
                viskoClient.StartGeneration();
                awaitingTexture = true;
                textureDeadline = Time.realtimeSinceStartup + 90;
            }
            catch (Exception e) { if (version == playbackVersion) VideoError(e.Message); }
        }
        private float textureDeadline;
        private void Update()
        {
            if (!awaitingTexture) return;
            if (Time.realtimeSinceStartup > textureDeadline) { VideoError("Video timed out."); return; }
            if (viskoClient.VideoTexture == null || gameManager.Phase != GamePhase.Main || gameManager.CurrentStageNumber != targetStage) return;
            awaitingTexture = false;
            sharedVideo.ShowExternal(viskoClient.VideoTexture, 0);
            if (outcomePlayback)
            {
                story.OutcomeVideoStarted(outcomeRequest, outcomeVideoSeconds);
                narration.SetText(story.LastChoice.outcome_narration);
                if (story.LastError != null) ShowRetry("Next scene unavailable. Retry to continue.");
            }
            else
            {
                if (story.Phase == StoryPhase.Opening) story.OpeningVideoStarted();
                narration.SetText(story.CurrentScene.narration);
                for (int i = 0; i < choiceButtons.Length; i++)
                {
                    bool used = i < story.CurrentScene.choices.Length;
                    choiceButtons[i].gameObject.SetActive(used);
                    if (used) choiceLabels[i].text = story.CurrentScene.choices[i].label;
                    choiceButtons[i].interactable = used && story.CanChoose(i);
                }
                ShowChoices(true);
            }
        }
        private void Choose(int index)
        {
            if (retryVideo) { ShowChoices(false); Play(prompt); return; }
            if (story.Phase == StoryPhase.WaitingForNextScene && story.LastError != null)
            { ShowChoices(false); story.RetryNextScene(); return; }
            if (story.Checkpoint == targetStage) story.TryChoose(index);
        }
        private void VideoError(string message)
        {
            awaitingTexture = false;
            retryVideo = true;
            ShowRetry("Video unavailable. Retry to continue.");
            Debug.LogWarning("[StageVisko] " + message, this);
        }
        private void DirectorError(string message)
        {
            if (story.Checkpoint == targetStage && !awaitingTexture) ShowRetry("Next scene unavailable. Retry to continue.");
        }
        private void ShowRetry(string message)
        {
            narration.SetText(message);
            narration.Show();
            ShowChoices(true);
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                choiceButtons[i].gameObject.SetActive(i == 0);
                choiceButtons[i].interactable = true;
            }
            choiceLabels[0].text = "Retry";
        }
        private void StopVideo()
        {
            ++playbackVersion;
            awaitingTexture = false;
            retryVideo = false;
            sharedVideo.HideExternal();
            viskoClient.Disconnect();
        }
        private void ShowChoices(bool show) => choiceRoot.SetActive(show);
    }
}
