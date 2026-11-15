using Oculus.Interaction;
using TakeMeToTheMoon.Story;
using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    [DefaultExecutionOrder(300)]
    public sealed class FuelThrowItem : MonoBehaviour
    {
        public FuelThrowGame game;
        public StoryItemDefinition definition;
        public Grabbable grabbable;
        public Rigidbody body;
        public Transform home;
        public RayInteractable[] rays;
        [Header("Idle display")]
        public float idleTilt = 12f;
        public float idleRotationSpeed = 18f;
        [Header("Light flick throw")]
        [Min(1)] public float throwSpeedMultiplier = 2.8f;
        [Range(.1f, 1f)] public float gravityScale = .18f;
        [Min(.1f)] public float minimumThrowSpeed = .15f;
        [Min(0)] public float minimumLaunchSpeed = 3.8f;
        [Min(0)] public float upwardAssist = 1.1f;
        private Renderer[] visuals;
        private float idleAngle;
        public bool InFlight { get; private set; }
        private bool held, released;
        private int releaseFrame;
        private float selectedAt;
        private ThrowWhenUnselected velocityThrow;
        private Vector3 releaseVelocity;
        private bool hasReleaseVelocity;
        private void OnEnable()
        {
            if (game != null) game.IgnorePanelCollisions(this);
            if (grabbable == null) return;
            grabbable.WhenPointerEventRaised += Pointer;
            velocityThrow = grabbable.VelocityThrow;
            velocityThrow.WhenThrown += CaptureRelease;
        }
        private void CaptureRelease(Vector3 velocity, Vector3 angularVelocity)
        {
            releaseVelocity = velocity;
            hasReleaseVelocity = true;
        }
        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= Pointer;
            if (velocityThrow != null) velocityThrow.WhenThrown -= CaptureRelease;
            velocityThrow = null;
            held = released = InFlight = false;
        }
        private void Pointer(PointerEvent evt)
        {
            if (InFlight) return;
            if (evt.Type == PointerEventType.Select) { hasReleaseVelocity = false; held = true; selectedAt = Time.unscaledTime; }
            if (evt.Type == PointerEventType.Unselect && held)
            { held = false; released = true; releaseFrame = Time.frameCount; }
            if (evt.Type == PointerEventType.Cancel && held) { held = false; ReturnHome(); }
        }
        private void LateUpdate()
        {
            // Use the SDK release sample, not a velocity already changed by a physics step.
            if (!held && !released && !InFlight)
            {
                idleAngle = Mathf.Repeat(idleAngle + idleRotationSpeed * Time.deltaTime, 360);
                ApplyHomePose();
            }
            if (!released || Time.frameCount <= releaseFrame) return;
            released = false;
            var sampledVelocity = hasReleaseVelocity ? releaseVelocity : body.linearVelocity;
            if (Time.unscaledTime - selectedAt < .2f || sampledVelocity.magnitude < minimumThrowSpeed || !game.BeginThrow(this)) { ReturnHome(); return; }
            Launch(AssistedVelocity(sampledVelocity));
        }
        private void Launch(Vector3 velocity)
        {
            InFlight = true;
            game.IgnorePanelCollisions(this);
            // Flight is controlled by physics only. Off-centre pivots plus wrist torque
            // otherwise make the visible prop corkscrew around its centre of mass.
            body.isKinematic = false;
            body.centerOfMass = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.linearDamping = 0;
            body.useGravity = false;
            body.linearVelocity = velocity;
            SetPickable(false);
        }
        public Vector3 AssistedVelocity(Vector3 sampledVelocity)
        {
            if (sampledVelocity.sqrMagnitude < .000001f) return Vector3.zero;
            // Preserve the user's aim, but give a short flick enough speed to travel.
            var velocity = sampledVelocity.normalized * Mathf.Clamp(
                sampledVelocity.magnitude * throwSpeedMultiplier, minimumLaunchSpeed, 12f);
            // A forward/upward flick floats in a gentle arc; deliberate downward throws stay downward.
            if (sampledVelocity.y >= -.1f && Vector3.ProjectOnPlane(sampledVelocity, Vector3.up).sqrMagnitude > .01f)
                velocity.y = Mathf.Max(velocity.y, upwardAssist);
            return Vector3.ClampMagnitude(velocity, 12f);
        }
        private void FixedUpdate()
        {
            // Mass does not change falling speed; soften gravity only during the throw.
            if (InFlight && !body.isKinematic)
                body.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
        }
        public void ApplyHomePose()
        {
            if (home == null) return;
            if (visuals == null) visuals = GetComponentsInChildren<Renderer>(true);
            transform.SetPositionAndRotation(home.position,
                home.rotation * Quaternion.Euler(0, idleAngle, 0) * Quaternion.Euler(0, 0, -idleTilt));
            // Different props have different pivots. Align their visible bottoms to the tray.
            float bottom = float.PositiveInfinity;
            foreach (var visual in visuals)
                if (visual != null && visual.enabled && !(visual is ParticleSystemRenderer) && !(visual is LineRenderer))
                    bottom = Mathf.Min(bottom, visual.bounds.min.y);
            if (!float.IsPositiveInfinity(bottom))
                transform.position += Vector3.up * (home.position.y - bottom);
            body.position = transform.position; body.rotation = transform.rotation;
        }
        public void SetPickable(bool value)
        { foreach (var ray in rays) if (ray != null) ray.enabled = value; }
        public void ReturnHome()
        {
            released = held = InFlight = false;
            body.isKinematic = false;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
            hasReleaseVelocity = false;
            ApplyHomePose();
        }
#if UNITY_EDITOR
        public void TestLaunch(Vector3 velocity)
        {
            if (!game.BeginThrow(this)) return;
            Launch(velocity);
        }
#endif
    }
}
