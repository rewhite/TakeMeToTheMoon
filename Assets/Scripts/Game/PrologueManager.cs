using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using TakeMeToTheMoon.Video;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Presentation only: plays the introduction before handing control to the main screen.</summary>
    [DefaultExecutionOrder(-500), DisallowMultipleComponent]
    public sealed class PrologueManager : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private CurvedVideoScreen screen;
        [Min(0)] [SerializeField] private float fadeInSeconds = 2f;
        [Tooltip("Checked: finish when the video ends. Unchecked: finish after Duration Seconds of playback.")]
        [SerializeField] private bool advanceWhenVideoEnds = true;
        [Min(0)] [SerializeField] private float durationSeconds = 15f;
        [Tooltip("Empty: show GameManager's Main. Otherwise load this scene from the Build Profile scene list.")]
        [SerializeField] private string nextSceneName;
        public UnityEvent Completed = new UnityEvent();

        private GameManager owner;
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
            fadeMaterial = new Material(originalMaterial) { name = "Prologue Screen (Runtime)" };
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

        public void Begin(GameManager manager)
        {
            Cancel();
            if (!isActiveAndEnabled || videoPlayer == null || screen == null || fadeMaterial == null)
            {
                Debug.LogError("Prologue requires an active manager, Video Player and Curved Video Screen with a material.", this);
                manager.ShowMain();
                return;
            }
            owner = manager;
            elapsed = 0;
            running = true;
            videoPlayer.isLooping = false;
            videoPlayer.started += OnStarted;
            videoPlayer.loopPointReached += OnVideoEnded;
            videoPlayer.errorReceived += OnVideoError;
            videoPlayer.Play();
        }

        private void OnStarted(VideoPlayer player) { playbackStarted = true; }
        private void Update()
        {
            if (!running || !playbackStarted) return;
            elapsed += Time.unscaledDeltaTime;
            SetOpacity(fadeInSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / fadeInSeconds));
            if (!advanceWhenVideoEnds && elapsed >= durationSeconds) Finish();
        }
        private void OnVideoEnded(VideoPlayer player) { if (running && advanceWhenVideoEnds) Finish(); }
        private void OnVideoError(VideoPlayer player, string message)
        {
            Debug.LogError("Prologue video playback failed: " + message, this);
            // Release the start flow even when a clip cannot be decoded on the device.
            Finish();
        }
        private void Finish()
        {
            if (!running) return;
            var manager = owner;
            Cancel();
            if (!string.IsNullOrWhiteSpace(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName))
                SceneManager.LoadSceneAsync(nextSceneName);
            else
            {
                if (!string.IsNullOrWhiteSpace(nextSceneName))
                    Debug.LogError("Prologue next scene is not in the build: " + nextSceneName, this);
                if (manager != null) manager.ShowMain();
            }
            Completed.Invoke();
        }
        public void Cancel()
        {
            running = playbackStarted = false;
            owner = null;
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
                screen.ScreenRenderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", originalMaterial.GetColor("_BaseColor"));
                screen.ScreenRenderer.SetPropertyBlock(properties);
            }
            if (glow != null) glow.intensity = originalGlow;
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }
    }
}
