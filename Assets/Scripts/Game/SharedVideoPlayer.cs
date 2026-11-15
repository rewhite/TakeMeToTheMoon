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
        private float opacity, fadeFromOpacity;
        private System.Action started, ended;
        private Material originalMaterial, fadeMaterial;
        private VideoFloorGlow glow;
        private float originalGlow, elapsed;
        private bool running, playbackStarted;
        private MaterialPropertyBlock properties;

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
            PauseForTransition();
            started = onStarted;
            ended = onEnded;
            if (videoPlayer == null || screen == null || fadeMaterial == null)
            {
                Debug.LogError("Assign the shared VideoPlayer and CurvedVideoScreen.", this);
                return;
            }
            if (settings.videoClip == null && string.IsNullOrWhiteSpace(settings.videoUrl))
            {
                // No replacement clip: leave the previous frame/panel visible.
                SetOpacity(1);
                var startCallback = started;
                var endCallback = ended;
                started = ended = null;
                startCallback?.Invoke();
                endCallback?.Invoke();
                return;
            }
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
        private void Update()
        {
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

        private void StopPlayback(bool keepVisible)
        {
            running = playbackStarted = false;
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
            if (screen != null && originalMaterial != null)
            {
                screen.screenMaterial = originalMaterial;
                screen.ScreenRenderer.sharedMaterial = originalMaterial;
                screen.ScreenRenderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", originalMaterial.GetColor("_BaseColor"));
                screen.ScreenRenderer.SetPropertyBlock(properties);
            }
            if (glow != null) glow.intensity = originalGlow;
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }
    }
}
