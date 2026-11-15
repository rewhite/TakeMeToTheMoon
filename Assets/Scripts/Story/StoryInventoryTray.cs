using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Shared eligibility and occupancy for three independent Meta sockets.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryInventoryTray : MonoBehaviour, ISnapPoseDelegate
    {
        [SerializeField] private StoryController controller;
        [SerializeField] private SnapInteractor[] allowedItems;
        [SerializeField] private Transform[] positions;
        [SerializeField, HideInInspector] private BoxCollider dropZone; // Legacy setup migration only.
        [SerializeField] private StoryInventorySlot[] slots;
        public StoryInventorySlot[] Slots => slots;
        [Tooltip("Invoked once when a released item is accepted into a free position.")]
        public UnityEvent ItemDocked = new UnityEvent();
        private readonly Dictionary<int, int> occupied = new Dictionary<int, int>();

        private readonly Dictionary<int, float> dockTimes = new Dictionary<int, float>();

        public bool IsSelectingItems => controller != null && controller.isActiveAndEnabled && controller.Phase == StoryPhase.SelectingItems;
        public bool IsSlotOccupied(int index) => occupied.ContainsValue(index);

        public int IndexOf(SnapInteractable socket)
        {
            if (slots != null)
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].Socket == socket) return i;
            return -1;
        }
        public void ReleaseSlot(int index, int id)
        {
            if (occupied.TryGetValue(id, out int current) && current == index) UnsnapElement(id);
        }
        public void ClearSlot(int index)
        {
            var ids = new List<int>();
            foreach (var pair in occupied) if (pair.Value == index) ids.Add(pair.Key);
            foreach (int id in ids) UnsnapElement(id);
        }
        public void DockSlot(int index, int id, Pose pose)
        {
            if (!occupied.ContainsKey(id) && PoseForSlot(index, id, pose, out _))
            {
                occupied.Add(id, index);
                dockTimes[id] = Time.time;
                ItemDocked.Invoke();
            }
        }
        public bool PoseForSlot(int index, int id, Pose pose, out Pose result)
        {
            result = pose;
            if (!TryGetSlot(id, pose, out int found) || found != index) return false;
            return SnapPoseForElement(id, pose, out result);
        }

        private void OnDisable() { occupied.Clear(); dockTimes.Clear(); }
        public void TrackElement(int id, Pose pose) { }
        public void MoveTrackedElement(int id, Pose pose) { }
        public void UntrackElement(int id) { occupied.Remove(id); dockTimes.Remove(id); }
        public void UnsnapElement(int id) { occupied.Remove(id); dockTimes.Remove(id); }

        public void SnapElement(int id, Pose pose)
        {
            if (!occupied.ContainsKey(id) && TryGetSlot(id, pose, out int slot))
            {
                occupied.Add(id, slot);
                dockTimes[id] = Time.time;
                ItemDocked.Invoke();
            }
        }

        public bool SnapPoseForElement(int id, Pose pose, out Pose result)
        {
            result = pose;
            if (!TryGetSlot(id, pose, out int slot)) return false;
            Vector3 position = positions[slot].position;
            if (occupied.ContainsKey(id) && dockTimes.TryGetValue(id, out float began))
            {
                var item = FindItem(id);
                var motion = item != null ? item.GetComponent<StoryItemDisplayMotion>() : null;
                if (motion != null && motion.isActiveAndEnabled)
                    position += positions[slot].up * motion.DockHeight(Time.time - began);
            }
            result = new Pose(position, positions[slot].rotation);
            return true;
        }

        internal bool TryGetSlot(int id, Pose pose, out int slot)
        {
            if (occupied.TryGetValue(id, out slot)) return true;
            slot = -1;
            if (!isActiveAndEnabled || controller == null || !controller.isActiveAndEnabled ||
                controller.Phase != StoryPhase.SelectingItems || positions == null || slots == null)
                return false;
            StoryItem item = FindItem(id);
            if (item == null || item.Definition == null) return false;
            bool available = false;
            foreach (var definition in controller.AvailableItems)
                if (definition == item.Definition) available = true;
            if (!available) return false;
            foreach (int other in occupied.Keys)
            {
                var otherItem = FindItem(other);
                if (otherItem != null && string.Equals(otherItem.Definition.DirectorName,
                    item.Definition.DirectorName, StringComparison.OrdinalIgnoreCase)) return false;
            }
            // Each slot owns its own non-overlapping volume. Never redirect to another empty slot.
            for (int i = 0; i < Mathf.Min(positions.Length, slots.Length); i++)
                if (positions[i] != null && slots[i] != null && slots[i].isActiveAndEnabled &&
                    !occupied.ContainsValue(i) && slots[i].Contains(pose.position)) { slot = i; return true; }
            return false;
        }

        private StoryItem FindItem(int id)
        {
            if (allowedItems != null)
                foreach (var interactor in allowedItems)
                    if (interactor != null && interactor.Identifier == id)
                        return interactor.GetComponentInParent<StoryItem>();
            return null;
        }
    }
}
