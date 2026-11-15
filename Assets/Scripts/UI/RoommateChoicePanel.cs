using TakeMeToTheMoon.Story;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI
{
    public sealed class RoommateChoicePanel : MonoBehaviour
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private Button wakeButton;
        [SerializeField] private Button leaveButton;
        private void OnEnable()
        {
            if (controller != null)
            {
                controller.StateChanged.AddListener(Refresh);
                // Stage reveal may precede the story opening timer.
                if (controller.Phase == StoryPhase.Opening) controller.AdvanceOpening();
            }
            wakeButton.onClick.AddListener(Wake);
            leaveButton.onClick.AddListener(Leave);
            Refresh();
        }
        private void OnDisable()
        {
            if (controller != null) controller.StateChanged.RemoveListener(Refresh);
            if (wakeButton != null) wakeButton.onClick.RemoveListener(Wake);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(Leave);
        }
        private bool IsRoommateChoice => controller != null && controller.Phase == StoryPhase.AwaitingChoice &&
            controller.CurrentScene?.choices != null && controller.CurrentScene.choices.Length == 2 &&
            !string.IsNullOrEmpty(controller.CurrentScene.choices[0].roommate);
        private void Refresh()
        {
            if (wakeButton != null) wakeButton.interactable = IsRoommateChoice && controller.CanChoose(0);
            if (leaveButton != null) leaveButton.interactable = IsRoommateChoice && controller.CanChoose(1);
        }
        public void Wake() => Choose(0);
        public void Leave() => Choose(1);
        private void Choose(int index)
        {
            if (IsRoommateChoice && controller.TryChoose(index)) gameObject.SetActive(false);
        }
    }
}
