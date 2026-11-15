using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TakeMeToTheMoon.UI
{
    /// <summary>Select one UI input path before Meta's exclusive module starts.</summary>
    [DefaultExecutionOrder(-10000), DisallowMultipleComponent]
    [RequireComponent(typeof(EventSystem))]
    public sealed class UIInputModeSwitcher : MonoBehaviour
    {
        [Tooltip("Enable only when testing Quest Link/Meta input in the Editor. Otherwise Editor uses mouse UI.")]
        [SerializeField] private bool useMetaInputInEditor;

        private void Awake() => ApplyMode();
        private void OnEnable() => ApplyMode();

        [ContextMenu("Apply Input Mode")]
        public void ApplyMode()
        {
#if UNITY_EDITOR
            bool useMeta = useMetaInputInEditor;
#elif UNITY_ANDROID
            bool useMeta = true;
#else
            bool useMeta = false;
#endif
            var mouse = GetComponent<InputSystemUIInputModule>();
            var meta = GetComponent<PointableCanvasModule>();
            if (mouse == null || meta == null)
            {
                Debug.LogError("UIInputModeSwitcher needs InputSystemUIInputModule and PointableCanvasModule on this EventSystem.", this);
                return;
            }
            // Clear exclusivity before enabling mouse input; Meta otherwise disables it again.
            meta.ExclusiveMode = useMeta;
            if (useMeta)
            {
                mouse.enabled = false;
                meta.enabled = true;
            }
            else
            {
                meta.enabled = false;
                mouse.enabled = true;
            }
        }
    }
}
