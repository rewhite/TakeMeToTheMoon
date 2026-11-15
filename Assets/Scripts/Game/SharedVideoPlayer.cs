using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using TakeMeToTheMoon.Video;

namespace TakeMeToTheMoon.Game
{
    /// <summary>One persistent video player and fading surface shared by all presentations.</summary>
    [DefaultExecutionOrder(-500), DisallowMultipleComponent]
    public sealed class SharedVideoPlayer : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private CurvedVideoScreen screen;
        private AudioSource videoAudio;
        private float fadeInSeconds;
        private float opacity, fadeFromOpacity;
        private System.Action started, ended;
        private Material originalMaterial, fadeMaterial;
        private VideoFloorGlow glow;
        private float originalGlow, elapsed;
        private bool running, playbackStarted, showingExternal;
        private MaterialPropertyBlock properties;
        private Texture originalTexture, externalTexture;
        private float externalFadeSeconds;

        /// <summary>True while a live external texture (e.g. the Visko stream) is on the screen.</summary>
        public bool ShowingExternal => showingExternal;
        public bool PlaybackFailed { get; private set; }

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            if (videoPlayer != null)
            {
                videoPlayer.playOnAwake = false;
                videoPlayer.Stop();
                ConfigureVideoAudio();
            }
            if (screen == null || screen.screenMaterial == null) return;
            originalMaterial = screen.screenMaterial;
            originalTexture = screen.sourceTexture;
            fadeMaterial = new Material(originalMaterial) { name = "Shared Video Screen (Runtime)" };
            // The project's video surface uses URP Unlit. Enable actual transparency, not just a black fade.
            fadeMaterial.SetFloat("_Surface", 1);
            fadeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fadeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fadeMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            fadeMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            fadeMaterial.SetFloat("_ZWrite", 0);
            fadeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fadeMaterial.SetOverrideTag("RenderType", "Transparent");
            fadeMaterial.renderQueue = (int)RenderQueue.Transparent;
            screen.screenMaterial = fadeMaterial;
            screen.ScreenRenderer.sharedMaterial = fadeMaterial;
            glow = screen.GetComponent<VideoFloorGlow>();
            if (glow != null) originalGlow = glow.intensity;
            SetOpacity(0);
        }

        private void ConfigureVideoAudio()
        {
            // Use Unity's centered stereo mixer for local clips as well as streamed video.
            // Avoid a separate platform-direct audio path on Quest.
            if (videoAudio == null)
                videoAudio = videoPlayer.GetComponent<AudioSource>();
            // Unity's missing-component wrapper compares equal to null, but is not CLR null.
            // Do not use ?? here: it can retain that wrapper and abort Awake.
            if (videoAudio == null)
                videoAudio = videoPlayer.gameObject.AddComponent<AudioSource>();
            videoAudio.playOnAwake = false;
            videoAudio.spatialBlend = 0f;
            videoAudio.panStereo = 0f;
            videoAudio.spatialize = false;
            videoAudio.dopplerLevel = 0f;
            videoAudio.bypassReverbZones = true;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.controlledAudioTrackCount = 1;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetTargetAudioSource(0, videoAudio);
        }

        public void Play(StagePresentation settings, System.Action onStarted, System.Action onEnded)
        {
            PauseForTransition();
            PlaybackFailed = false;
            started = onStarted;
            ended = onEnded;
            if (videoPlayer == null || screen == null || fadeMaterial == null)
            {
                Debug.LogError("Assign the shared VideoPlayer and CurvedVideoScreen.", this);
                return;
            }
            if (settings.videoClip == null && string.IsNullOrWhiteSpace(settings.videoUrl))
            {
                // Prefer the live stream; otherwise retain the previous frame.
                if (externalTexture != null) BeginExternal(settings.videoFadeSeconds);
                else SetOpacity(1);
                var startCallback = started;
                var endCallback = ended;
                started = ended = null;
                startCallback?.Invoke();
                endCallback?.Invoke();
                return;
            }
            screen.SetTexture(originalTexture);
            fadeFromOpacity = opacity;
            fadeInSeconds = settings.videoFadeSeconds;
            elapsed = 0;
            running = true;
            if (settings.videoClip != null)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = settings.videoClip;
            }
            else
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = settings.videoUrl;
            }
            ConfigureVideoAudio();
            videoPlayer.isLooping = false;
            videoPlayer.started += OnStarted;
            videoPlayer.loopPointReached += OnVideoEnded;
            videoPlayer.errorReceived += OnVideoError;
            videoPlayer.Play();
        }

        private void OnStarted(VideoPlayer player)
        {
            if (!running) return;
            playbackStarted = true;
            started?.Invoke();
        }
        /// <summary>
        /// Shows a live texture on the shared screen whenever no stage clip is playing. Stage clips
        /// take precedence; stages without a clip show this texture again.
        /// </summary>
        public void ShowExternal(Texture texture, float fadeSeconds = 0.5f)
        {
            externalTexture = texture;
            if (texture == null) { HideExternal(); return; }
            if (!running && screen != null && fadeMaterial != null) BeginExternal(fadeSeconds);
        }

        public void HideExternal()
        {
            externalTexture = null;
            if (!showingExternal) return;
            showingExternal = false;
            if (screen != null) screen.SetTexture(originalTexture);
            SetOpacity(0);
        }

        private RenderTexture retainedExternalFrame;
        /// <summary>Keep a valid last frame while the external track is disposed on stage exit.</summary>
        public void RetainExternalFrame()
        {
            if (externalTexture == null || externalTexture == retainedExternalFrame) return;
            if (retainedExternalFrame == null || retainedExternalFrame.width != externalTexture.width ||
                retainedExternalFrame.height != externalTexture.height)
            {
                if (retainedExternalFrame != null) Destroy(retainedExternalFrame);
                retainedExternalFrame = new RenderTexture(externalTexture.width, externalTexture.height, 0);
            }
            Graphics.Blit(externalTexture, retainedExternalFrame);
            ShowExternal(retainedExternalFrame, 0);
        }

        private void BeginExternal(float fadeSeconds)
        {
            if (showingExternal && screen.sourceTexture == externalTexture) return;
            // The live stream is already on screen (e.g. a stage change mid-story): keep it, no re-fade.
            if (screen.sourceTexture == externalTexture && opacity > .99f)
            {
                showingExternal = true;
                elapsed = externalFadeSeconds = 0;
                SetOpacity(1);
                return;
            }
            screen.SetTexture(externalTexture);
            showingExternal = true;
            externalFadeSeconds = fadeSeconds;
            fadeFromOpacity = opacity;
            elapsed = 0;
            if (fadeSeconds <= 0) SetOpacity(1);
        }

        private void Update()
        {
            if (showingExternal && !running)
            {
                elapsed += Time.unscaledDeltaTime;
                SetOpacity(externalFadeSeconds <= 0 ? 1 : Mathf.Lerp(fadeFromOpacity, 1, Mathf.Clamp01(elapsed / externalFadeSeconds)));
                return;
            }
            if (!running || !playbackStarted) return;
            elapsed += Time.unscaledDeltaTime;
            SetOpacity(fadeInSeconds <= 0 ? 1 : Mathf.Lerp(fadeFromOpacity, 1, Mathf.Clamp01(elapsed / fadeInSeconds)));
        }
        private void OnVideoEnded(VideoPlayer player)
        {
            if (!running) return;
            running = false;
            SetOpacity(1);
            var callback = ended;
            ended = null;
            callback?.Invoke();
        }
        private void OnVideoError(VideoPlayer player, string message)
        {
            PlaybackFailed = true;
            Debug.LogError("Stage video failed: " + message, this);
            // Start the time fallback or signal video-ended, according to the stage condition.
            var startCallback = started;
            var endCallback = ended;
            PauseForTransition();
            SetOpacity(1);
            startCallback?.Invoke();
            endCallback?.Invoke();
        }
        // Detach callbacks and pause without clearing the output or hiding its surface.
        public void PauseForTransition() => StopPlayback(true);
        public void Cancel() => StopPlayback(false);

        public void SkipCurrentVideo()
        {
            var callback = ended;
            PauseForTransition();
            SetOpacity(1);
            callback?.Invoke();
        }

        private void StopPlayback(bool keepVisible)
        {
            running = playbackStarted = showingExternal = false;
            started = ended = null;
            if (videoPlayer != null)
            {
                videoPlayer.started -= OnStarted;
                videoPlayer.loopPointReached -= OnVideoEnded;
                videoPlayer.errorReceived -= OnVideoError;
                if (keepVisible) videoPlayer.Pause();
                else videoPlayer.Stop();
            }
            if (!keepVisible) SetOpacity(0);
        }
        private void SetOpacity(float alpha)
        {
            opacity = Mathf.Clamp01(alpha);
            if (screen != null && fadeMaterial != null)
            {
                var renderer = screen.ScreenRenderer;
                renderer.GetPropertyBlock(properties);
                var color = originalMaterial.GetColor("_BaseColor");
                color.a *= alpha;
                properties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(properties);
            }
            if (glow != null) glow.intensity = originalGlow * alpha;
        }
        private void OnDisable() { Cancel(); }
        private void OnDestroy()
        {
            if (retainedExternalFrame != null) Destroy(retainedExternalFrame);
            if (screen != null && originalMaterial != null)
            {
                screen.screenMaterial = originalMaterial;
                screen.ScreenRenderer.sharedMaterial = originalMaterial;
                screen.SetTexture(originalTexture);
                screen.ScreenRenderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", originalMaterial.GetColor("_BaseColor"));
                screen.ScreenRenderer.SetPropertyBlock(properties);
            }
            if (glow != null) glow.intensity = originalGlow;
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }
    }
}
