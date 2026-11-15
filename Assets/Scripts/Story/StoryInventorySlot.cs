using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SnapInteractable), typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class StoryInventorySlot : MonoBehaviour, ISnapPoseDelegate
    {
        [SerializeField] private StoryInventoryTray tray;
        [SerializeField] private int index;
        [SerializeField] private BoxCollider zone;
        public SnapInteractable Socket => GetComponent<SnapInteractable>();
        private void Awake()
        {
            Socket.MaxSelectingInteractors = 1;
            Socket.InjectOptionalSnapPoseDelegate(this);
        }
        public bool Contains(Vector3 position)
        {
            if (zone == null || !zone.enabled) return false;
            Vector3 local = zone.transform.InverseTransformPoint(position) - zone.center;
            Vector3 half = zone.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        public void TrackElement(int id, Pose pose) { }
        public void MoveTrackedElement(int id, Pose pose) { }
        public void UntrackElement(int id) => tray.ReleaseSlot(index, id);
        public void UnsnapElement(int id) => tray.ReleaseSlot(index, id);
        public void SnapElement(int id, Pose pose) => tray.DockSlot(index, id, pose);
        public bool SnapPoseForElement(int id, Pose pose, out Pose result) => tray.PoseForSlot(index, id, pose, out result);
        private void OnDisable() { if (tray != null) tray.ClearSlot(index); }
    }
}
