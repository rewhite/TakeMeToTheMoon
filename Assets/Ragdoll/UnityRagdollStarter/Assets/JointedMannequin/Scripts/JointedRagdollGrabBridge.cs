using UnityEngine;

namespace JointedMannequin
{
    /// <summary>
    /// Optional Inspector event adapter. Gesture detection and nearby-object selection are
    /// provided by your XR system; this component only forwards their start/end events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JointedRagdollGrabBridge : MonoBehaviour
    {
        [Tooltip("The mannequin to grab. Wire object-qualified select events, not global/raw hand pinches. Your XR system must already have selected this mannequin.")]
        [SerializeField] private JointedRagdoll ragdoll;
        [Tooltip("A Transform outside the mannequin, continuously updated to your tracked pinch position.")]
        [SerializeField] private Transform pinchAnchor;
        [Tooltip("Optional: choose a specific limb. Leave empty to grab the default chest.")]
        [SerializeField] private Rigidbody bodyToGrab;
        [Tooltip("Optional: an exact surface contact point. Leave empty to use the body's center of mass.")]
        [SerializeField] private Transform contactPoint;

        public void Configure(JointedRagdoll targetRagdoll, Transform trackedPinchAnchor)
        {
            EndPinch();
            ragdoll = targetRagdoll;
            pinchAnchor = trackedPinchAnchor;
        }

        /// <summary>Wire your object's selected/pinch-start event here.</summary>
        public void BeginPinch()
        {
            if (ragdoll == null || pinchAnchor == null) return;
            Rigidbody body = bodyToGrab != null ? bodyToGrab : ragdoll.DefaultGrabBody;
            if (body == null) return;
            Vector3 point = contactPoint != null ? contactPoint.position : body.worldCenterOfMass;
            ragdoll.BeginGrab(body, pinchAnchor, point);
        }

        /// <summary>Wire the same object's deselected/pinch-end event here.</summary>
        public void EndPinch()
        {
            // Only release this bridge's own attachment; a different hand may own the grab.
            if (ragdoll != null && ragdoll.GrabAnchor == pinchAnchor)
                ragdoll.EndGrab();
        }

        private void OnDisable() { EndPinch(); }
    }
}
