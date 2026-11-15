using TMPro;
using TakeMeToTheMoon.Story;
using UnityEngine;

namespace TakeMeToTheMoon.UI
{
    /// <summary>Displays StoryController wellness without owning or modifying story state.</summary>
    [DisallowMultipleComponent]
    public sealed class WellnessBar : MonoBehaviour
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private bool followStory = true;
        [SerializeField, Range(0, 100)] private int previewValue = 100;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private RectTransform heart;
        [SerializeField, Min(1)] private float healthyBpm = 55;
        [SerializeField, Min(1)] private float lowWellnessBpm = 150;
        [SerializeField, Range(0, .4f)] private float pulseAmount = .15f;
        private StoryController subscribed;
        private Vector3 restingScale;
        private float phase;
        public int Value { get; private set; } = 100;
        public float BeatsPerMinute => Mathf.Lerp(lowWellnessBpm, healthyBpm, Value / 100f);

        private void Awake() { if (heart != null) restingScale = heart.localScale; }
        private void OnEnable()
        {
            if (followStory)
            {
                if (controller == null) controller = FindFirstObjectByType<StoryController>(FindObjectsInactive.Include);
                subscribed = controller;
                if (subscribed != null) subscribed.StateChanged.AddListener(Refresh);
            }
            phase = 0;
            Refresh();
        }
        private void OnDisable()
        {
            if (subscribed != null) subscribed.StateChanged.RemoveListener(Refresh);
            subscribed = null;
            if (heart != null) heart.localScale = restingScale;
        }
        private void Update()
        {
            phase = Mathf.Repeat(phase + Time.unscaledDeltaTime * BeatsPerMinute / 60f, 1);
            // Two quick beats followed by a rest, instead of uniform breathing.
            float pulse = Bump(phase, .04f, .20f) + .55f * Bump(phase, .24f, .40f);
            if (heart != null) heart.localScale = restingScale * (1 + pulseAmount * pulse);
        }
        private static float Bump(float value, float start, float end)
        {
            if (value <= start || value >= end) return 0;
            float sine = Mathf.Sin((value - start) / (end - start) * Mathf.PI);
            return sine * sine;
        }
        public void Refresh()
        {
            Value = Mathf.Clamp(followStory && controller != null ? controller.Wellness : previewValue, 0, 100);
            if (valueText != null) valueText.text = Value.ToString();
        }
        // Presentation-only mode can be driven from a UnityEvent. Bound story values remain authoritative.
        public void SetValue(int value) { previewValue = Mathf.Clamp(value, 0, 100); Refresh(); }
        private void OnValidate() => Refresh();
    }
}
