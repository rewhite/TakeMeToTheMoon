using System;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Presentation only: preplaced empty-slot markers follow the tray and ghost state.</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class StoryInventoryPlaceholders : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public GameObject visual;
            public Transform plus;
        }
        [SerializeField] private StoryInventoryTray tray;
        [SerializeField] private StoryInventoryGhost ghost;
        [SerializeField] private Slot[] slots;
        [Tooltip("Optional XR center-eye transform. Falls back to Camera.main.")]
        [SerializeField] private Transform viewer;
        public int VisibleCount { get; private set; }

        private void LateUpdate()
        {
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            VisibleCount = 0;
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                bool visible = tray != null && tray.isActiveAndEnabled && tray.IsSelectingItems &&
                    !tray.IsSlotOccupied(i) && !(ghost != null && ghost.isActiveAndEnabled && ghost.PreviewedSlot == i);
                if (slot.visual != null)
                {
                    if (slot.visual.activeSelf != visible) slot.visual.SetActive(visible);
                    if (visible) VisibleCount++;
                }
                if (visible && viewer != null && slot.plus != null)
                {
                    Vector3 direction = slot.plus.position - viewer.position;
                    if (direction.sqrMagnitude > .000001f)
                        slot.plus.rotation = Quaternion.LookRotation(direction, viewer.up);
                }
            }
        }
        private void OnDisable()
        {
            VisibleCount = 0;
            if (slots != null)
                foreach (var slot in slots) if (slot.visual != null) slot.visual.SetActive(false);
        }
    }
}
