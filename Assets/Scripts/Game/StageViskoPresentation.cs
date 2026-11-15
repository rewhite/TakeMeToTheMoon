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
        [Header("Ending videos")]
        private bool endingPresentation;
        private float endingElapsed;
        [SerializeField] private GameObject choiceRoot;
        [SerializeField] private Button[] choiceButtons;
        [SerializeField] private Button continueButton;
        [SerializeField] private TMP_Text[] choiceLabels;
        [SerializeField] private NarrationPanel narration;
        [UnityEngine.Serialization.FormerlySerializedAs("outcomeVideoSeconds")]
        [UnityEngine.Serialization.FormerlySerializedAs("nextStageDelaySeconds")]
        [Tooltip("Delay from choosing until Continue appears. The button enables once the next scene/video is ready.")]
        [SerializeField, Min(0)] private float continueDelaySeconds = 10;
        [SerializeField, Min(0)] private float choicesDelaySeconds = 2.5f;
        [Tooltip("Visible Stage 3 video time before revealing the physical fuel challenge.")]
        [SerializeField, Min(0)] private float fuelRevealSeconds = 10;
        private float fuelVideoElapsed;
        private bool fuelRevealed;
        public bool ControlsFuelVideo(GameManager manager) => isActiveAndEnabled && gameManager == manager;
        [SerializeField, Min(0)] private float choicesFadeSeconds = .4f;
        [Tooltip("Buffer after generated chunks, before accepting decoded frames as the changed shot. Visko does not identify prompts in video frames.")]
        [SerializeField, Min(0)] private float playbackBufferSeconds = 1.8f;
        [Header("Next video recovery")]
        [SerializeField, Min(5)] private float preparingTimeoutSeconds = 30;
        [SerializeField, Range(0, 5)] private int maxPreparingRetries = 2;
        [SerializeField, Min(0)] private float preparingRetryDelaySeconds = 2;
        private int preparingRetries;
        private bool preparingRetryPending;
        private float preparingRetryAt;
        private ViskoStreamClient standbyClient;
        private ViskoStreamClient webRtcDriver;
        private float standbyDiagnosticAt;
        private readonly ViskoPlaybackGate standbyGate = new ViskoPlaybackGate();
        private StoryScene standbyScene;
        private int standbyRequest, standbyVersion;
        private bool standbyWaiting, standbyReady, standbyFailed;
        private float standbyDeadline;
        private bool StandbyMoving => standbyClient != null && standbyClient.SessionReady &&
            standbyClient.VideoTexture != null && Time.realtimeSinceStartup - standbyClient.LastVideoFrameTime < 2;
        private readonly ViskoPlaybackGate gate = new ViskoPlaybackGate();
        private int playbackVersion, outcomeRequest;
        private bool waiting, shotVisible, outcomePlayback, retryVideo, generating, wanted, suspended;
        private bool prewarming, prepared, parked;
        private bool presentationOnly;
        private int playbackStage = 1;
        public ViskoStreamClient ActiveClient => viskoClient;
        public bool Stage1VideoPrepared => prewarming && prepared && VideoMoving;
        private bool choicesRevealed;
        private float deadline, choicesElapsed, fadeElapsed;
        private string prompt, audioPrompt;
        private UnityAction[] actions;
        private CanvasGroup choiceGroup;
        private FuelThrowGame fuelGame;

        // All five stage videos use Visko; Stage 3 retains the physical fuel challenge.
        private bool SupportsStage(int stage) => stage >= 1 && stage <= StoryController.EndingCheckpoint;
        private bool InStage => gameManager.Phase == GamePhase.Main &&
            SupportsStage(gameManager.CurrentStageNumber) &&
            (gameManager.CurrentStageNumber == playbackStage || (endingPresentation && playbackStage == StoryController.EndingCheckpoint));
        private bool VideoMoving => viskoClient.SessionReady && viskoClient.VideoTexture != null &&
            Time.realtimeSinceStartup - viskoClient.LastVideoFrameTime < 2;

        private void Awake()
        {
            fuelGame = FindAnyObjectByType<FuelThrowGame>(FindObjectsInactive.Include);
            webRtcDriver = viskoClient;
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
            story.NextScenePrepared += PrepareNextVideo;
            story.FuelResultVideoRequested += FuelResultVideo;
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
            if (continueButton != null) continueButton.onClick.AddListener(ContinueClicked);
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
                story.NextScenePrepared -= PrepareNextVideo;
                story.FuelResultVideoRequested -= FuelResultVideo;
                story.OutcomePresented.RemoveListener(Outcome);
                story.ErrorRaised.RemoveListener(DirectorError);
                story.StateChanged.RemoveListener(StoryState);
            }
            if (viskoClient != null) viskoClient.MessageReceived -= Message;
            if (gameManager != null) gameManager.StateChanged.RemoveListener(GameState);
            for (int i = 0; i < choiceButtons.Length; i++)
                if (actions != null && choiceButtons[i] != null) choiceButtons[i].onClick.RemoveListener(actions[i]);
            if (continueButton != null) continueButton.onClick.RemoveListener(ContinueClicked);
            StopVideo(false);
            HideChoices();
            if (narration != null) narration.Hide();
        }
        private void PrepareNextVideo(int id, StoryScene scene)
        {
            preparingRetries = 0;
            BeginNextVideo(id, scene);
        }
        private async void BeginNextVideo(int id, StoryScene scene)
        {
            int stage = story.GetDirectorInput().checkpoint_number;
            if (!SupportsStage(stage) || !isActiveAndEnabled) return;
            CancelStandby();
            standbyScene = scene;
            standbyRequest = id;
            story.SetNextVideoPending(id, true);
            standbyWaiting = true;
            standbyDeadline = Time.realtimeSinceStartup + Mathf.Max(5, preparingTimeoutSeconds);
            int version = standbyVersion;
            try
            {
                // Do not reconnect a retired Android receiver: create fresh native track
                // state for every preload, keeping only the original global WebRTC driver.
                var retired = standbyClient;
                standbyClient = null;
                if (retired != null)
                {
                    await retired.DisconnectAsync();
                    if (retired != webRtcDriver) Destroy(retired.gameObject);
                }
                if (version != standbyVersion || !isActiveAndEnabled) return;
                if (standbyClient == null)
                {
                    var host = new GameObject("Visko Next Video (Runtime)");
                    host.transform.SetParent(transform, false);
                    standbyClient = host.AddComponent<ViskoStreamClient>();
                    standbyClient.connectOnStart = standbyClient.autoStart = false;
                    standbyClient.externalScreen = true;
                    standbyClient.driveWebRtcUpdate = false; // The persistent original client drives WebRTC.
                    standbyClient.showDebugGui = standbyClient.showWorldStatus = false;
                    standbyClient.model = viskoClient.model;
                    standbyClient.resolution = viskoClient.resolution;
                    standbyClient.generateAudio = viskoClient.generateAudio;
                    standbyClient.playAudio = viskoClient.playAudio;
                    standbyClient.spatialBlend = viskoClient.spatialBlend;
                }
                standbyClient.SetOutputMuted(true);
                standbyClient.MessageReceived -= StandbyMessage;
                standbyClient.MessageReceived += StandbyMessage;
                await standbyClient.Connect();
                if (version != standbyVersion || !isActiveAndEnabled) return;
                if (!standbyClient.SessionReady) { StandbyError(); return; }
                standbyGate.Begin(false, playbackBufferSeconds);
                standbyClient.SetPrompt(scene.video_prompt);
                LogViskoPrompt("NEXT STAGE / Director", stage, id, scene.video_prompt, scene.audio_prompt);
                if (!string.IsNullOrWhiteSpace(scene.audio_prompt)) standbyClient.SetAudioPrompt(scene.audio_prompt);
                standbyClient.StartGeneration();
                Debug.Log("[StageVisko] Preparing next stage on a second stream; current video keeps playing.", this);
            }
            catch (Exception) { if (version == standbyVersion) StandbyError(); }
        }
        private void StandbyMessage(string type, string json)
        {
            if (standbyScene == null || suspended || standbyFailed) return;
            standbyGate.Message(type, Time.realtimeSinceStartup, standbyClient.DecodedVideoFrames);
            if (type == "prompt_accepted" || type == "chunk_complete")
                Debug.Log($"[StageVisko] Next stream {type}; decoded={standbyClient.DecodedVideoFrames}, texture={standbyClient.VideoTexture != null}.", this);
            if (type == "command_error") StandbyError();
            else if (type == "generation_complete" && standbyClient.SessionReady) standbyClient.StartGeneration();
        }
        private void StandbyError()
        {
            if (standbyFailed || standbyScene == null) return;
            standbyWaiting = standbyReady = false;
            standbyFailed = true;
            standbyGate.Cancel();
            story.SetNextVideoPending(standbyRequest, true);
            if (continueButton != null) continueButton.interactable = false;
            if (preparingRetries < maxPreparingRetries)
            {
                preparingRetries++;
                preparingRetryPending = true;
                preparingRetryAt = Time.realtimeSinceStartup + preparingRetryDelaySeconds;
                Debug.LogWarning($"[StageVisko] Next video automatic reconnect {preparingRetries}/{maxPreparingRetries}; keeping the current video and selected result.", this);
                return;
            }
            ShowRetry(false);
            Debug.LogWarning("[StageVisko] Next stream unavailable; current stream retained. Retry prepares the same next scene.", this);
        }
        private void TickStandby()
        {
            if (standbyScene == null || suspended) return;
            if (preparingRetryPending)
            {
                if (Time.realtimeSinceStartup >= preparingRetryAt)
                {
                    var next = standbyScene;
                    int id = standbyRequest;
                    preparingRetryPending = false;
                    if (story.PendingRequestId == id && story.Phase == StoryPhase.WaitingForNextScene)
                        BeginNextVideo(id, next);
                    else CancelStandby();
                }
                return;
            }
            if (standbyFailed) return;
            if (standbyWaiting && Time.realtimeSinceStartup > standbyDeadline) { StandbyError(); return; }
            if (Time.realtimeSinceStartup >= standbyDiagnosticAt)
            {
                standbyDiagnosticAt = Time.realtimeSinceStartup + 10;
                Debug.Log($"[StageVisko] Next stream status: connected={standbyClient != null && standbyClient.SessionReady}, decoded={standbyClient?.DecodedVideoFrames}, texture={standbyClient != null && standbyClient.VideoTexture != null}, moving={StandbyMoving}, gateReady={standbyReady}.", this);
            }
            if (standbyClient == null) return;
            if (standbyWaiting)
            {
                if (Time.realtimeSinceStartup > standbyDeadline) { StandbyError(); return; }
                if (standbyGate.TryOpen(Time.realtimeSinceStartup, standbyClient.DecodedVideoFrames, StandbyMoving))
                {
                    standbyWaiting = false;
                    standbyReady = true;
                    Debug.Log("[StageVisko] Next stream decoded; waiting for choice delay before switching.", this);
                }
            }
            if (standbyReady)
            {
                if (StandbyMoving) standbyDeadline = Time.realtimeSinceStartup + Mathf.Max(5, preparingTimeoutSeconds);
                else if (Time.realtimeSinceStartup > standbyDeadline) { StandbyError(); return; }
            }
            story.SetNextVideoPending(standbyRequest, !standbyReady || !StandbyMoving);
        }
        private bool AdoptStandby(StoryScene scene)
        {
            if (!standbyReady || standbyScene != scene || !StandbyMoving) return false;
            ++playbackVersion;
            viskoClient.MessageReceived -= Message;
            standbyClient.MessageReceived -= StandbyMessage;
            var previous = viskoClient;
            viskoClient = standbyClient;
            standbyClient = previous;
            viskoClient.MessageReceived += Message;
            sharedVideo.ShowExternal(viskoClient.VideoTexture, 0);
            standbyClient.SetOutputMuted(true);
            standbyClient.Disconnect(); // Retire only after the screen references the new texture.
            standbyScene = null;
            standbyWaiting = standbyReady = standbyFailed = false;
            standbyGate.Cancel();
            prompt = scene.video_prompt;
            audioPrompt = scene.audio_prompt;
            wanted = generating = prepared = true;
            waiting = shotVisible = retryVideo = parked = false;
            HideChoices();
            narration.Hide();
            Debug.Log("[StageVisko] Swapped to prepared next stream without resetting the visible video.", this);
            return true;
        }
        private void CancelStandby()
        {
            preparingRetryPending = false;
            ++standbyVersion;
            if (story != null) story.SetNextVideoPending(standbyRequest, false);
            standbyScene = null;
            standbyWaiting = standbyReady = standbyFailed = false;
            standbyGate.Cancel();
            if (standbyClient != null)
            {
                standbyClient.MessageReceived -= StandbyMessage;
                standbyClient.SetOutputMuted(true);
                standbyClient.Disconnect();
            }
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
            if (checkpoint == StoryController.EndingCheckpoint) { endingPresentation = true; endingElapsed = 0; }
            else gameManager.ShowStage(checkpoint);
        }
        private void GameState()
        {
            if (gameManager.Phase == GamePhase.Main && SupportsStage(gameManager.CurrentStageNumber))
            {
                int stage = gameManager.CurrentStageNumber;
                if (stage == 3 && story.Checkpoint != 3)
                {
                    // Seed the existing physical-game preview before its video, not when
                    // the game is revealed (which would reset an already-playing shot).
                    story.PrepareFuelChallengePreview();
                    return;
                }
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
            CancelStandby();
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
                case 3:
                    nextPrompt = "First-person wide view of an emergency rocket launch pad, towering evacuation rockets surrounded by smoke and pulsing warning lights. Crews hurry between fueling equipment as ash drifts across the scorched ground beneath a blood-orange sky.";
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
            fuelVideoElapsed = 0;
            fuelRevealed = false;
            prewarming = outcomePlayback = false;
            audioPrompt = stage <= 3 ? "Distant sirens, hurried footsteps and wind." : "Low spacecraft engine hum and cabin vibration.";
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
            fuelVideoElapsed = 0;
            fuelRevealed = false;
            if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
            outcomePlayback = false;
            bool reuse = prewarming && !retryVideo && prompt == scene.video_prompt && audioPrompt == scene.audio_prompt;
            prewarming = false;
            audioPrompt = scene.audio_prompt;
            if (AdoptStandby(scene)) return;
            if (standbyScene != null) CancelStandby();
            if (reuse)
            {
                story.SetVideoPlaybackPaused(true);
                // Keep both the in-flight request and an already-decoded shot. No second set_prompt.
                Debug.Log("[StageVisko] Stage 1 adopts the preloaded stream without reconnecting or resending its prompt.", this);
                return;
            }
            Play(scene.video_prompt);
        }
        private void FuelResultVideo(string resultPrompt)
        {
            if (!InStage || playbackStage != 3) return;
            Play(resultPrompt, morphCurrent: true);
        }
        private void Outcome(StoryChoice choice)
        {
            if (!SupportsStage(story.Checkpoint)) return;
            if (gameManager.Phase != GamePhase.Main || gameManager.CurrentStageNumber != story.Checkpoint) return;
            presentationOnly = false;
            playbackStage = story.Checkpoint;
            outcomeRequest = story.PendingRequestId;
            if (story.Checkpoint == 3 && story.NextContinueVisible)
            {
                // Use the remote launch outcome while checkpoint 4 prepares in parallel.
                Play(choice.outcome_video_prompt, morphCurrent: true);
                narration.SetText(choice.outcome_narration);
                narration.Show();
                outcomePlayback = true;
                story.SetVideoPlaybackPaused(false);
                return;
            }
            story.HoldOutcomeForVideo(outcomeRequest);
            outcomePlayback = true;
            // The fuel game already has its own Continue; do not ask twice.
            if (story.Checkpoint != 3) story.RequireNextSceneContinue();
            // Steer this location toward the selected outcome, while the Director's
            // next checkpoint is prepared independently on the standby stream.
            Play(choice.outcome_video_prompt, morphCurrent: true);
            narration.SetText(choice.outcome_narration);
            narration.Show();
            story.OutcomeVideoStarted(outcomeRequest, continueDelaySeconds);
            story.SetVideoPlaybackPaused(false);
        }
        private async void Play(string nextPrompt, bool reconnect = false, bool morphCurrent = false)
        {
            int version = ++playbackVersion;
            parked = false;
            wanted = true;
            prompt = nextPrompt;
            retryVideo = shotVisible = prepared = false;
            waiting = true;
            gate.Cancel();
            HideChoices();
            narration.Hide();
            if (!morphCurrent) viskoClient.SetOutputMuted(true);
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
                bool morph = morphCurrent && generating && !reconnect;
                // Outcomes steer the existing shot. Fresh fallback/retry scenes reset
                // generation; normal next-stage shots use the independent standby.
                if (generating && !morph)
                {
                    sharedVideo.RetainExternalFrame();
                    viskoClient.ResetGeneration();
                }
                gate.Begin(morph, playbackBufferSeconds);
                viskoClient.SetPrompt(prompt);
                LogViskoPrompt(morph ? "CURRENT SCENE / selected outcome or minigame result" : "SCENE / start or retry",
                    playbackStage, story.PendingRequestId, prompt, audioPrompt);
                if (!string.IsNullOrWhiteSpace(audioPrompt)) viskoClient.SetAudioPrompt(audioPrompt);
                if (!morph) viskoClient.StartGeneration();
                generating = true;
                Debug.Log($"[StageVisko] Shot {version}: {(morph ? "morph" : "start")}; waiting for accepted prompt, chunks and decoded frames.", this);
            }
            catch (Exception) { if (version == playbackVersion) VideoError(); }
        }
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogViskoPrompt(string purpose, int stage, int request, string video, string audio)
        {
            string choice = story.LastChoice != null ? story.LastChoice.label : "(none)";
            Debug.Log($"[ViskoPrompt] {purpose} | Stage {stage} | Request {request}\n" +
                $"Selected choice: {choice}\nvideo_prompt: {video}\naudio_prompt: {audio}", this);
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
            if (suspended) return;
            TickStandby();
            if (!wanted) return;
            bool playing = InStage && !gameManager.IsTransitioning && VideoMoving;
            if (!prewarming && !presentationOnly) story.SetVideoPlaybackPaused(outcomePlayback ? false : !shotVisible || !playing);
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
            if (endingPresentation)
            {
                if (shotVisible && playing && !waiting && !retryVideo)
                {
                    endingElapsed += Time.unscaledDeltaTime;
                    if (endingElapsed >= continueDelaySeconds) ShowContinue();
                }
                return;
            }
            if ((playbackStage != 3 || (fuelGame != null && fuelGame.ContinueReady)) &&
                outcomePlayback && story.NextContinueVisible && InStage && !gameManager.IsTransitioning &&
                !standbyFailed && !retryVideo && story.LastError == null)
                ShowContinue();
            if (waiting || !prepared)
            {
                return;
            }
            if (playbackStage == 3)
            {
                // Streaming has no video-ended event. Reveal after actual visible playback,
                // and never expose the Director's travel choices over the physical game.
                if (shotVisible && playing && !outcomePlayback && !fuelRevealed)
                {
                    fuelVideoElapsed += Time.unscaledDeltaTime;
                    if (fuelVideoElapsed >= fuelRevealSeconds)
                    {
                        fuelRevealed = true;
                        gameManager.RevealCurrentStage();
                    }
                }
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
                (outcomePlayback ? $"choice delay {continueDelaySeconds}s." : "scene timer started; choices remain hidden."), this);
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
        private void ShowContinue()
        {
            if (continueButton == null) return;
            foreach (var button in choiceButtons) button.gameObject.SetActive(false);
            continueButton.gameObject.SetActive(true);
            continueButton.interactable = endingPresentation || story.CanContinueNextScene;
            var label = continueButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = (endingPresentation || story.CanContinueNextScene) ? "CONTINUE" : "PREPARING...";
            choiceGroup.alpha = 1;
            choiceGroup.interactable = choiceGroup.blocksRaycasts = true;
            choiceRoot.SetActive(true);
        }
        private void ContinueClicked()
        {
            if (endingPresentation)
            {
                if (!InStage || !shotVisible || endingElapsed < continueDelaySeconds ||
                    continueButton == null || !continueButton.gameObject.activeInHierarchy || !continueButton.interactable) return;
                endingPresentation = false;
                HideChoices();
                gameManager.ShowEnding();
                return;
            }
            if (!InStage || gameManager.IsTransitioning || continueButton == null ||
                !continueButton.gameObject.activeInHierarchy || !continueButton.interactable) return;
            if (story.ContinueNextScene()) HideChoices();
        }
        private void Choose(int index)
        {
            if (!InStage || !choiceRoot.activeSelf || !choiceGroup.interactable) return;
            if (standbyFailed && standbyScene != null)
            {
                var next = standbyScene;
                int id = standbyRequest;
                HideChoices();
                PrepareNextVideo(id, next);
                return;
            }
            if (retryVideo) { Play(prompt, true); return; }
            if (story.NextContinueVisible && story.LastError == null)
            {
                if (story.ContinueNextScene()) HideChoices();
                return;
            }
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
                if (story.Phase == StoryPhase.Opening) story.HoldOpeningForVideo();
                Play(prompt, true);
                if (standbyScene != null)
                {
                    var next = standbyScene;
                    int id = standbyRequest;
                    PrepareNextVideo(id, next);
                }
            }
        }
        private void StopVideo(bool keepFrame)
        {
            endingPresentation = false;
            endingElapsed = 0;
            CancelStandby();
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
            CancelStandby();
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
            if (continueButton != null) continueButton.gameObject.SetActive(false);
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
