using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game
{
    // Preplaced UI; showing feedback never instantiates a new object.
    public sealed class FuelResultToast : MonoBehaviour
    {
        public RawImage image;
        public CanvasGroup group;
        public Texture successImage, failureImage;
        [Min(.5f)] public float duration = 2.2f;
        public bool IsShowing => elapsed >= 0;
        private float elapsed = -1;
        private void OnEnable() => Clear();
        private void OnDisable() => Clear();
        public void Show(bool success)
        {
            image.texture = success ? successImage : failureImage;
            elapsed = 0;
            group.alpha = 0;
        }
        public void Clear()
        {
            elapsed = -1;
            if (group != null) group.alpha = 0;
        }
        private void Update()
        {
            if (!IsShowing) return;
            elapsed += Time.unscaledDeltaTime;
            float fadeIn = Mathf.SmoothStep(0, 1, elapsed / .15f);
            float fadeOut = Mathf.SmoothStep(0, 1, (duration - elapsed) / .4f);
            group.alpha = Mathf.Min(fadeIn, fadeOut);
            if (elapsed >= duration) Clear();
        }
    }
}
