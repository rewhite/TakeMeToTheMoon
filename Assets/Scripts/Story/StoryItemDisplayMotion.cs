using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Rotates the actual idle prop so ray colliders and the model remain aligned.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryItemDisplayMotion : MonoBehaviour
    {
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private SnapInteractor snapInteractor;
        [SerializeField] private Rigidbody body;
        [Tooltip("Preplaced start/return position. Does not count as story inventory.")]
        [SerializeField] private Transform home;
        [SerializeField, Min(.1f)] private float returnSpeed = 7;
        private float releasedAt = float.NegativeInfinity;
        public Transform Home => home;
        [SerializeField, Range(0, 30)] private float rightTiltDegrees = 12;
        [SerializeField, Min(0)] private float rotationDegreesPerSecond = 18;
        [SerializeField, Min(0)] private float dockBobHeight = .025f;
        [SerializeField, Min(.1f)] private float dockBobPeriod = 3;
        private readonly HashSet<int> hands = new HashSet<int>();
        private Quaternion authoredRotation;
        private float angle;
        public bool IsHeld => hands.Count > 0;

        private void Awake() => authoredRotation = transform.localRotation;
        private void Start()
        {
            if (home == null) return;
            transform.position = home.position;
            if (body != null && body.isKinematic) body.position = transform.position;
        }
        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += OnPointer;
        }
        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= OnPointer;
            hands.Clear();
        }
        private void OnPointer(PointerEvent evt)
        {
            if (snapInteractor != null && evt.Identifier == snapInteractor.Identifier) return;
            if (evt.Type == PointerEventType.Select) hands.Add(evt.Identifier);
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
            {
                if (hands.Remove(evt.Identifier) && hands.Count == 0) releasedAt = Time.time;
            }
        }
        private void Update()
        {
            if (IsHeld || snapInteractor == null || snapInteractor.State == InteractorState.Select) return;
            // Let Meta finish the release-to-socket transition before starting a return.
            if (Time.time - releasedAt < .2f) return;
            if (home != null)
            {
                Vector3 destination = home.position;
                transform.position = Vector3.Lerp(transform.position, destination, 1 - Mathf.Exp(-returnSpeed * Time.deltaTime));
                if ((transform.position - destination).sqrMagnitude < .000001f) transform.position = destination;
                if (body != null && body.isKinematic) body.position = transform.position;
            }
            angle = Mathf.Repeat(angle + rotationDegreesPerSecond * Time.deltaTime, 360);
            Quaternion basis = home != null
                ? (transform.parent != null ? Quaternion.Inverse(transform.parent.rotation) * home.rotation : home.rotation)
                : authoredRotation;
            Quaternion target = Quaternion.Euler(0, 0, -rightTiltDegrees) * Quaternion.Euler(0, angle, 0) * basis;
            transform.localRotation = Quaternion.Slerp(transform.localRotation, target, 1 - Mathf.Exp(-5 * Time.deltaTime));
            if (body != null && body.isKinematic) body.rotation = transform.rotation;
        }
        public void ResetToHome()
        {
            // Disabling the prop cancels Meta grab/snap selection before teleporting it.
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(false);
            hands.Clear();
            angle = 0;
            releasedAt = float.NegativeInfinity;
            if (home != null) transform.SetPositionAndRotation(home.position, home.rotation);
            if (body != null)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.position = transform.position;
                body.rotation = transform.rotation;
            }
            gameObject.SetActive(wasActive);
        }
        // Start and finish at zero height with zero velocity; never bob below the socket plane.
        public float DockHeight(float elapsed) => dockBobHeight * .5f *
            (1 - Mathf.Cos(Mathf.Max(0, elapsed) * Mathf.PI * 2 / Mathf.Max(.1f, dockBobPeriod)));
    }
}
