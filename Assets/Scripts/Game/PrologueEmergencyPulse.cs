using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Gentle prologue-only color overlay on a preplaced inward-facing sphere.</summary>
    [DefaultExecutionOrder(250), DisallowMultipleComponent]
    public sealed class PrologueEmergencyPulse : MonoBehaviour
    {
        [SerializeField] private GameManager manager;
        [SerializeField] private Camera eye;
        [SerializeField] private MeshRenderer surface;
        [SerializeField, Min(1)] private float cycleSeconds = 4;
        [SerializeField, Range(0, .4f)] private float peakOpacity = .16f;
        [SerializeField] private Color emergencyColor = new Color(1, .025f, .005f, 1);
        private MaterialPropertyBlock properties;
        private float elapsed;
        private bool active;
        public bool IsPulsing => active;
        public float CurrentOpacity { get; private set; }
        private void OnEnable()
        {
            properties = new MaterialPropertyBlock();
            if (manager != null) manager.StateChanged.AddListener(RefreshPhase);
            RefreshPhase();
        }
        private void OnDisable()
        {
            if (manager != null) manager.StateChanged.RemoveListener(RefreshPhase);
            active = false;
            SetOpacity(0);
        }
        private void RefreshPhase()
        {
            bool next = manager != null && manager.isActiveAndEnabled && manager.Phase == GamePhase.Prologue;
            if (next != active) elapsed = 0;
            active = next;
            if (!active) SetOpacity(0);
        }
        private void LateUpdate()
        {
            if (eye == null || surface == null || manager == null || !manager.isActiveAndEnabled)
            {
                active = false;
                SetOpacity(0);
                return;
            }
            if (!active) return;
            transform.SetPositionAndRotation(eye.transform.position, Quaternion.identity);
            transform.localScale = Vector3.one * Mathf.Max(2, eye.nearClipPlane * 8);
            elapsed += Time.unscaledDeltaTime;
            // Zero -> red -> zero smoothly, once per four seconds by default.
            float wave = .5f - .5f * Mathf.Cos(2 * Mathf.PI * elapsed / Mathf.Max(1, cycleSeconds));
            SetOpacity(wave * peakOpacity);
        }
        private void SetOpacity(float alpha)
        {
            CurrentOpacity = alpha;
            if (surface == null) return;
            surface.enabled = alpha > .0001f;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat("_Alpha", alpha);
            properties.SetColor("_Tint", emergencyColor);
            surface.SetPropertyBlock(properties);
        }
    }
}
