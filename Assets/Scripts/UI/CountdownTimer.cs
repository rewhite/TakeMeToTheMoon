using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace TakeMeToTheMoon.UI
{
    [DisallowMultipleComponent]
    public sealed class CountdownTimer : MonoBehaviour
    {
        [SerializeField, Min(0)] private float durationSeconds = 15;
        [SerializeField] private bool startOnEnable = true;
        [SerializeField] private TMP_Text secondsText;
        [SerializeField] private TimerRing ring;
        public UnityEvent Completed = new UnityEvent();
        public float RemainingSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        private float runDuration;

        private void OnEnable()
        {
            ResetTimer();
            if (startOnEnable) StartTimer();
        }
        private void OnDisable() => IsRunning = false;
        private void Update()
        {
            if (!IsRunning) return;
            RemainingSeconds = Mathf.Max(0, RemainingSeconds - Time.unscaledDeltaTime);
            Refresh();
            if (RemainingSeconds <= 0) Finish();
        }
        public void StartTimer()
        {
            ResetTimer();
            IsRunning = true;
            if (RemainingSeconds <= 0) Finish();
        }
        public void PauseTimer() => IsRunning = false;
        public void ResumeTimer() { if (RemainingSeconds > 0) IsRunning = true; }
        public void ResetTimer()
        {
            IsRunning = false;
            runDuration = Mathf.Max(0, durationSeconds);
            RemainingSeconds = runDuration;
            Refresh();
        }
        // Can be used by CONTINUE; repeated presses do not fire Completed again.
        public void CompleteNow() { if (IsRunning) Finish(); }
        private void Finish()
        {
            IsRunning = false;
            RemainingSeconds = 0;
            Refresh();
            Completed.Invoke();
        }
        private void Refresh()
        {
            if (secondsText != null) secondsText.text = Mathf.CeilToInt(RemainingSeconds).ToString();
            if (ring != null) ring.Fill = runDuration > 0 ? RemainingSeconds / runDuration : 0;
        }
    }
}
