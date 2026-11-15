using Oculus.Interaction;
using UnityEngine;

namespace JointedMannequin
{
    /// <summary>All body surfaces share one Meta Grabbable; only the head is pulled.</summary>
    public sealed class JointedRagdollRayGrab : MonoBehaviour, ITransformer, IMovementProvider
    {
        [SerializeField] private JointedRagdoll ragdoll;
        [SerializeField] private Rigidbody head;
        private IGrabbable grabbable;
        private Transform anchor;
        private Vector3 initialOffset;
        private float grabStarted;

        public void Configure(JointedRagdoll target, Rigidbody headBody)
        {
            ragdoll = target;
            head = headBody;
        }

        public void Initialize(IGrabbable target) => grabbable = target;
        public IMovement CreateMovement() => new MoveTowardsTarget(PoseTravelData.FAST);

        public void BeginTransform()
        {
            EndTransform();
            if (ragdoll == null || head == null || grabbable.GrabPoints.Count == 0) return;
            // External anchor: never move the ragdoll root or parent physics bodies to the hand.
            anchor = new GameObject(name + "_RayHeadAnchor").transform;
            anchor.SetPositionAndRotation(head.position, Quaternion.identity);
            initialOffset = head.position - grabbable.GrabPoints[0].position;
            grabStarted = Time.time;
            if (!ragdoll.BeginGrab(head, anchor, head.position, true)) EndTransform();
        }

        public void UpdateTransform()
        {
            if (anchor == null || grabbable.GrabPoints.Count == 0) return;
            if (!ragdoll.IsGrabbed) { EndTransform(); return; }
            Pose pose = grabbable.GrabPoints[0];
            // Fade out the hit-to-head offset so grabbing a foot cannot yank the head down first.
            float blend = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((Time.time - grabStarted) / 0.3f));
            anchor.SetPositionAndRotation(pose.position + initialOffset * blend, pose.rotation);
        }

        public void EndTransform()
        {
            if (anchor == null) return;
            if (ragdoll != null && ragdoll.GrabAnchor == anchor) ragdoll.EndGrab();
            Destroy(anchor.gameObject);
            anchor = null;
        }

        private void OnDisable() => EndTransform();
        private void OnDestroy() => EndTransform();
    }
}
