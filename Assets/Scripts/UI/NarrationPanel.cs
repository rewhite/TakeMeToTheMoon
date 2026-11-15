using TMPro;
using UnityEngine;

namespace TakeMeToTheMoon.UI
{
    /// <summary>Presentation only; the story/game manager supplies narration.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class NarrationPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text narrationText;
        [SerializeField] private GameObject panel;
        [SerializeField, Min(0)] private float minimumHeight = 100f;

        void OnEnable() => RefreshHeight();
        void LateUpdate() => RefreshHeight();

        // Keep the chosen font size; grow the canvas instead of shrinking the text.
        void RefreshHeight()
        {
            if (narrationText == null || narrationText.font == null) return;
            var canvasRect = transform as RectTransform;
            if (canvasRect == null) return;

            narrationText.enableAutoSizing = false;
            var textRect = narrationText.rectTransform;
            float width = textRect.rect.width;
            if (width <= 0) return;

            // The prefab's stretched text has negative sizeDelta for its padding.
            float verticalPadding = Mathf.Max(0, -textRect.sizeDelta.y);
            float height = Mathf.Max(minimumHeight,
                Mathf.Ceil(narrationText.GetPreferredValues(narrationText.text, width, Mathf.Infinity).y
                    + verticalPadding));
            if (Mathf.Abs(canvasRect.rect.height - height) > 0.1f)
                canvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        public string Text => narrationText != null ? narrationText.text : string.Empty;

        /// <summary>Updates the text without changing visibility. Also usable from UnityEvents.</summary>
        public void SetText(string text)
        {
            if (narrationText != null) narrationText.text = text ?? string.Empty;
            RefreshHeight();
        }

        public void Show() => SetVisible(true);
        public void Hide() => SetVisible(false);
        public void Clear() => SetText(string.Empty);

        public void SetVisible(bool visible)
        {
            if (panel != null) panel.SetActive(visible);
        }
    }
}
