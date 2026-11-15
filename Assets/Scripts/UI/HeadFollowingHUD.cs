using UnityEngine;

namespace TakeMeToTheMoon.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class HeadFollowingHUD : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [Tooltip("Meters from the head position, in the simulated torso's upright frame: right, up, forward.")]
        [SerializeField] private Vector3 offset = new Vector3(.38f, -.22f, .9f);
        [SerializeField, Min(0)] private float followSpeed = 12;
        [Tooltip("Head yaw within this angle does not turn the simulated torso.")]
        [SerializeField, Range(0, 90)] private float yawDeadZone = 35;
        [SerializeField, Min(0)] private float torsoTurnSpeed = 2;
        private bool placed;
        private float torsoYaw;
        public Transform Head => head;
        public Vector3 Offset => offset;
        private void OnEnable() => placed = false;
        private void LateUpdate()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;
            // Project forward onto the floor: looking down at the HUD must not move it away.
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            float headYaw = forward.sqrMagnitude > .001f
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : torsoYaw;
            if (!placed) torsoYaw = headYaw;
            float difference = Mathf.DeltaAngle(torsoYaw, headYaw);
            float excess = Mathf.Abs(difference) - yawDeadZone;
            if (excess > 0)
            {
                float turn = torsoTurnSpeed <= 0 ? 1 : 1 - Mathf.Exp(-torsoTurnSpeed * Time.unscaledDeltaTime);
                torsoYaw += Mathf.Sign(difference) * excess * turn;
            }
            Quaternion torsoRotation = Quaternion.Euler(0, torsoYaw, 0);
            Vector3 destination = head.position + torsoRotation * offset;
            bool snap = !placed || Vector3.Distance(transform.position, destination) > .5f;
            float t = snap || followSpeed <= 0 ? 1 : 1 - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, destination, t),
                Quaternion.Slerp(transform.rotation, torsoRotation, t));
            placed = true;
        }
        [ContextMenu("Preview HUD Position")]
        public void PreviewPosition()
        {
            if (head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            Quaternion rotation = forward.sqrMagnitude > .001f
                ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            transform.SetPositionAndRotation(head.position + rotation * offset, rotation);
        }
    }
}
