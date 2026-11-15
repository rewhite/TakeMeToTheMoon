using TakeMeToTheMoon.Game;
using UnityEngine;
using Visko;

namespace TakeMeToTheMoon.Story
{
    /// <summary>
    /// Plays the story on the live Visko stream. ScenePresented cuts or morphs to the scene's
    /// video_prompt; OutcomePresented morphs to the outcome while the Director writes the next
    /// beat. The stream is shown on the shared screen through SharedVideoPlayer, so stage clips
    /// (e.g. the prologue) still take precedence. Mirrors WebTest/src/visko.js.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ViskoStoryBridge : MonoBehaviour
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private ViskoStreamClient visko;
        [SerializeField] private SharedVideoPlayer sharedVideo;
        [Tooltip("Appended to every video prompt so the look stays consistent (WebTest style line).")]
        [SerializeField, TextArea(2, 4)] private string styleLine =
            "First-person view, handheld survival footage, photorealistic, dim smoky blood-orange light, ash in the air, harsh shadows, one continuous take.";
        [Tooltip("Connect when this component starts. Otherwise call Connect() (e.g. from the Start button) so the session is warm by the first scene.")]
        [SerializeField] private bool connectOnStart = true;
        [Tooltip("Send start again when Visko reports its chunk limit, so the picture keeps running.")]
        [SerializeField] private bool restartOnComplete = true;
        [SerializeField, Min(0)] private float screenFadeSeconds = 0.5f;
        [Tooltip("During a cut, the last frame is frozen on screen until the new shot streams (no black gap).")]
        [SerializeField, Min(.5f)] private float holdFrameMaxSeconds = 4f;
        [SerializeField, Min(0)] private float holdFrameMinSeconds = 1f;
        [Tooltip("How long a scene's arrival_video_prompt plays before morphing to its video_prompt.")]
        [SerializeField, Min(0)] private float arrivalSeconds = 8f;

        private string pendingPrompt, pendingAudio;
        private bool pendingCut, hasPending, generating;
        private Texture shownTexture;
        private StoryScene arrivingScene;
        private string lastViskoStatus, currentPrompt, currentAudio;
        private bool wanted;
        private float arrivalRemaining;
        private RenderTexture heldFrame;
        private bool holding, newShotStreaming;
        private float holdStarted;

        /// <summary>How long a new checkpoint's arrival journey plays before its scene.</summary>
        public float ArrivalSeconds => arrivalSeconds;

        private void Reset()
        {
            controller = FindAnyObjectByType<StoryController>();
            visko = FindAnyObjectByType<ViskoStreamClient>();
            sharedVideo = FindAnyObjectByType<SharedVideoPlayer>();
        }

        private void Awake()
        {
            // Runs before the client's Start: the bridge owns connection timing, prompts and the screen.
            if (visko == null) return;
            visko.connectOnStart = false;
            visko.autoStart = false;
            visko.externalScreen = true;
            visko.showWorldStatus = false;
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.ScenePresented.AddListener(OnScenePresented);
                controller.OutcomePresented.AddListener(OnOutcomePresented);
            }
            if (visko != null) visko.MessageReceived += OnViskoMessage;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.ScenePresented.RemoveListener(OnScenePresented);
                controller.OutcomePresented.RemoveListener(OnOutcomePresented);
            }
            if (visko != null) visko.MessageReceived -= OnViskoMessage;
        }

        private void Start()
        {
            if (visko == null) { Debug.LogError("[ViskoBridge] Assign the ViskoStreamClient.", this); return; }
            if (connectOnStart) Connect();
        }

        /// <summary>Starts the paid Visko session. Safe to call more than once.</summary>
        public void Connect()
        {
            wanted = true;
            if (visko != null && !visko.SessionReady) _ = visko.Connect();
        }

        // ViskoStreamClient disconnects when the app pauses (headset off, system menu). Reconnect on
        // resume and restart the current shot so the story picks up where it was.
        private void OnApplicationPause(bool paused)
        {
            if (paused || !wanted || visko == null) return;
            Debug.Log("[ViskoBridge] Resumed; reconnecting Visko.");
            generating = false;
            shownTexture = null;
            if (!string.IsNullOrEmpty(currentPrompt) && !hasPending)
            {
                pendingPrompt = currentPrompt; pendingAudio = currentAudio; pendingCut = true; hasPending = true;
            }
            _ = visko.Connect();
        }

        public void Disconnect()
        {
            wanted = false;
            holding = false;
            currentPrompt = currentAudio = null;
            if (visko != null) visko.Disconnect();
            generating = false;
            if (sharedVideo != null) sharedVideo.HideExternal();
            shownTexture = null;
        }

        private void Update()
        {
            if (visko == null) return;
            if (visko.Status != lastViskoStatus)
            {
                lastViskoStatus = visko.Status;
                Debug.Log("[ViskoBridge] Visko status: " + lastViskoStatus);
            }
            if (arrivingScene != null && (arrivalRemaining -= Time.unscaledDeltaTime) <= 0)
            {
                StoryScene scene = arrivingScene;
                arrivingScene = null;
                // Arrived: morph into the checkpoint's establishing shot. Never reset mid-story: a reset
                // leaves Visko with no frames for a moment (black screen).
                Queue(scene.video_prompt, scene.audio_prompt, false);
            }
            if (hasPending && visko.SessionReady) Flush();
            if (holding)
            {
                float held = Time.unscaledTime - holdStarted;
                if ((newShotStreaming && held >= holdFrameMinSeconds) || held >= holdFrameMaxSeconds) ReleaseHeldFrame();
                return;
            }
            // The WebRTC texture appears (and may be replaced) after connection.
            if (visko.VideoTexture != null && visko.VideoTexture != shownTexture && sharedVideo != null)
            {
                shownTexture = visko.VideoTexture;
                Debug.Log($"[ViskoBridge] Showing stream {shownTexture.width}x{shownTexture.height} on the shared screen.");
                sharedVideo.ShowExternal(shownTexture, screenFadeSeconds);
            }
        }

        private void OnScenePresented(StoryScene scene)
        {
            // New checkpoint: the escape journey continues the outcome (morph), then the checkpoint
            // itself cuts to a fresh shot. Without a journey, a new place cuts straight away.
            if (!string.IsNullOrWhiteSpace(scene.arrival_video_prompt))
            {
                arrivingScene = scene;
                arrivalRemaining = arrivalSeconds;
                Queue(scene.arrival_video_prompt, scene.audio_prompt, !generating);
                return;
            }
            arrivingScene = null;
            Queue(scene.video_prompt, scene.audio_prompt, !generating);
        }

        private void OnOutcomePresented(StoryChoice choice)
        {
            arrivingScene = null;
            Queue(choice.outcome_video_prompt, null, false);
        }

        private void Queue(string prompt, string audio, bool cut)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            pendingPrompt = WithStyle(prompt);
            pendingAudio = audio;
            // A cut that has not been sent yet stays a cut even if a morph replaces its prompt.
            pendingCut = (hasPending && pendingCut) || cut;
            hasPending = true;
            if (visko != null && visko.SessionReady) Flush();
            else Debug.Log("[ViskoBridge] Session not ready; prompt will be sent on connect.");
        }

        private void Flush()
        {
            hasPending = false;
            currentPrompt = pendingPrompt;
            if (!string.IsNullOrWhiteSpace(pendingAudio)) currentAudio = pendingAudio;
            if (pendingCut || !generating)
            {
                Debug.Log("[ViskoBridge] CUT  " + pendingPrompt);
                if (generating) HoldCurrentFrame();
                visko.ResetGeneration();
                visko.SetPrompt(pendingPrompt);
                if (!string.IsNullOrWhiteSpace(pendingAudio)) visko.SetAudioPrompt(pendingAudio);
                visko.StartGeneration();
                generating = true;
            }
            else
            {
                Debug.Log("[ViskoBridge] MORPH " + pendingPrompt);
                visko.SetPrompt(pendingPrompt);
                if (!string.IsNullOrWhiteSpace(pendingAudio)) visko.SetAudioPrompt(pendingAudio);
            }
            pendingCut = false;
        }

        // Freeze the last streamed frame on screen while Visko resets and starts the new shot.
        private void HoldCurrentFrame()
        {
            Texture live = visko.VideoTexture;
            if (live == null || sharedVideo == null) return;
            if (heldFrame == null || heldFrame.width != live.width || heldFrame.height != live.height)
            {
                if (heldFrame != null) heldFrame.Release();
                heldFrame = new RenderTexture(live.width, live.height, 0) { name = "Visko Held Frame" };
            }
            Graphics.Blit(live, heldFrame);
            sharedVideo.ShowExternal(heldFrame, 0);
            holding = true;
            newShotStreaming = false;
            holdStarted = Time.unscaledTime;
        }

        private void ReleaseHeldFrame()
        {
            holding = false;
            shownTexture = visko.VideoTexture;
            if (shownTexture != null && sharedVideo != null) sharedVideo.ShowExternal(shownTexture, 0);
            Debug.Log($"[ViskoBridge] New shot on screen after {Time.unscaledTime - holdStarted:0.0}s held frame.");
        }

        private void OnDestroy()
        {
            if (heldFrame != null) { heldFrame.Release(); Destroy(heldFrame); }
        }

        private void OnViskoMessage(string type, string json)
        {
            // The first chunk of the new shot has been generated: it is streaming now.
            // Only a chunk after the reset counts (a late chunk of the old shot used to release too early).
            if (holding && type == "chunk_complete" && visko.Status == "Generating" &&
                Time.unscaledTime - holdStarted > .3f) newShotStreaming = true;
            if (type == "generation_complete")
            {
                Debug.Log("[ViskoBridge] Chunk limit reached" + (restartOnComplete ? "; restarting." : "."));
                if (restartOnComplete && visko.SessionReady) visko.StartGeneration();
                else generating = false;
            }
        }

        private string WithStyle(string prompt) =>
            string.IsNullOrWhiteSpace(styleLine) ? prompt.Trim() : prompt.Trim() + " " + styleLine.Trim();
    }
}
