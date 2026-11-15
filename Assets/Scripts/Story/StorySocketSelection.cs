using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Optional adapter: configured Meta sockets own the pre-story selection.</summary>
    [DisallowMultipleComponent]
    public sealed class StorySocketSelection : MonoBehaviour
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private SnapInteractable[] sockets;
        private readonly List<StoryItemDefinition> docked = new List<StoryItemDefinition>();
        private readonly List<StoryItemDefinition> remove = new List<StoryItemDefinition>();

        private void Reset() => controller = GetComponent<StoryController>();

        private void LateUpdate() => RefreshSelection();

        public void RefreshSelection()
        {
            if (controller == null || controller.Phase != StoryPhase.SelectingItems ||
                sockets == null || sockets.Length == 0) return;
            docked.Clear();
            foreach (SnapInteractable socket in sockets)
            {
                if (socket == null || !socket.isActiveAndEnabled) continue;
                foreach (SnapInteractor interactor in socket.SelectingInteractors)
                {
                    if (interactor == null || !interactor.isActiveAndEnabled) continue;
                    StoryItem item = interactor.GetComponentInParent<StoryItem>();
                    if (item != null && item.Definition != null && !docked.Contains(item.Definition))
                        docked.Add(item.Definition);
                }
            }
            remove.Clear();
            foreach (StoryItemDefinition selected in controller.Inventory)
                if (!docked.Contains(selected)) remove.Add(selected);
            foreach (StoryItemDefinition item in remove) controller.DeselectItem(item);
            foreach (StoryItemDefinition item in docked) controller.TrySelectItem(item);
        }

        // Hook the start button here so a same-frame undock is reflected before confirming.
        public void StartStory()
        {
            RefreshSelection();
            if (controller != null) controller.StartStory();
        }

        /// <summary>Starts with whatever is docked, even fewer than the required count (grab time is up).</summary>
        public void StartStoryWithDockedItems()
        {
            RefreshSelection();
            if (controller != null) controller.TryStartStory(true);
        }
    }
}
