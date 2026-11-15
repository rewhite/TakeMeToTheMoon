using UnityEngine;
using UnityEngine.Events;

namespace TakeMeToTheMoon.Game
{
    public enum GamePhase { StartScreen = 0, Main = 1, EndScreen = 2, Prologue = 3 }

    /// <summary>Three presentation states. Video and story systems stay in the scene.</summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        [Tooltip("Parent for spawned prefab assets only. Scene objects keep their existing transforms.")]
        [SerializeField] private Transform contentRoot;
        [SerializeField] private PrologueManager prologueManager;
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [SerializeField] private GameObject startScreenPrefab;
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [SerializeField] private GameObject mainPrefab;
        [Tooltip("Drag a Hierarchy object to reuse it, or a Project prefab asset to spawn it.")]
        [SerializeField] private GameObject endScreenPrefab;
        [SerializeField] private Material startScreenSkybox;
        [SerializeField] private Material mainSkybox;
        [SerializeField] private Material endScreenSkybox;
        public UnityEvent StateChanged = new UnityEvent();

        private GameObject currentContent;
        private bool ownsCurrentContent;
        private Material originalSkybox;
        private bool transitioning;
        public GamePhase Phase { get; private set; } = GamePhase.StartScreen;

        private void OnEnable()
        {
            originalSkybox = RenderSettings.skybox;
            HideSceneScreen(startScreenPrefab);
            HideSceneScreen(mainPrefab);
            HideSceneScreen(endScreenPrefab);
            ReturnToStart();
        }

        private void OnDisable()
        {
            if (prologueManager != null) prologueManager.Cancel();
            ClearContent();
            RenderSettings.skybox = originalSkybox;
        }

        public void StartGame()
        {
            if (Phase != GamePhase.StartScreen) return;
            if (prologueManager == null) { ShowMain(); return; }
            Show(GamePhase.Prologue, null, mainSkybox);
            prologueManager.Begin(this);
        }
        public void ShowMain() => Show(GamePhase.Main, mainPrefab, mainSkybox);
        public void ShowEnding() => Show(GamePhase.EndScreen, endScreenPrefab, endScreenSkybox);
        public void ReturnToStart() => Show(GamePhase.StartScreen, startScreenPrefab, startScreenSkybox);
        public void NextStage()
        {
            if (Phase == GamePhase.StartScreen) StartGame();
            else if (Phase == GamePhase.Prologue) ShowMain();
            else if (Phase == GamePhase.Main) ShowEnding();
        }

        private void Show(GamePhase phase, GameObject prefab, Material skybox)
        {
            if (!isActiveAndEnabled || transitioning) return;
            if (prefab != null && prefab.scene.IsValid() && transform.IsChildOf(prefab.transform))
            {
                Debug.LogError("Screen roots must not contain GameManager. Assign a separate screen object.", this);
                return;
            }
            transitioning = true;
            try
            {
                if (prologueManager != null) prologueManager.Cancel();
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
            finally { transitioning = false; }
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
