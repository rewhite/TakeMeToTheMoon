using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Binds prefab-local UI without storing scene references inside the prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowScreen : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button returnToStartButton;
        private GameManager manager;

        public void Bind(GameManager owner)
        {
            Unbind();
            manager = owner;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                    canvas.worldCamera = Camera.main;
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (returnToStartButton != null) returnToStartButton.onClick.AddListener(ReturnToStart);
        }

        public void StartGame() { if (manager != null) manager.StartGame(); }
        public void ReturnToStart() { if (manager != null) manager.ReturnToStart(); }

        private void OnDestroy() => Unbind();
        public void Unbind()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartGame);
            if (returnToStartButton != null) returnToStartButton.onClick.RemoveListener(ReturnToStart);
            manager = null;
        }
    }
}
