using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.Story;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI
{
    public sealed class InventoryContinueButton : MonoBehaviour
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private StorySocketSelection selection;
        [SerializeField] private GameManager manager;
        [SerializeField] private Button button;

        private void OnEnable()
        {
            if (controller != null) controller.StateChanged.AddListener(Refresh);
            if (button != null) button.onClick.AddListener(Continue);
            Refresh();
        }
        private void OnDisable()
        {
            if (controller != null) controller.StateChanged.RemoveListener(Refresh);
            if (button != null) button.onClick.RemoveListener(Continue);
        }
        private void Refresh()
        {
            if (button == null) return;
            bool ready = controller != null && controller.CanStart;
            button.interactable = ready;
            button.gameObject.SetActive(ready);
        }
        public void Continue()
        {
            // Recheck physical sockets before accepting a same-frame click/undock.
            if (selection == null || controller == null) return;
            selection.RefreshSelection();
            if (!controller.TryStartStory()) { Refresh(); return; }
            if (manager != null) manager.ShowMain();
        }
    }
}
