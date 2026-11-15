using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TakeMeToTheMoon.Story;

namespace TakeMeToTheMoon.Game
{
    [DisallowMultipleComponent]
    public sealed class EndRunSummary : MonoBehaviour
    {
        [Serializable] public sealed class Portrait
        {
            public string travelerType;
            public Sprite image;
        }
        [SerializeField] private Portrait[] portraits;
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text title, blurb, detail, wellness, throws, status;
        [SerializeField] private TMP_Text[] choices;
        [SerializeField] private Button playAgain;
        private CancellationTokenSource cancellation;

        private async void OnEnable()
        {
            if (!Application.isPlaying) return;
            playAgain.onClick.AddListener(PlayAgain);
            var story = FindAnyObjectByType<StoryController>();
            if (story == null) { Show(null, 0, 0, Array.Empty<StoryHistoryEntry>()); return; }
            var history = story.ChoiceHistory;
            string type = story.TravelerType;
            Show(type, story.Wellness, story.ThrowsUsed, history);
            var director = FindAnyObjectByType<DirectorClient>();
            if (type == null || director == null) return;
            cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = cancellation.Token;
            string line = await director.RequestTravelerDetail(type, history, token);
            if (!token.IsCancellationRequested && this != null && isActiveAndEnabled) detail.text = line;
        }
        private void OnDisable()
        {
            if (playAgain != null) playAgain.onClick.RemoveListener(PlayAgain);
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        }
        public void Show(string type, int finalWellness, int throwsUsed, StoryHistoryEntry[] history)
        {
            title.text = type ?? "Mission Failed";
            status.text = type == null ? "JOURNEY COMPLETE" : "YOU ARE";
            blurb.text = type == null ? "The Moon is out of reach this time. Your journey is worth another try." : TravelerArchetypes.Blurb(type);
            detail.text = "";
            var match = Array.Find(portraits, p => p.travelerType == type);
            portrait.sprite = match?.image;
            portrait.enabled = portrait.sprite != null;
            bool hasCard = portrait.enabled;
            title.gameObject.SetActive(!hasCard);
            blurb.gameObject.SetActive(!hasCard);
            status.gameObject.SetActive(!hasCard);
            wellness.text = finalWellness.ToString();
            wellness.color = finalWellness >= 85 ? new Color(.65f,.81f,.30f) : new Color(1,.48f,.25f);
            throws.text = throwsUsed + " / 3";
            for (int i = 0; i < choices.Length; i++)
            {
                var entry = Array.Find(history, h => h.checkpoint == i + 1);
                choices[i].text = entry?.picked ?? "—";
            }
        }
        private void PlayAgain()
        {
            var manager = FindAnyObjectByType<GameManager>();
            if (manager == null) return;
            if (FindAnyObjectByType<StoryController>() != null) RemoteDebugCommands.Execute(manager, "reset");
            else manager.ReturnToStart();
        }
    }
}
