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
        private bool prewarming, prepared, parked;
        private bool presentationOnly;
        private int playbackStage = 1;
        public bool Stage1VideoPrepared => prewarming && prepared && VideoMoving;
        private bool choicesRevealed;
        private float deadline, choicesElapsed, fadeElapsed;
        private string prompt, audioPrompt;
        private UnityAction[] actions;
        private CanvasGroup choiceGroup;

        // Stage 3 onward retains authored media and the physical fuel challenge.
        private bool SupportsStage(int stage) => stage == targetStage || stage == 2 || stage == 4 || stage == 5;
        private bool InStage => gameManager.Phase == GamePhase.Main &&
            SupportsStage(gameManager.CurrentStageNumber) && gameManager.CurrentStageNumber == playbackStage;
        private bool VideoMoving => viskoClient.SessionReady && viskoClient.VideoTexture != null &&
            Time.realtimeSinceStartup - viskoClient.LastVideoFrameTime < 2;

        private void Awake()
        {
            viskoClient.connectOnStart = viskoClient.autoStart = false;
            viskoClient.externalScreen = true;
            viskoClient.SetOutputMuted(true);
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
            // Socket changes also emit StateChanged. They must not cancel the background stream.
            if (gameManager.Phase == GamePhase.Prologue)
            {
                PrewarmStage1();
                return;
            }
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
            if (gameManager.Phase == GamePhase.Main && SupportsStage(gameManager.CurrentStageNumber))
            {
                int stage = gameManager.CurrentStageNumber;
                if (story.Checkpoint == stage && story.CurrentScene != null)
                {
                    // ScenePresented follows CheckpointEntered in normal progression.
                    if (!wanted || playbackStage != stage) Scene(story.CurrentScene);
                }
                else if (!wanted || playbackStage != stage) PlayStagePreview(stage);
                return;
            }
            HideChoices();
            if (narration != null) narration.Hide();
            if (gameManager.Phase == GamePhase.Prologue)
                PrewarmStage1();
            else if (gameManager.Phase == GamePhase.Main)
                ParkVideo();
            else StopVideo(false);
        }
        private void PlayStagePreview(int stage)
        {
            // Presentation-only skips must not invent choices, inventory or story history.
            string nextPrompt;
            switch (stage)
            {
                case 1:
                    var preview = story.GetStage1PreviewScene();
                    if (preview == null) return;
                    nextPrompt = preview.video_prompt;
                    break;
                case 2:
                    nextPrompt = "First-person wide view of Stanford Memorial Church and the Main Quad emerging through thick smoke. Panicked crowds flee the heat beneath a blood-orange sky, ash drifting past abandoned cars and flashing emergency lights.";
                    break;
                case 4:
                    nextPrompt = "First-person view from a spacecraft in low Earth orbit, the burning Earth curving below the window. The cabin gently vibrates as distant spacecraft drift against the darkness of space.";
                    break;
                default:
                    nextPrompt = "First-person view from a spacecraft approaching the Moon, its cratered surface growing large through the forward window. The burning Earth recedes behind as the craft glides toward the lunar horizon.";
                    break;
            }
            if (stage != 1 && story.Roommate == "came along")
                nextPrompt += " Your roommate in a grey hoodie with a blanket over their shoulders is beside you.";
            nextPrompt += " No on-screen text or dialogue.";
            presentationOnly = true;
            playbackStage = stage;
            prewarming = outcomePlayback = false;
            audioPrompt = stage <= 2 ? "Distant sirens, hurried footsteps and wind." : "Low spacecraft engine hum and cabin vibration.";
            story.SetVideoPlaybackPaused(false);
            Play(nextPrompt);
        }

        private void PrewarmStage1()
        {
            if (prewarming || suspended || story.Phase != StoryPhase.SelectingItems) return;
            var scene = story.GetStage1PreviewScene();
            if (scene == null) return;
            presentationOnly = false;
            playbackStage = 1;
            prewarming = true;
            outcomePlayback = false;
            audioPrompt = scene.audio_prompt;
            Play(scene.video_prompt);
            Debug.Log("[StageVisko] Preparing Stage 1 behind the prologue; video and audio remain hidden.", this);
        }
        private void Scene(StoryScene scene)
        {
            if (!SupportsStage(story.Checkpoint)) return;
            if (!presentationOnly && wanted && playbackStage == story.Checkpoint && !outcomePlayback &&
                prompt == scene.video_prompt && !prewarming) return;
            presentationOnly = false;
            playbackStage = story.Checkpoint;
            if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
            outcomePlayback = false;
            bool reuse = prewarming && !retryVideo && prompt == scene.video_prompt && audioPrompt == scene.audio_prompt;
            prewarming = false;
            audioPrompt = scene.audio_prompt;
            if (reuse)
            {
                story.SetVideoPlaybackPaused(true);
                // Keep both the in-flight request and an already-decoded shot. No second set_prompt.
                Debug.Log("[StageVisko] Stage 1 adopts the preloaded stream without reconnecting or resending its prompt.", this);
                return;
            }
            Play(scene.video_prompt);
        }
        private void Outcome(StoryChoice choice)
        {
            if (!SupportsStage(story.Checkpoint)) return;
            if (gameManager.Phase != GamePhase.Main || gameManager.CurrentStageNumber != story.Checkpoint) return;
            presentationOnly = false;
            playbackStage = story.Checkpoint;
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
            bool resume = parked;
            parked = false;
            wanted = true;
            prompt = nextPrompt;
            retryVideo = shotVisible = prepared = false;
            waiting = true;
            gate.Cancel();
            HideChoices();
            narration.Hide();
            viskoClient.SetOutputMuted(true);
            deadline = Time.realtimeSinceStartup + 120;
            if (!prewarming && !presentationOnly) story.SetVideoPlaybackPaused(true);
            try
            {
                if (reconnect)
                {
                    sharedVideo.RetainExternalFrame();
                    generating = false;
                    await viskoClient.DisconnectAsync();
                }
                if (version != playbackVersion || !isActiveAndEnabled) return;
                if (!viskoClient.SessionReady) generating = false;
                await viskoClient.Connect();
                if (version != playbackVersion || !isActiveAndEnabled) return;
                if (!viskoClient.SessionReady) { VideoError(); return; }
                if (string.IsNullOrWhiteSpace(prompt)) { VideoError(); return; }
                bool morph = generating;
                gate.Begin(morph, playbackBufferSeconds);
                viskoClient.SetPrompt(prompt);
                if (!string.IsNullOrWhiteSpace(audioPrompt)) viskoClient.SetAudioPrompt(audioPrompt);
                // Keep the same session and track for scenes and outcomes across Stages 1 and 2.
                if (!generating) viskoClient.StartGeneration();
                else if (resume) viskoClient.ResumeGeneration();
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
            if (!prewarming && !presentationOnly) story.SetVideoPlaybackPaused(!shotVisible || !playing);
            if (waiting)
            {
                if (Time.realtimeSinceStartup > deadline) { VideoError(); return; }
                if (gate.TryOpen(Time.realtimeSinceStartup, viskoClient.DecodedVideoFrames,
                    VideoMoving))
                {
                    waiting = false;
                    prepared = true;
                    Debug.Log("[StageVisko] Decoded shot ready" + (prewarming ? " behind prologue." : "."), this);
                }
            }
            // Preloading never replaces the prologue, reveals UI, or consumes story timers.
            if (prewarming) return;
            if (prepared && InStage && VideoMoving)
            {
                sharedVideo.ShowExternal(viskoClient.VideoTexture, 0);
                viskoClient.SetOutputMuted(!playing);
                if (!shotVisible && playing && sharedVideo.ShowingExternal) ShotStarted();
            }
            if (waiting || !prepared)
            {
                return;
            }
            if (presentationOnly || !shotVisible || outcomePlayback || story.Phase != StoryPhase.AwaitingChoice) return;
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
            if (presentationOnly)
            {
                Debug.Log($"[StageVisko] Stage {playbackStage} preview video on screen.", this);
                return;
            }
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
            narration.Show();
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
            if (!presentationOnly && shotVisible && choicesRevealed && VideoMoving && story.Checkpoint == playbackStage)
                story.TryChoose(index);
        }
        private void VideoError()
        {
            ++playbackVersion;
            waiting = shotVisible = prepared = false;
            gate.Cancel();
            retryVideo = true;
            if (!prewarming)
            {
                story.SetVideoPlaybackPaused(true);
                ShowRetry(true);
            }
            Debug.LogWarning("[StageVisko] Video not ready; progression paused. Retry reconnects without applying the choice twice.", this);
        }
        private void DirectorError(string message)
        {
            if (SupportsStage(story.Checkpoint) && outcomePlayback && shotVisible && !retryVideo) ShowRetry(false);
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
                if (!prewarming)
                {
                    sharedVideo.RetainExternalFrame();
                    story.SetVideoPlaybackPaused(true);
                }
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
            prewarming = prepared = parked = false;
            gate.Cancel();
            if (story != null) story.SetVideoPlaybackPaused(false);
            if (sharedVideo != null)
            {
                if (keepFrame) sharedVideo.RetainExternalFrame();
                else sharedVideo.HideExternal();
            }
            if (viskoClient != null)
            {
                viskoClient.SetOutputMuted(true);
                viskoClient.Disconnect();
            }
        }
        private void ParkVideo()
        {
            if (parked) return;
            ++playbackVersion;
            wanted = waiting = shotVisible = prewarming = prepared = retryVideo = false;
            parked = true;
            gate.Cancel();
            story.SetVideoPlaybackPaused(false);
            sharedVideo.RetainExternalFrame();
            viskoClient.SetOutputMuted(true);
            // Other stages retain authored media. Keep the connection, pause unused generation.
            if (viskoClient.SessionReady && generating) viskoClient.PauseGeneration();
            Debug.Log("[StageVisko] Stage changed; keeping the Visko connection and pausing unused generation.", this);
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
