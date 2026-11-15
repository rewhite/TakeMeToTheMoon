using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Events;

namespace TakeMeToTheMoon.Game
{
    public enum GamePhase { StartScreen = 0, Main = 1, EndScreen = 2, Prologue = 3 }

    /// <summary>Start, prologue, main and ending presentation. Story systems stay in the scene.</summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Demo remote controls")]
        [SerializeField] private bool enableRemoteDebugCommands = true;
        private readonly RemoteDebugCommands remoteDebug = new RemoteDebugCommands();
        public bool IsTransitioning => transitioning;
        [Header("Shared")]
        [Tooltip("Parent for spawned prefab assets only. Scene objects keep their existing transforms.")]
        [SerializeField] private Transform contentRoot;
        [SerializeField] private SharedVideoPlayer sharedVideo;
        [Header("Stage Presentations")]
        [SerializeField] private StagePresentation prologue = new StagePresentation { title = "Prologue" };
        [SerializeField] private StagePresentation[] stages = new StagePresentation[]
        {
            new StagePresentation { title = "Stage 1" }, new StagePresentation { title = "Stage 2" },
            new StagePresentation { title = "Stage 3" }, new StagePresentation { title = "Stage 4" },
            new StagePresentation { title = "Stage 5" }
        };
        [Header("Start Screen")]
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [SerializeField] private GameObject startScreenPrefab;
        [SerializeField] private Material startScreenSkybox;
        [Header("Prologue")]
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [HideInInspector, SerializeField] private GameObject prologuePrefab;
        [HideInInspector, SerializeField] private Material prologueSkybox;
        [HideInInspector, SerializeField] private PrologueManager prologueManager;
        [Header("Main")]
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [HideInInspector, SerializeField] private GameObject mainPrefab;
        [HideInInspector, SerializeField] private Material mainSkybox;
        [Header("End Screen")]
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [SerializeField] private GameObject endScreenPrefab;
        [SerializeField] private Material endScreenSkybox;
        [Header("Transition")]
        [Tooltip("Total fade-out + fade-in time. Zero switches immediately.")]
        [Min(0)] [SerializeField] private float transitionDuration = 1f;
        private bool skipNextFade;
        [Tooltip("Optional. Uses Camera.main when empty.")]
        [SerializeField] private Camera transitionCamera;
        [SerializeField] private Shader transitionShader;
        [Header("Events")]
        public UnityEvent StateChanged = new UnityEvent();

        private StagePresentation currentStage;
        private bool waitingToReveal, playbackBegan;
        private bool externalFuelVideo;
        private float stageElapsed;
        public int CurrentStageNumber { get; private set; }
        private GameObject currentContent;
        private bool ownsCurrentContent;
        private Material originalSkybox;
        private Material imageSkybox;
        private Material panoramaBlend;
        private bool transitioning;
        private GameObject fadeSphere;
        private Material fadeMaterial;
        private Coroutine transitionRoutine;
        private System.Action pendingTransition;
        public GamePhase Phase { get; private set; } = GamePhase.StartScreen;

        private void OnEnable()
        {
            CurrentStageNumber = 0;
            originalSkybox = RenderSettings.skybox;
            HideSceneScreen(startScreenPrefab);
            HideSceneScreen(prologuePrefab);
            HideSceneScreen(mainPrefab);
            HideSceneScreen(endScreenPrefab);
            HideSceneScreen(prologue.screenPrefab);
            foreach (var stage in stages) if (stage != null) HideSceneScreen(stage.screenPrefab);
            // Scene-authored reveal targets may stay visible while editing their layout.
            HideRevealTargets(prologue);
            foreach (var stage in stages) HideRevealTargets(stage);
            ApplyScreen(GamePhase.StartScreen, startScreenPrefab, startScreenSkybox);
        }

        private void HideRevealTargets(StagePresentation stage)
        {
            if (stage == null || stage.revealObjects == null) return;
            foreach (var target in stage.revealObjects)
                if (target != null && !transform.IsChildOf(target.transform)) target.SetActive(false);
        }

        private void OnDisable()
        {
            remoteDebug.Disable();
            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = null;
            pendingTransition = null;
            transitioning = false;
            SetFade(0);
            if (fadeSphere != null) Destroy(fadeSphere);
            if (fadeMaterial != null) Destroy(fadeMaterial);
            StopStage();
            ClearContent();
            RenderSettings.skybox = originalSkybox;
            if (imageSkybox != null) Destroy(imageSkybox);
            imageSkybox = null;
            if (panoramaBlend != null) Destroy(panoramaBlend);
            panoramaBlend = null;
        }

        public void StartGame()
        {
            if (transitioning || Phase != GamePhase.StartScreen) return;
            ShowPrologue();
        }
        public void ShowPrologue()
        {
            Show(GamePhase.Prologue, prologue.screenPrefab, prologue.skybox, prologue);
        }
        // Compatibility for existing buttons/events: Main means stage 1.
        public void ShowMain() => ShowStage(1);
        /// <summary>
        /// Like ShowStage, without the fade to black: for changes during a continuous live stream
        /// (Visko), where the picture must not blank between checkpoints.
        /// </summary>
        public void ShowStageSeamless(int stageNumber)
        {
            skipNextFade = true;
            ShowStage(stageNumber);
        }

        public void ShowStage(int stageNumber)
        {
            if (stageNumber < 1 || stageNumber > stages.Length) { Debug.LogError("Stage number is out of range.", this); return; }
            var stage = stages[stageNumber - 1];
            if (stage == null) return;
            Show(GamePhase.Main, stage.screenPrefab, stage.skybox, stage, stageNumber);
        }
        public void ShowEndingVideoPreview()
        {
            // Remote commands reset presentation first, so this switch is immediate.
            skipNextFade = true;
            Show(GamePhase.Main, null, endScreenSkybox);
        }

        public void ShowEnding() => Show(GamePhase.EndScreen, endScreenPrefab, endScreenSkybox);
        public void ReturnToStart() => Show(GamePhase.StartScreen, startScreenPrefab, startScreenSkybox);

        public void ResetPresentationForDebug()
        {
            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = null;
            pendingTransition = null;
            transitioning = false;
            SetFade(0);
            StopStage();
            ClearContent();
            HideRevealTargets(prologue);
            foreach (var stage in stages) HideRevealTargets(stage);
            CurrentStageNumber = 0;
            ApplyScreen(GamePhase.StartScreen, startScreenPrefab, startScreenSkybox);
        }

        public void SkipVideoForDebug()
        {
            if (transitioning || currentStage == null) return;
            if (sharedVideo != null) sharedVideo.SkipCurrentVideo();
            RevealCurrentStage();
        }
        public void NextStage()
        {
            if (transitioning) return;
            if (Phase == GamePhase.StartScreen) StartGame();
            else if (Phase == GamePhase.Prologue) ShowMain();
            else if (Phase == GamePhase.Main)
            {
                if (CurrentStageNumber < stages.Length) ShowStage(CurrentStageNumber + 1);
                else ShowEnding();
            }
        }

        private void Show(GamePhase phase, GameObject prefab, Material skybox, StagePresentation stage = null, int number = 0)
        {
            if (!isActiveAndEnabled) return;
            if (prefab != null && prefab.scene.IsValid() && transform.IsChildOf(prefab.transform))
            {
                Debug.LogError("Screen roots must not contain GameManager. Assign a separate screen object.", this);
                return;
            }
            if (transitioning)
            {
                // Keep a request arriving from a timer/event instead of losing it mid-fade.
                pendingTransition = () => Show(phase, prefab, skybox, stage, number);
                return;
            }
            transitionRoutine = StartCoroutine(Transition(phase, prefab, skybox, stage, number));
        }

        private IEnumerator Transition(GamePhase phase, GameObject prefab, Material skybox, StagePresentation stage, int number)
        {
            transitioning = true;
            float halfDuration = Mathf.Max(0, transitionDuration) * .5f;
            bool blendSky = halfDuration > 0 && PreparePanoramaBlend(currentStage, stage);
            bool canFade = !blendSky && !skipNextFade && halfDuration > 0 && EnsureFadeSurface();
            skipNextFade = false;
            try
            {
                if (canFade) yield return Fade(0, 1, halfDuration);
                StopStage(stage != null);
                currentStage = stage;
                CurrentStageNumber = number;
                ApplyScreen(phase, prefab, ResolveSkybox(stage, skybox));
                SetRevealed(false);
                if (blendSky)
                {
                    RenderSettings.skybox = panoramaBlend;
                    float elapsed = 0;
                    float duration = halfDuration * 2;
                    while (elapsed < duration)
                    {
                        panoramaBlend.SetFloat("_Blend", Mathf.SmoothStep(0, 1, elapsed / duration));
                        yield return null;
                        elapsed += Time.unscaledDeltaTime;
                    }
                    RenderSettings.skybox = ResolveSkybox(stage, skybox);
                }
                else if (canFade) yield return Fade(1, 0, halfDuration);
            }
            finally
            {
                SetFade(0);
                transitioning = false;
                transitionRoutine = null;
            }
            var pending = pendingTransition;
            pendingTransition = null;
            if (pending != null) pending();
            else BeginStage();
        }

        private bool PreparePanoramaBlend(StagePresentation from, StagePresentation to)
        {
            if (from?.skyboxImage == null || to?.skyboxImage == null) return false;
            if (panoramaBlend == null)
            {
                var template = Resources.Load<Material>("StagePanoramaBlendTemplate");
                if (template == null || !template.shader.isSupported) return false;
                panoramaBlend = new Material(template) { name = "Stage Panorama Blend (Runtime)" };
            }
            panoramaBlend.SetTexture("_MainTex", from.skyboxImage);
            panoramaBlend.SetTexture("_NextTex", to.skyboxImage);
            panoramaBlend.SetFloat("_Rotation", from.skyboxRotation);
            panoramaBlend.SetFloat("_NextRotation", to.skyboxRotation);
            panoramaBlend.SetFloat("_Exposure", from.skyboxExposure);
            panoramaBlend.SetFloat("_NextExposure", to.skyboxExposure);
            panoramaBlend.SetFloat("_Blend", 0);
            return true;
        }

        private Material ResolveSkybox(StagePresentation stage, Material fallback)
        {
            if (stage == null || stage.skyboxImage == null) return fallback;
            if (imageSkybox == null)
            {
                // Resource material retains the panoramic shader in standalone Quest builds.
                var template = Resources.Load<Material>("StagePanoramaTemplate");
                if (template == null)
                {
                    Debug.LogError("Missing StagePanoramaTemplate material.", this);
                    return fallback;
                }
                imageSkybox = new Material(template) { name = "Stage Panorama (Runtime)" };
            }
            imageSkybox.SetTexture("_MainTex", stage.skyboxImage);
            imageSkybox.SetFloat("_Rotation", stage.skyboxRotation);
            imageSkybox.SetFloat("_Exposure", stage.skyboxExposure);
            return imageSkybox;
        }

        private void ApplyScreen(GamePhase phase, GameObject prefab, Material skybox)
        {
            ClearContent();
            Phase = phase;
            RenderSettings.skybox = skybox != null ? skybox : originalSkybox;
            if (prefab != null)
            {
                ownsCurrentContent = !prefab.scene.IsValid();
                currentContent = ownsCurrentContent
                    ? Instantiate(prefab, contentRoot != null ? contentRoot : transform, false)
                    : prefab;
                foreach (var screen in currentContent.GetComponentsInChildren<GameFlowScreen>(true))
                    screen.Bind(this);
                currentContent.SetActive(true);
            }
            StateChanged.Invoke();
        }

        private void BeginStage()
        {
            if (currentStage == null) return;
            waitingToReveal = true;
            playbackBegan = false;
            stageElapsed = 0;
            var presenter = CurrentStageNumber == 3 ? FindAnyObjectByType<StageViskoPresentation>() : null;
            externalFuelVideo = presenter != null && presenter.ControlsFuelVideo(this);
            if (sharedVideo != null)
            {
                // The live presenter owns Stage 3 readiness/reveal. Ignore the old local
                // demo clip even in scenes that still have it serialized.
                var media = externalFuelVideo ? new StagePresentation { videoFadeSeconds = 0 } : currentStage;
                sharedVideo.Play(media, () => playbackBegan = true, externalFuelVideo ? null : OnStageVideoEnded);
            }
            else
            {
                playbackBegan = true;
                if (currentStage.revealWhen == RevealCondition.VideoEnded)
                    Debug.LogWarning("Video-ended reveal needs Shared Video. Use RevealCurrentStage to continue.", this);
            }
        }

        private void Update()
        {
            remoteDebug.Tick(this, enableRemoteDebugCommands);
            // Inspector changes apply even after video/UI reveal has finished.
            if (!transitioning && currentStage != null)
            {
                var skybox = ResolveSkybox(currentStage, currentStage.skybox);
                RenderSettings.skybox = skybox != null ? skybox : originalSkybox;
            }
            if (transitioning || !waitingToReveal || !playbackBegan || currentStage == null) return;
            stageElapsed += Time.unscaledDeltaTime;
            if (!externalFuelVideo && currentStage.revealWhen == RevealCondition.AfterSeconds && stageElapsed >= currentStage.revealAfterSeconds)
                RevealCurrentStage();
        }

        private void OnStageVideoEnded()
        {
            if (currentStage != null && currentStage.revealWhen == RevealCondition.VideoEnded) RevealCurrentStage();
        }

        public void RevealCurrentStage()
        {
            if (!waitingToReveal || currentStage == null) return;
            waitingToReveal = false;
            SetRevealed(true);
            currentStage.Revealed.Invoke();
        }

        private void SetRevealed(bool visible)
        {
            if (currentStage != null && currentStage.revealObjects != null)
                foreach (var target in currentStage.revealObjects)
                    if (target != null && target != currentContent && !transform.IsChildOf(target.transform)) target.SetActive(visible);
            if (currentContent != null)
                foreach (var view in currentContent.GetComponentsInChildren<GameStageView>(true)) view.SetRevealed(visible);
        }

        private void StopStage(bool keepVideoVisible = false)
        {
            waitingToReveal = playbackBegan = false;
            if (sharedVideo != null)
            {
                if (keepVideoVisible) sharedVideo.PauseForTransition();
                else sharedVideo.Cancel();
            }
            SetRevealed(false);
            currentStage = null;
        }

        private bool EnsureFadeSurface()
        {
            var camera = transitionCamera != null ? transitionCamera : Camera.main;
            if (camera == null || transitionShader == null)
            {
                Debug.LogWarning("Transition needs a camera and Transition Shader; switching immediately.", this);
                return false;
            }
            if (fadeSphere == null)
            {
                fadeSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fadeSphere.name = "Screen Transition (Runtime)";
                var collider = fadeSphere.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                fadeMaterial = new Material(transitionShader);
                var renderer = fadeSphere.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = fadeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = short.MaxValue;
            }
            // Surround both eyes and follow the tracked camera. No physics or UI raycasts.
            fadeSphere.transform.SetParent(camera.transform, false);
            fadeSphere.transform.localPosition = Vector3.zero;
            fadeSphere.transform.localScale = Vector3.one * Mathf.Max(2f, camera.nearClipPlane * 8f);
            for (int layer = 0; layer < 32; layer++)
                if ((camera.cullingMask & (1 << layer)) != 0) { fadeSphere.layer = layer; break; }
            SetFade(0);
            return true;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float elapsed = 0;
            while (elapsed < seconds)
            {
                SetFade(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / seconds)));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            SetFade(to);
        }

        private void SetFade(float alpha)
        {
            if (fadeMaterial != null) fadeMaterial.SetFloat("_Alpha", alpha);
            if (fadeSphere != null) fadeSphere.SetActive(alpha > 0);
        }

        private void HideSceneScreen(GameObject screen)
        {
            if (screen != null && screen.scene.IsValid() && !transform.IsChildOf(screen.transform))
                screen.SetActive(false);
        }

        private void ClearContent()
        {
            if (currentContent == null) return;
            currentContent.SetActive(false);
            foreach (var screen in currentContent.GetComponentsInChildren<GameFlowScreen>(true))
                screen.Unbind();
            if (ownsCurrentContent) Destroy(currentContent);
            currentContent = null;
            ownsCurrentContent = false;
        }
    }
}
