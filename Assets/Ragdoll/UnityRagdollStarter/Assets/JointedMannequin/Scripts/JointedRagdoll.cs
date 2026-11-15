using System;
using System.Collections.Generic;
using UnityEngine;

namespace JointedMannequin
{
    /// <summary>
    /// Holds a segmented mannequin in its authored pose, then lets physics take over.
    /// Connect your XR select/pinch events to BeginGrab and EndGrab. No XR package is required.
    /// Keep this root stationary while its Rigidbody children are simulating.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JointedRagdoll : MonoBehaviour
    {
        [Header("Body")]
        [SerializeField] private Rigidbody[] bodies = Array.Empty<Rigidbody>();
        [SerializeField] private Rigidbody defaultGrabBody;
        [SerializeField] private bool startFrozen = true;
        [Tooltip("Disables collisions between this mannequin's own parts for a stable toy-style ragdoll. Parts still collide with the environment.")]
        [SerializeField] private bool ignoreSelfCollisions = true;

        [Header("Grab")]
        [Tooltip("Caps how fast the invisible physics handle follows the pinch point, limiting impulses from tracking jumps.")]
        [Min(0.1f)]
        [SerializeField] private float maximumHandleSpeed = 6f;
        [Tooltip("Small positional error allowed before the hand attachment is projected back into place.")]
        [Min(0.001f)]
        [SerializeField] private float grabProjectionDistance = 0.03f;

        private Vector3[] standingPositions;
        private Quaternion[] standingRotations;
        private Animator[] poseAnimators = Array.Empty<Animator>();
        private bool[] animatorWasEnabled = Array.Empty<bool>();
        private Transform trackedHand;
        private Vector3 pointInHandSpace;
        private Rigidbody physicsHandle;
        private ConfigurableJoint grabJoint;
        private bool isDynamic;
        private Rigidbody heldBody;
        private Vector3 heldVelocity;
        private Vector3 smoothingVelocity;
        private bool heldCollisionState;

        public Rigidbody[] Bodies { get { return bodies; } }
        public Rigidbody DefaultGrabBody { get { return defaultGrabBody; } }
        public bool IsGrabbed { get { return (physicsHandle != null && grabJoint != null) || heldBody != null; } }
        public bool IsDynamic { get { return isDynamic; } }
        public Transform GrabAnchor { get { return trackedHand; } }

        /// <summary>Called by the editor builder after all physics parts have been created.</summary>
        public void Configure(Rigidbody[] parts, Rigidbody grabBody)
        {
            bodies = parts ?? Array.Empty<Rigidbody>();
            defaultGrabBody = grabBody;
            EnsureBodies();
            CaptureStandingPose();
            if (Application.isPlaying)
            {
                ApplySelfCollisionPolicy();
                if (startFrozen) ResetPose();
                else SetDynamic();
            }
        }

        private void Awake()
        {
            EnsureBodies();
            CaptureStandingPose();
            TuneForScale();
            ApplySelfCollisionPolicy();
            if (startFrozen) ResetPose();
            else SetDynamic();
        }

        private void TuneForScale()
        {
            float scale = Mathf.Max(0.01f, Mathf.Min(Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));
            foreach (Rigidbody body in bodies)
            {
                if (body == null) continue;
                body.solverIterations = 20;
                body.solverVelocityIterations = 8;
                body.maxDepenetrationVelocity = 2f;
#if UNITY_6000_0_OR_NEWER
                body.angularDamping = 1.5f;
#else
                body.angularDrag = 1.5f;
#endif
                foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
                    collider.contactOffset = Mathf.Clamp(0.005f * scale, 0.0005f, 0.005f);
                foreach (CharacterJoint joint in body.GetComponents<CharacterJoint>())
                {
                    joint.enablePreprocessing = false;
                    joint.projectionDistance = Mathf.Max(0.0005f, 0.01f * scale);
                }
            }
        }

        private void OnEnable()
        {
            // Unity can discard IgnoreCollision pairs when colliders are deactivated.
            // Reapply when a pooled or temporarily hidden mannequin becomes active again.
            if (Application.isPlaying && bodies != null) ApplySelfCollisionPolicy();
        }

        private void EnsureBodies()
        {
            if (bodies == null || bodies.Length == 0)
                bodies = GetComponentsInChildren<Rigidbody>(true);

            if (defaultGrabBody == null || !ContainsBody(defaultGrabBody))
            {
                defaultGrabBody = null;
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] == null) continue;
                    if (defaultGrabBody == null) defaultGrabBody = bodies[i];
                    if (bodies[i].name.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        defaultGrabBody = bodies[i];
                        break;
                    }
                }
            }
        }

        private void CaptureStandingPose()
        {
            standingPositions = new Vector3[bodies.Length];
            standingRotations = new Quaternion[bodies.Length];
            Quaternion inverseRootRotation = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                standingPositions[i] = transform.InverseTransformPoint(bodies[i].position);
                standingRotations[i] = inverseRootRotation * bodies[i].rotation;
            }

            poseAnimators = GetComponentsInChildren<Animator>(true);
            animatorWasEnabled = new bool[poseAnimators.Length];
            for (int i = 0; i < poseAnimators.Length; i++)
                animatorWasEnabled[i] = poseAnimators[i].enabled;
        }

        private void ApplySelfCollisionPolicy()
        {
            if (!ignoreSelfCollisions) return;
            var colliders = new List<Collider>();
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                Collider[] candidates = bodies[i].GetComponentsInChildren<Collider>(true);
                for (int j = 0; j < candidates.Length; j++)
                {
                    if (candidates[j].attachedRigidbody == bodies[i] && !colliders.Contains(candidates[j]))
                        colliders.Add(candidates[j]);
                }
            }

            // IgnoreCollision state is runtime state; this deliberately runs after prefab instantiation.
            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        public bool ContainsBody(Rigidbody body)
        {
            return body != null && bodies != null && Array.IndexOf(bodies, body) >= 0;
        }

        /// <summary>
        /// Grab the default chest body. The existing offset from the supplied hand to the chest
        /// is preserved, so the mannequin does not teleport when this is called.
        /// </summary>
        public bool BeginGrab(Transform handAnchor)
        {
            if (defaultGrabBody == null) EnsureBodies();
            if (defaultGrabBody == null) return false;
            return BeginGrab(defaultGrabBody, handAnchor, defaultGrabBody.worldCenterOfMass);
        }

        /// <summary>
        /// Grab a specific body part at a world-space contact point. Use a continuously updated
        /// pinch-pose Transform for handAnchor. Keep that Transform outside this ragdoll hierarchy.
        /// </summary>
        public bool BeginGrab(Rigidbody body, Transform handAnchor, Vector3 worldGrabPoint, bool holdBodyKinematic = false)
        {
            if (!Application.isPlaying || !ContainsBody(body) || handAnchor == null
                || !handAnchor.gameObject.activeInHierarchy || handAnchor.IsChildOf(transform))
                return false;

            // This starter supports one grab at a time. Repeated events from the current
            // hand are harmless, and another hand cannot steal or reset the attachment.
            if (IsGrabbed) return trackedHand == handAnchor;

            EndGrab();
            SetDynamic();
            trackedHand = handAnchor;
            pointInHandSpace = handAnchor.InverseTransformPoint(worldGrabPoint);
            if (holdBodyKinematic)
            {
                heldBody = body;
                heldVelocity = smoothingVelocity = Vector3.zero;
                heldCollisionState = body.detectCollisions;
                body.isKinematic = true;
                // The held head must not fight the selecting hand's physical colliders.
                body.detectCollisions = false;
                return true;
            }

            var handleObject = new GameObject(name + "_PhysicsGrabHandle");
            handleObject.transform.SetPositionAndRotation(worldGrabPoint, handAnchor.rotation);
            physicsHandle = handleObject.AddComponent<Rigidbody>();
            physicsHandle.isKinematic = true;
            physicsHandle.useGravity = false;
            physicsHandle.interpolation = RigidbodyInterpolation.Interpolate;

            grabJoint = body.gameObject.AddComponent<ConfigurableJoint>();
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.connectedBody = physicsHandle;
            grabJoint.anchor = body.transform.InverseTransformPoint(worldGrabPoint);
            grabJoint.connectedAnchor = Vector3.zero;
            grabJoint.xMotion = ConfigurableJointMotion.Locked;
            grabJoint.yMotion = ConfigurableJointMotion.Locked;
            grabJoint.zMotion = ConfigurableJointMotion.Locked;
            // Hold one point, leaving the character free to swing around that point.
            grabJoint.angularXMotion = ConfigurableJointMotion.Free;
            grabJoint.angularYMotion = ConfigurableJointMotion.Free;
            grabJoint.angularZMotion = ConfigurableJointMotion.Free;
            grabJoint.enableCollision = false;
            grabJoint.enablePreprocessing = false;
            grabJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            grabJoint.projectionDistance = grabProjectionDistance;
            grabJoint.projectionAngle = 15f;
            body.WakeUp();
            return true;
        }

        private void FixedUpdate()
        {
            if (heldBody != null)
            {
                if (trackedHand == null || !trackedHand.gameObject.activeInHierarchy)
                {
                    EndGrab();
                    return;
                }
                Vector3 target = trackedHand.TransformPoint(pointInHandSpace);
                Vector3 next = Vector3.SmoothDamp(heldBody.position, target, ref smoothingVelocity,
                    0.08f, Mathf.Min(maximumHandleSpeed, 2f), Time.fixedDeltaTime);
                heldVelocity = (next - heldBody.position) / Time.fixedDeltaTime;
                heldBody.MovePosition(next);
                return;
            }
            if (physicsHandle == null) return;
            if (trackedHand == null || !trackedHand.gameObject.activeInHierarchy || grabJoint == null)
            {
                EndGrab();
                return;
            }

            Vector3 targetPoint = trackedHand.TransformPoint(pointInHandSpace);
            Vector3 nextPoint = Vector3.MoveTowards(physicsHandle.position, targetPoint,
                maximumHandleSpeed * Time.fixedDeltaTime);
            physicsHandle.MovePosition(nextPoint);
            physicsHandle.MoveRotation(trackedHand.rotation);
        }

        /// <summary>Release the temporary hand connection; retain each body's simulated velocity.</summary>
        public void EndGrab()
        {
            if (heldBody != null)
            {
                heldBody.isKinematic = false;
                heldBody.detectCollisions = heldCollisionState;
#if UNITY_6000_0_OR_NEWER
                heldBody.linearVelocity = heldVelocity;
#else
                heldBody.velocity = heldVelocity;
#endif
                heldBody.WakeUp();
                heldBody = null;
            }
            if (grabJoint != null)
            {
                // Destroy is deferred. Free every axis immediately so this attachment cannot
                // exert forces if another physics step occurs before the component is removed.
                grabJoint.xMotion = ConfigurableJointMotion.Free;
                grabJoint.yMotion = ConfigurableJointMotion.Free;
                grabJoint.zMotion = ConfigurableJointMotion.Free;
                grabJoint.angularXMotion = ConfigurableJointMotion.Free;
                grabJoint.angularYMotion = ConfigurableJointMotion.Free;
                grabJoint.angularZMotion = ConfigurableJointMotion.Free;
                grabJoint.connectedBody = null;
                Destroy(grabJoint);
                grabJoint = null;
            }
            if (physicsHandle != null)
            {
                Destroy(physicsHandle.gameObject);
                physicsHandle = null;
            }
            trackedHand = null;
        }

        /// <summary>Enable ragdoll physics without grabbing it. The mannequin will fall.</summary>
        public void SetDynamic()
        {
            if (isDynamic) return;
            for (int i = 0; i < poseAnimators.Length; i++)
                if (poseAnimators[i] != null) poseAnimators[i].enabled = false;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null) continue;
                body.isKinematic = false;
                body.useGravity = true;
                ClearVelocity(body);
                body.WakeUp();
            }
            isDynamic = true;
        }

        /// <summary>Return to the authored standing pose relative to this root, then freeze.</summary>
        public void ResetPose()
        {
            EndGrab();
            if (standingPositions == null || standingPositions.Length != bodies.Length)
                CaptureStandingPose();

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null) continue;
                if (!body.isKinematic) ClearVelocity(body);
                body.isKinematic = true;
                body.position = transform.TransformPoint(standingPositions[i]);
                body.rotation = transform.rotation * standingRotations[i];
            }
            isDynamic = false;

            for (int i = 0; i < poseAnimators.Length; i++)
                if (poseAnimators[i] != null) poseAnimators[i].enabled = animatorWasEnabled[i];
            Physics.SyncTransforms();
        }

        private static void ClearVelocity(Rigidbody body)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = Vector3.zero;
#else
            body.velocity = Vector3.zero;
#endif
            body.angularVelocity = Vector3.zero;
        }

        private void OnDisable() { EndGrab(); }
        private void OnDestroy() { EndGrab(); }
    }
}
