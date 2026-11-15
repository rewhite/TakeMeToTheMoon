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
        private float fadeInSeconds;
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

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            if (videoPlayer != null)
            {
                videoPlayer.playOnAwake = false;
                videoPlayer.Stop();
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

        public void Play(StagePresentation settings, System.Action onStarted, System.Action onEnded)
        {
            Cancel();
            started = onStarted;
            ended = onEnded;
            if (videoPlayer == null || screen == null || fadeMaterial == null)
            {
                Debug.LogError("Assign the shared VideoPlayer and CurvedVideoScreen.", this);
                return;
            }
            if (settings.videoClip == null && string.IsNullOrWhiteSpace(settings.videoUrl))
            {
                if (externalTexture != null) BeginExternal(settings.videoFadeSeconds);
                started?.Invoke();
                return;
            }
            screen.SetTexture(originalTexture);
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

        private void BeginExternal(float fadeSeconds)
        {
            if (showingExternal && screen.sourceTexture == externalTexture) return;
            screen.SetTexture(externalTexture);
            showingExternal = true;
            externalFadeSeconds = fadeSeconds;
            elapsed = 0;
            SetOpacity(fadeSeconds <= 0 ? 1 : 0);
        }

        private void Update()
        {
            if (showingExternal && !running)
            {
                elapsed += Time.unscaledDeltaTime;
                SetOpacity(externalFadeSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / externalFadeSeconds));
                return;
            }
            if (!running || !playbackStarted) return;
            elapsed += Time.unscaledDeltaTime;
            SetOpacity(fadeInSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / fadeInSeconds));
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
            Debug.LogError("Stage video failed: " + message, this);
            // Start the time fallback or signal video-ended, according to the stage condition.
            var startCallback = started;
            var endCallback = ended;
            Cancel();
            startCallback?.Invoke();
            endCallback?.Invoke();
        }
        public void Cancel()
        {
            running = playbackStarted = showingExternal = false;
            started = ended = null;
            if (videoPlayer != null)
            {
                videoPlayer.started -= OnStarted;
                videoPlayer.loopPointReached -= OnVideoEnded;
                videoPlayer.errorReceived -= OnVideoError;
                videoPlayer.Stop();
            }
            SetOpacity(0);
        }
        private void SetOpacity(float alpha)
        {
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
