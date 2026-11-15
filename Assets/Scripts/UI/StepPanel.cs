using TMPro;
using UnityEngine;

namespace TakeMeToTheMoon.UI
{
    /// <summary>Displays supplied step information; does not own story progression.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class StepPanel : MonoBehaviour
    {
        [SerializeField] private string stepLabel = "STEP 1 · DORM";
        [SerializeField] private string progressLabel = "1 / 3";
        [SerializeField, TextArea(2, 5)] private string instruction = "Pick something to take before you leave";
        [SerializeField, Min(0)] private float minimumHeight = 150;
        [SerializeField, Min(0)] private float horizontalPadding = 36;
        [SerializeField, Min(0)] private float verticalPadding = 22;
        [SerializeField, Min(0)] private float rowSpacing = 14;
        [SerializeField] private RectTransform badge;
        [SerializeField] private TMP_Text stepText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text instructionText;

        private void OnEnable() => RefreshLayout();
        private void LateUpdate() => RefreshLayout();
        public void SetStepLabel(string value) { stepLabel = value ?? ""; RefreshLayout(); }
        public void SetProgressLabel(string value) { progressLabel = value ?? ""; RefreshLayout(); }
        public void SetProgress(int current, int total) => SetProgressLabel($"{current} / {total}");
        public void SetInstruction(string value) { instruction = value ?? ""; RefreshLayout(); }

        public void RefreshLayout()
        {
            var root = transform as RectTransform;
            if (root == null || badge == null || stepText == null || progressText == null || instructionText == null) return;
            var content = instructionText.transform.parent as RectTransform;
            if (content == null) return;
            float width = root.rect.width + content.sizeDelta.x - horizontalPadding * 2;
            if (width < 1) return;
            SetText(stepText, stepLabel);
            SetText(progressText, progressLabel);
            SetText(instructionText, instruction);
            bool hasStep = !string.IsNullOrWhiteSpace(stepLabel);
            bool hasProgress = !string.IsNullOrWhiteSpace(progressLabel);
            if (badge.gameObject.activeSelf != hasStep) badge.gameObject.SetActive(hasStep);
            float gap = hasStep && hasProgress ? 16 : 0;
            float counterWidth = hasProgress ? Mathf.Min(width * .3f, Mathf.Ceil(progressText.GetPreferredValues(progressLabel).x) + 2) : 0;
            float badgeWidth = hasStep ? Mathf.Max(1, Mathf.Min(width - counterWidth - gap, stepText.GetPreferredValues(stepLabel).x + 28)) : 0;
            float badgeHeight = hasStep ? Mathf.Max(30, Height(stepText, Mathf.Max(1, badgeWidth - 24)) + 12) : 0;
            float counterHeight = hasProgress ? Height(progressText, Mathf.Max(1, counterWidth)) : 0;
            float rowHeight = Mathf.Max(badgeHeight, counterHeight);
            float left = horizontalPadding + (width - badgeWidth - gap - counterWidth) * .5f;
            Place(badge, left, verticalPadding + (rowHeight - badgeHeight) * .5f, badgeWidth, badgeHeight);
            Place(progressText.rectTransform, left + badgeWidth + gap, verticalPadding + (rowHeight - counterHeight) * .5f, counterWidth, counterHeight);
            float textY = verticalPadding + rowHeight + (rowHeight > 0 ? rowSpacing : 0);
            float textHeight = string.IsNullOrWhiteSpace(instruction) ? 0 : Height(instructionText, width);
            Place(instructionText.rectTransform, horizontalPadding, textY, width, textHeight);
            float height = Mathf.Max(minimumHeight, textY + textHeight + verticalPadding - content.sizeDelta.y);
            if (Mathf.Abs(root.rect.height - height) > .1f) root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        private static void SetText(TMP_Text text, string value)
        {
            if (text.text != value) text.text = value;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
        }
        private static float Height(TMP_Text text, float width) => text.font == null ? 0 : Mathf.Ceil(text.GetPreferredValues(text.text, width, Mathf.Infinity).y);
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            var topLeft = new Vector2(0, 1);
            if (rect.anchorMin != topLeft) rect.anchorMin = topLeft;
            if (rect.anchorMax != topLeft) rect.anchorMax = topLeft;
            if (rect.pivot != topLeft) rect.pivot = topLeft;
            var position = new Vector2(x, -y);
            var size = new Vector2(w, h);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }
    }
}
