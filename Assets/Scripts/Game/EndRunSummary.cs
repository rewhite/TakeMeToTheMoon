using System;
using System.Linq;
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

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            playAgain.onClick.AddListener(PlayAgain);
            var story = FindAnyObjectByType<StoryController>();
            if (story == null) { Show(new StoryVerdict { title = "Moon Reject" }, 0, Array.Empty<StoryHistoryEntry>()); return; }
            Show(story.GetVerdict(), story.FuelHits, story.History.ToArray());
        }
        private void OnDisable()
        {
            if (playAgain != null) playAgain.onClick.RemoveListener(PlayAgain);
        }
        public void Show(StoryVerdict result, int throwsSucceeded, StoryHistoryEntry[] history)
        {
            string type = result.archetype;
            int finalWellness = result.wellness;
            title.text = result.passed ? type : "Moon Reject";
            status.text = result.passed ? "YOU ARE" : "ACCESS DENIED";
            blurb.text = result.passed ? result.archetype_line : "You reached the Moon, but the colony has denied you entry.";
            detail.text = "";
            // Redeemed artwork contains the superseded blurb; Ace Pilot has no authored card yet.
            var match = type == "The Redeemed" || type == "The Ace Pilot" ? null : Array.Find(portraits, p => p.travelerType == type);
            portrait.sprite = match?.image;
            portrait.enabled = portrait.sprite != null;
            bool hasCard = portrait.enabled;
            title.gameObject.SetActive(!hasCard);
            blurb.gameObject.SetActive(!hasCard);
            status.gameObject.SetActive(!hasCard);
            wellness.text = finalWellness.ToString();
            wellness.color = finalWellness >= 85 ? new Color(.65f,.81f,.30f) : new Color(1,.48f,.25f);
            throws.text = throwsSucceeded + " / 3";
            for (int i = 0; i < choices.Length; i++)
            {
                var entry = Array.Find(history, h => h.checkpoint == i + 1);
                choices[i].text = entry == null ? "—"
                    : $"{entry.picked}   (Wellness {entry.wellness_change:+0;-0;0})";
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
