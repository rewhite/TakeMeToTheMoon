using System;
using TakeMeToTheMoon.Story;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Visko;

namespace TakeMeToTheMoon.Game
{
    // Presentation only: StoryController still owns decisions, timers and checkpoint progression.
    [DefaultExecutionOrder(-100)]
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
        [SerializeField, Min(0)] private float choicesDelaySeconds = 2.5f;
        [SerializeField, Min(0)] private float choicesFadeSeconds = .4f;
        [Tooltip("Buffer after generated chunks, before accepting decoded frames as the changed shot. Visko does not identify prompts in video frames.")]
        [SerializeField, Min(0)] private float playbackBufferSeconds = 1.8f;
        private readonly ViskoPlaybackGate gate = new ViskoPlaybackGate();
        private int playbackVersion, outcomeRequest;
        private bool waiting, shotVisible, outcomePlayback, retryVideo, generating, wanted, suspended;
        private bool choicesRevealed;
        private float deadline, choicesElapsed, fadeElapsed;
        private string prompt, audioPrompt;
        private UnityAction[] actions;
        private CanvasGroup choiceGroup;

        private bool InStage => gameManager.Phase == GamePhase.Main && gameManager.CurrentStageNumber == targetStage;
        private bool VideoMoving => viskoClient.SessionReady && viskoClient.VideoTexture != null &&
            Time.realtimeSinceStartup - viskoClient.LastVideoFrameTime < 2;

        private void Awake()
        {
            viskoClient.connectOnStart = viskoClient.autoStart = false;
            viskoClient.externalScreen = true;
            choiceGroup = choiceRoot.GetComponent<CanvasGroup>();
            if (choiceGroup == null) choiceGroup = choiceRoot.AddComponent<CanvasGroup>();
        }
        private void OnEnable()
        {
            story.CheckpointEntered.AddListener(Checkpoint);
            story.ScenePresented.AddListener(Scene);
            story.OutcomePresented.AddListener(Outcome);
            story.ErrorRaised.AddListener(DirectorError);
            story.StateChanged.AddListener(StoryState);
            viskoClient.MessageReceived += Message;
            gameManager.StateChanged.AddListener(GameState);
            actions = new UnityAction[choiceButtons.Length];
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int index = i;
                actions[i] = () => Choose(index);
                choiceButtons[i].onClick.AddListener(actions[i]);
            }
            HideChoices();
            if (narration != null) narration.Hide();
        }
        private void OnDisable()
        {
            // Scene teardown may destroy the independently placed UI/client before this host.
            if (story != null)
            {
                story.CheckpointEntered.RemoveListener(Checkpoint);
                story.ScenePresented.RemoveListener(Scene);
                story.OutcomePresented.RemoveListener(Outcome);
                story.ErrorRaised.RemoveListener(DirectorError);
                story.StateChanged.RemoveListener(StoryState);
            }
            if (viskoClient != null) viskoClient.MessageReceived -= Message;
            if (gameManager != null) gameManager.StateChanged.RemoveListener(GameState);
            for (int i = 0; i < choiceButtons.Length; i++)
                if (actions != null && choiceButtons[i] != null) choiceButtons[i].onClick.RemoveListener(actions[i]);
            StopVideo(false);
            HideChoices();
            if (narration != null) narration.Hide();
        }
        private void StoryState()
        {
            if (story.Phase != StoryPhase.SelectingItems) return;
            StopVideo(false);
            HideChoices();
            if (narration != null) narration.Hide();
        }
        private void Checkpoint(int checkpoint)
        {
            if (checkpoint < 2) return; // InventoryContinueButton opens Stage 1.
            HideChoices();
            if (narration != null) narration.Hide();
            if (checkpoint == StoryController.EndingCheckpoint) gameManager.ShowEnding();
            else gameManager.ShowStage(checkpoint);
        }
        private void GameState()
        {
            if (InStage) return;
            StopVideo(gameManager.Phase == GamePhase.Main);
            HideChoices();
            if (narration != null) narration.Hide();
        }
        private void Scene(StoryScene scene)
        {
            if (story.Checkpoint != targetStage) return;
            if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
            outcomePlayback = false;
            audioPrompt = scene.audio_prompt;
            Play(scene.video_prompt);
        }
        private void Outcome(StoryChoice choice)
        {
            if (story.Checkpoint != targetStage) return;
            outcomeRequest = story.PendingRequestId;
            story.HoldOutcomeForVideo(outcomeRequest);
            outcomePlayback = true;
            Play(choice.outcome_video_prompt);
        }
        private async void Play(string nextPrompt, bool reconnect = false)
        {
            if (reconnect && outcomePlayback)
            {
                outcomeRequest = story.PendingRequestId;
                story.HoldOutcomeForVideo(outcomeRequest);
            }
            int version = ++playbackVersion;
            wanted = true;
            prompt = nextPrompt;
            retryVideo = shotVisible = false;
            waiting = true;
            gate.Cancel();
            HideChoices();
            narration.SetText(outcomePlayback ? "Preparing your choice..." : "Preparing video...");
            narration.Show();
            deadline = Time.realtimeSinceStartup + 120;
            story.SetVideoPlaybackPaused(true);
            try
            {
                if (reconnect)
                {
                    sharedVideo.RetainExternalFrame();
                    generating = false;
                    await viskoClient.DisconnectAsync();
                }
                if (version != playbackVersion || !isActiveAndEnabled) return;
                await viskoClient.Connect();
                if (version != playbackVersion || !isActiveAndEnabled) return;
                if (!viskoClient.SessionReady) { VideoError(); return; }
                if (string.IsNullOrWhiteSpace(prompt)) { VideoError(); return; }
                bool morph = generating;
                gate.Begin(morph, playbackBufferSeconds);
                viskoClient.SetPrompt(prompt);
                if (!string.IsNullOrWhiteSpace(audioPrompt)) viskoClient.SetAudioPrompt(audioPrompt);
                // Keep the same session and track for dorm -> roommate -> selected outcome.
                if (!generating) viskoClient.StartGeneration();
                generating = true;
                Debug.Log($"[StageVisko] Shot {version}: {(morph ? "morph" : "start")}; waiting for accepted prompt, chunks and decoded frames.", this);
            }
            catch (Exception) { if (version == playbackVersion) VideoError(); }
        }
        private void Message(string type, string json)
        {
            if (!wanted || suspended) return;
            gate.Message(type, Time.realtimeSinceStartup, viskoClient.DecodedVideoFrames);
            if (type == "command_error") { VideoError(); return; }
            if (type == "generation_complete" && !retryVideo && viskoClient.SessionReady)
                viskoClient.StartGeneration(); // Continue beyond the server chunk limit.
        }
        private void Update()
        {
            if (!wanted || suspended) return;
            bool playing = InStage && !gameManager.IsTransitioning && VideoMoving;
            story.SetVideoPlaybackPaused(!shotVisible || !playing);
            if (viskoClient.VideoTexture != null && InStage)
                sharedVideo.ShowExternal(viskoClient.VideoTexture, 0);
            if (waiting)
            {
                if (Time.realtimeSinceStartup > deadline) { VideoError(); return; }
                if (gate.TryOpen(Time.realtimeSinceStartup, viskoClient.DecodedVideoFrames,
                    playing && sharedVideo.ShowingExternal)) ShotStarted();
                return;
            }
            if (!shotVisible || outcomePlayback || story.Phase != StoryPhase.AwaitingChoice) return;
            if (!playing)
            {
                HideChoices();
                return;
            }
            choicesElapsed += Time.unscaledDeltaTime;
            if (!choicesRevealed && choicesElapsed >= choicesDelaySeconds) RevealChoices();
            if (!choicesRevealed) return;
            fadeElapsed += Time.unscaledDeltaTime;
            choiceGroup.alpha = choicesFadeSeconds <= 0 ? 1 : Mathf.Clamp01(fadeElapsed / choicesFadeSeconds);
            choiceGroup.interactable = choiceGroup.blocksRaycasts = choiceGroup.alpha >= 1;
        }
        private void ShotStarted()
        {
            waiting = false;
            shotVisible = true;
            story.SetVideoPlaybackPaused(false);
            choicesElapsed = 0;
            if (outcomePlayback)
            {
                story.OutcomeVideoStarted(outcomeRequest, outcomeVideoSeconds);
                narration.SetText(story.LastChoice.outcome_narration);
                if (story.LastError != null) ShowRetry(false);
            }
            else
            {
                if (story.Phase == StoryPhase.Opening) story.OpeningVideoStarted();
                narration.SetText(story.CurrentScene.narration);
            }
            Debug.Log($"[StageVisko] Shot {playbackVersion} on screen; " +
                (outcomePlayback ? $"outcome timer {outcomeVideoSeconds}s." : "scene timer started; choices remain hidden."), this);
        }
        private void RevealChoices()
        {
            var choices = story.CurrentScene?.choices;
            if (choices == null || choices.Length == 0) return;
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                bool used = i < choices.Length;
                choiceButtons[i].gameObject.SetActive(used);
                if (used) choiceLabels[i].text = choices[i].label;
                choiceButtons[i].interactable = used && story.CanChoose(i);
            }
            choicesRevealed = true;
            fadeElapsed = 0;
            choiceGroup.alpha = 0;
            choiceRoot.SetActive(true);
            Debug.Log("[StageVisko] Choice reveal after visible scene delay.", this);
        }
        private void Choose(int index)
        {
            if (!InStage || !choiceRoot.activeSelf || !choiceGroup.interactable) return;
            if (retryVideo) { Play(prompt, true); return; }
            if (story.Phase == StoryPhase.WaitingForNextScene && story.LastError != null)
            { HideChoices(); story.RetryNextScene(); return; }
            if (shotVisible && choicesRevealed && VideoMoving && story.Checkpoint == targetStage)
                story.TryChoose(index);
        }
        private void VideoError()
        {
            ++playbackVersion;
            waiting = shotVisible = false;
            gate.Cancel();
            story.SetVideoPlaybackPaused(true);
            ShowRetry(true);
            Debug.LogWarning("[StageVisko] Video not ready; progression paused. Retry reconnects without applying the choice twice.", this);
        }
        private void DirectorError(string message)
        {
            if (story.Checkpoint == targetStage && outcomePlayback && shotVisible && !retryVideo) ShowRetry(false);
        }
        private void ShowRetry(bool video)
        {
            retryVideo = video;
            HideChoices();
            narration.SetText(video ? "Video unavailable. Retry to continue." : "Next scene unavailable. Retry to continue.");
            narration.Show();
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                choiceButtons[i].gameObject.SetActive(i == 0);
                choiceButtons[i].interactable = i == 0;
            }
            choiceLabels[0].text = "Retry";
            choiceGroup.alpha = 1;
            choiceGroup.interactable = choiceGroup.blocksRaycasts = true;
            choiceRoot.SetActive(true);
        }
        private void OnApplicationPause(bool paused)
        {
            suspended = paused;
            if (!wanted) return;
            if (paused)
            {
                ++playbackVersion;
                gate.Cancel();
                sharedVideo.RetainExternalFrame();
                story.SetVideoPlaybackPaused(true);
                HideChoices();
            }
            else
            {
                if (outcomePlayback)
                {
                    outcomeRequest = story.PendingRequestId;
                    story.HoldOutcomeForVideo(outcomeRequest);
                }
                else if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
                Play(prompt, true);
            }
        }
        private void StopVideo(bool keepFrame)
        {
            ++playbackVersion;
            wanted = waiting = shotVisible = generating = retryVideo = false;
            gate.Cancel();
            if (story != null) story.SetVideoPlaybackPaused(false);
            if (sharedVideo != null)
            {
                if (keepFrame) sharedVideo.RetainExternalFrame();
                else sharedVideo.HideExternal();
            }
            if (viskoClient != null) viskoClient.Disconnect();
        }
        private void HideChoices()
        {
            choicesRevealed = false;
            choicesElapsed = fadeElapsed = 0;
            if (choiceGroup != null)
            {
                choiceGroup.alpha = 0;
                choiceGroup.interactable = choiceGroup.blocksRaycasts = false;
            }
            if (choiceRoot != null) choiceRoot.SetActive(false);
        }
    }
}
