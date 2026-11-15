using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Shared preplaced overlay for prologue pulses and stage 3 result flashes.</summary>
    [DefaultExecutionOrder(250), DisallowMultipleComponent]
    public sealed class PrologueEmergencyPulse : MonoBehaviour
    {
        [SerializeField] private GameManager manager;
        [SerializeField] private Camera eye;
        [SerializeField] private MeshRenderer surface;
        [SerializeField, Min(1)] private float cycleSeconds = 4;
        [SerializeField, Range(0, .4f)] private float peakOpacity = .16f;
        [SerializeField] private Color emergencyColor = new Color(1, .025f, .005f, 1);
        [Header("Fuel minigame feedback")]
        [SerializeField, Min(.1f)] private float feedbackDuration = 1.1f;
        [SerializeField, Range(0, .4f)] private float feedbackOpacity = .16f;
        [SerializeField] private Color successColor = new Color(.12f, .85f, .18f, 1);
        [SerializeField] private Color failureColor = new Color(1, .05f, .025f, 1);
        private float feedbackElapsed = -1;
        private Color feedbackColor;
        public bool IsFeedbackPlaying => feedbackElapsed >= 0;
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
            feedbackElapsed = -1;
            SetOpacity(0);
        }
        private void RefreshPhase()
        {
            ClearResult();
            bool next = manager != null && manager.isActiveAndEnabled && manager.Phase == GamePhase.Prologue;
            if (next != active) elapsed = 0;
            active = next;
            if (!active) SetOpacity(0);
        }
        public void PlayResult(bool success)
        {
            if (!isActiveAndEnabled || manager == null || !manager.isActiveAndEnabled ||
                manager.Phase != GamePhase.Main || manager.CurrentStageNumber != 3) return;
            feedbackColor = success ? successColor : failureColor;
            feedbackElapsed = 0;
        }
        public void ClearResult()
        {
            feedbackElapsed = -1;
            if (!active) SetOpacity(0);
        }
        private void LateUpdate()
        {
            if (eye == null || surface == null || manager == null || !manager.isActiveAndEnabled)
            {
                active = false;
                feedbackElapsed = -1;
                SetOpacity(0);
                return;
            }
            if (!active && !IsFeedbackPlaying) return;
            transform.SetPositionAndRotation(eye.transform.position, Quaternion.identity);
            transform.localScale = Vector3.one * Mathf.Max(2, eye.nearClipPlane * 8);
            if (IsFeedbackPlaying)
            {
                feedbackElapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(feedbackElapsed / Mathf.Max(.1f, feedbackDuration));
                // Quick soft onset, followed by a longer smooth fade. One pulse per result.
                float envelope = progress < .18f
                    ? Mathf.SmoothStep(0, 1, progress / .18f)
                    : 1 - Mathf.SmoothStep(0, 1, (progress - .18f) / .82f);
                SetOpacity(envelope * feedbackOpacity, feedbackColor);
                if (progress >= 1) ClearResult();
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            // Zero -> red -> zero smoothly, once per four seconds by default.
            float wave = .5f - .5f * Mathf.Cos(2 * Mathf.PI * elapsed / Mathf.Max(1, cycleSeconds));
            SetOpacity(wave * peakOpacity);
        }
        private void SetOpacity(float alpha, Color? tint = null)
        {
            CurrentOpacity = alpha;
            if (surface == null) return;
            surface.enabled = alpha > .0001f;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat("_Alpha", alpha);
            properties.SetColor("_Tint", tint ?? emergencyColor);
            surface.SetPropertyBlock(properties);
        }
    }
}
