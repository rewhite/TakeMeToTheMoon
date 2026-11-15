using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI
{
    // Presentation only. Wire ContinueRequested to the desired story/game action.
    [ExecuteAlways]
    public sealed class IntroPanel : MonoBehaviour
    {
        [SerializeField] private Button continueButton;
        public UnityEvent ContinueRequested = new UnityEvent();
        [SerializeField, Min(0)] private float minimumHeight = 360;
        [SerializeField, Min(0)] private float horizontalPadding = 32;
        [SerializeField, Min(0)] private float topPadding = 28;
        [SerializeField, Min(0)] private float sectionSpacing = 20;
        [SerializeField, Min(0)] private float footerSpacing = 32;
        [SerializeField, Min(0)] private float bottomPadding = 30;
        private TMP_Text titleText, bodyText, hintText;
        private RectTransform content, badge;
        private bool continued;

        public TMP_Text TitleText { get { ResolveLayout(); return titleText; } }
        public TMP_Text BodyText { get { ResolveLayout(); return bodyText; } }
        public void SetTitle(string value) { if (TitleText != null) TitleText.text = value ?? ""; RefreshLayout(); }
        public void SetBody(string value) { if (BodyText != null) BodyText.text = value ?? ""; RefreshLayout(); }
        private void LateUpdate() => RefreshLayout();

        private void ResolveLayout()
        {
            if (content == null) content = transform.Find("Panel Base/Content") as RectTransform;
            if (content == null) return;
            if (titleText == null) titleText = content.Find("Title")?.GetComponent<TMP_Text>();
            if (bodyText == null) bodyText = content.Find("Body")?.GetComponent<TMP_Text>();
            if (hintText == null) hintText = content.Find("Hint")?.GetComponent<TMP_Text>();
            if (badge == null) badge = content.Find("Prologue Badge") as RectTransform;
        }

        public void RefreshLayout()
        {
            ResolveLayout();
            var root = transform as RectTransform;
            if (root == null || content == null || titleText == null || bodyText == null) return;
            // Content stretches within Panel Base; account for its existing outer padding.
            float width = root.rect.width + content.sizeDelta.x - 2 * horizontalPadding;
            if (width <= 1) return;
            float y = topPadding;
            if (badge != null && badge.gameObject.activeSelf)
            {
                Place(badge, horizontalPadding, y, badge.rect.width, badge.rect.height);
                y += badge.rect.height + sectionSpacing;
            }
            float titleHeight = Measure(titleText, width);
            Place(titleText.rectTransform, horizontalPadding, y, width, titleHeight);
            y += titleHeight + (titleHeight > 0 ? sectionSpacing : 0);
            float bodyHeight = Measure(bodyText, width);
            Place(bodyText.rectTransform, horizontalPadding, y, width, bodyHeight);
            y += bodyHeight + footerSpacing;
            var buttonRect = continueButton != null ? continueButton.transform as RectTransform : null;
            bool hasButton = buttonRect != null && continueButton.gameObject.activeSelf;
            float buttonWidth = hasButton ? Mathf.Min(278, width) : 0;
            float buttonHeight = hasButton ? 72 : 0;
            bool stack = hasButton && width < 600;
            float hintWidth = !hasButton || stack ? width : Mathf.Max(1, width - buttonWidth - sectionSpacing);
            float hintHeight = Measure(hintText, hintWidth);
            float footerHeight = stack ? hintHeight + (hintHeight > 0 ? sectionSpacing : 0) + buttonHeight
                : Mathf.Max(hintHeight, buttonHeight);
            float height = Mathf.Max(minimumHeight, y + footerHeight + bottomPadding - content.sizeDelta.y);
            // Keep footer at the bottom even when minimum height provides extra space.
            float footerY = height + content.sizeDelta.y - bottomPadding - footerHeight;
            if (hintText != null) Place(hintText.rectTransform, horizontalPadding,
                footerY + (stack ? 0 : (footerHeight - hintHeight) * .5f), hintWidth, hintHeight);
            if (hasButton) Place(buttonRect, horizontalPadding + width - buttonWidth,
                footerY + (stack ? footerHeight - buttonHeight : (footerHeight - buttonHeight) * .5f), buttonWidth, buttonHeight);
            if (Mathf.Abs(root.rect.height - height) > .1f)
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        private static float Measure(TMP_Text text, float width)
        {
            if (text == null || !text.gameObject.activeSelf || string.IsNullOrWhiteSpace(text.text) || text.font == null) return 0;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return Mathf.Ceil(text.GetPreferredValues(text.text, width, Mathf.Infinity).y);
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            var topLeft = new Vector2(0, 1);
            if (rect.anchorMin != topLeft) rect.anchorMin = topLeft;
            if (rect.anchorMax != topLeft) rect.anchorMax = topLeft;
            if (rect.pivot != topLeft) rect.pivot = topLeft;
            var position = new Vector2(x, -y);
            var size = new Vector2(width, height);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }
        private void OnEnable()
        {
            RefreshLayout();
            if (!Application.isPlaying) return;
            continued = false;
            if (continueButton != null)
            {
                continueButton.interactable = true;
                continueButton.onClick.AddListener(Continue);
            }
        }
        private void OnDisable()
        {
            if (continueButton != null) continueButton.onClick.RemoveListener(Continue);
        }
        public void Continue()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || continued) return;
            continued = true;
            if (continueButton != null) continueButton.interactable = false;
            ContinueRequested.Invoke();
        }
    }
}
