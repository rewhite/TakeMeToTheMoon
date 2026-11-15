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
            "First-person view, slightly handheld, bright saturated comedic look, orange heat haze, one continuous take.";
        [Tooltip("Connect when this component starts. Otherwise call Connect() (e.g. from the Start button) so the session is warm by the first scene.")]
        [SerializeField] private bool connectOnStart = true;
        [Tooltip("Send start again when Visko reports its chunk limit, so the picture keeps running.")]
        [SerializeField] private bool restartOnComplete = true;
        [SerializeField, Min(0)] private float screenFadeSeconds = 0.5f;
        [Tooltip("How long a scene's arrival_video_prompt plays before morphing to its video_prompt.")]
        [SerializeField, Min(0)] private float arrivalSeconds = 8f;

        private string pendingPrompt, pendingAudio;
        private bool pendingCut, hasPending, generating;
        private Texture shownTexture;
        private StoryScene arrivingScene;
        private float arrivalRemaining;

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
            if (visko != null && !visko.SessionReady) _ = visko.Connect();
        }

        public void Disconnect()
        {
            if (visko != null) visko.Disconnect();
            generating = false;
            if (sharedVideo != null) sharedVideo.HideExternal();
            shownTexture = null;
        }

        private void Update()
        {
            if (visko == null) return;
            if (arrivingScene != null && (arrivalRemaining -= Time.unscaledDeltaTime) <= 0)
            {
                StoryScene scene = arrivingScene;
                arrivingScene = null;
                Queue(scene.video_prompt, scene.audio_prompt, false);
            }
            if (hasPending && visko.SessionReady) Flush();
            // The WebRTC texture appears (and may be replaced) after connection.
            if (visko.VideoTexture != null && visko.VideoTexture != shownTexture && sharedVideo != null)
            {
                shownTexture = visko.VideoTexture;
                sharedVideo.ShowExternal(shownTexture, screenFadeSeconds);
            }
        }

        private void OnScenePresented(StoryScene scene)
        {
            bool cut = scene.transition == "cut" || !generating;
            // New checkpoint: play the journey there first, then morph into the checkpoint itself.
            if (!string.IsNullOrWhiteSpace(scene.arrival_video_prompt))
            {
                arrivingScene = scene;
                arrivalRemaining = arrivalSeconds;
                Queue(scene.arrival_video_prompt, scene.audio_prompt, cut);
                return;
            }
            arrivingScene = null;
            Queue(scene.video_prompt, scene.audio_prompt, cut);
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
            if (pendingCut || !generating)
            {
                Debug.Log("[ViskoBridge] CUT  " + pendingPrompt);
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

        private void OnViskoMessage(string type, string json)
        {
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
