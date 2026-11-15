using UnityEngine;

namespace JointedMannequin
{
    /// <summary>
    /// Optional desktop demonstration. The physics controller itself does not depend on an
    /// input package. Mouse input is compiled only when Unity's legacy input backend is enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JointedRagdollMouseDemo : MonoBehaviour
    {
        [SerializeField] private JointedRagdoll ragdoll;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private LayerMask grabLayers = ~0;
        [Min(0.1f)] [SerializeField] private float maximumRayDistance = 30f;
        [SerializeField] private bool showHelp = true;

        private Transform mouseAnchor;
        private float grabDistance;

        public void Configure(JointedRagdoll targetRagdoll, Camera targetCamera)
        {
            ragdoll = targetRagdoll;
            interactionCamera = targetCamera;
        }

        private void Awake()
        {
            if (ragdoll == null) ragdoll = GetComponent<JointedRagdoll>();
            if (interactionCamera == null) interactionCamera = Camera.main;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (ragdoll == null || interactionCamera == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                ReleaseMouse();
                ragdoll.ResetPose();
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ReleaseMouse();
                ragdoll.SetDynamic();
            }

            if (Input.GetMouseButtonDown(0))
            {
                // Leave the on-screen controls available without picking through them.
                Vector2 screenPoint = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (!showHelp || !new Rect(12, 12, 460, 126).Contains(screenPoint))
                    TryBeginMouseGrab();
            }
            if (Input.GetMouseButtonUp(0)) ReleaseMouse();

            if (mouseAnchor != null)
            {
                if (!ragdoll.IsGrabbed)
                {
                    ReleaseMouse();
                    return;
                }
                grabDistance = Mathf.Clamp(grabDistance + Input.mouseScrollDelta.y * 0.12f,
                    0.2f, maximumRayDistance);
                Ray ray = interactionCamera.ScreenPointToRay(Input.mousePosition);
                mouseAnchor.position = ray.GetPoint(grabDistance);
            }
#endif
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private void TryBeginMouseGrab()
        {
            Ray ray = interactionCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, maximumRayDistance, grabLayers, QueryTriggerInteraction.Ignore))
                return;
            if (!ragdoll.ContainsBody(hit.rigidbody)) return;

            ReleaseMouse();
            mouseAnchor = new GameObject("Mannequin_MousePinchTarget").transform;
            mouseAnchor.position = hit.point;
            mouseAnchor.rotation = interactionCamera.transform.rotation;
            grabDistance = hit.distance;
            if (!ragdoll.BeginGrab(hit.rigidbody, mouseAnchor, hit.point)) ReleaseMouse();
        }
#endif

        private void ReleaseMouse()
        {
            if (mouseAnchor == null) return;
            if (ragdoll != null) ragdoll.EndGrab();
            Destroy(mouseAnchor.gameObject);
            mouseAnchor = null;
        }

        private void OnGUI()
        {
            if (!showHelp) return;
            GUILayout.BeginArea(new Rect(12, 12, 460, 126), GUI.skin.box);
            GUILayout.Label("JOINTED MANNEQUIN — PHYSICS DEMO");
#if ENABLE_LEGACY_INPUT_MANAGER
            GUILayout.Label("Drag any body part. Scroll to move it nearer/farther.");
            GUILayout.Label("Release to drop. R = stand again. Space = go limp.");
#else
            GUILayout.Label("Mouse demo: enable Active Input Handling = Both.");
            GUILayout.Label("XR: call BeginGrab(pinchTransform) and EndGrab().");
#endif
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset standing pose") && ragdoll != null)
            {
                ReleaseMouse();
                ragdoll.ResetPose();
            }
            if (GUILayout.Button("Let go limp") && ragdoll != null)
            {
                ReleaseMouse();
                ragdoll.EndGrab();
                ragdoll.SetDynamic();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ReleaseMouse();
        }
        private void OnDisable() { ReleaseMouse(); }
        private void OnDestroy() { ReleaseMouse(); }
    }
}
