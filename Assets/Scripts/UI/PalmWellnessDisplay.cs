using Oculus.Interaction.Input;
using TakeMeToTheMoon.Game;
using UnityEngine;

namespace TakeMeToTheMoon.UI
{
    [DisallowMultipleComponent, DefaultExecutionOrder(210)]
    public sealed class PalmWellnessDisplay : MonoBehaviour
    {
        [SerializeField] private GameManager manager;
        [Tooltip("Left hand (existing scene reference).") ]
        [SerializeField] private MonoBehaviour handSource;
        [SerializeField] private MonoBehaviour rightHandSource;
        [SerializeField] private Transform head;
        [SerializeField] private CanvasGroup visibility;
        [SerializeField, Min(0)] private float heightAbovePalm = .22f;
        [SerializeField, Range(-1, 1)] private float showPalmUpDot = .65f;
        [SerializeField, Range(-1, 1)] private float hidePalmUpDot = .35f;
        [SerializeField, Min(0)] private float revealHoldSeconds = .15f;
        [SerializeField, Min(0)] private float followSpeed = 18;
        private int activeHand = -1;
        private readonly float[] held = new float[2];
        private readonly bool[] ready = new bool[2];
        private readonly Vector3[] palmPositions = new Vector3[2];
        private void OnEnable()
        {
            activeHand = -1;
            for (int i = 0; i < 2; i++) { held[i] = 0; ready[i] = false; }
            SetVisible(false);
        }
        private void OnDisable() => SetVisible(false);
        private void SetVisible(bool visible)
        {
            if (visibility == null) return;
            visibility.alpha = visible ? 1 : 0;
            visibility.interactable = visibility.blocksRaycasts = false;
        }
        private void LateUpdate()
        {
            // Presentation owns this gate; inventory/story selection starts later.
            if (manager == null || !manager.isActiveAndEnabled || manager.Phase == GamePhase.StartScreen)
            {
                activeHand = -1;
                for (int i = 0; i < 2; i++) { held[i] = 0; ready[i] = false; }
                SetVisible(false);
                return;
            }
            UpdateHand(0, handSource as IHand);
            UpdateHand(1, rightHandSource as IHand);
            int previousHand = activeHand;
            if (activeHand < 0 || !ready[activeHand])
                activeHand = ready[0] ? 0 : ready[1] ? 1 : -1;
            if (head == null || activeHand < 0) { SetVisible(false); return; }
            Vector3 target = palmPositions[activeHand] + Vector3.up * heightAbovePalm;
            Vector3 facing = target - head.position;
            if (facing.sqrMagnitude < .0001f) return;
            // Canvas front faces the viewer, while text stays upright.
            Quaternion rotation = Quaternion.LookRotation(facing, head.up);
            float t = previousHand != activeHand || visibility == null || visibility.alpha == 0 || followSpeed <= 0
                ? 1 : 1 - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, target, t), Quaternion.Slerp(transform.rotation, rotation, t));
            SetVisible(true);
        }
        private void UpdateHand(int index, IHand hand)
        {
            if (hand == null || !hand.IsConnected || !hand.IsHighConfidence ||
                !hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist) ||
                !hand.GetPalmPoseLocal(out Pose palm))
            {
                ready[index] = false; held[index] = 0; return;
            }
            Vector3 normal = wrist.rotation * (hand.Handedness == Handedness.Left ? Constants.LeftPalmar : Constants.RightPalmar);
            float up = Vector3.Dot(normal, Vector3.up);
            if (ready[index] && up < hidePalmUpDot) ready[index] = false;
            if (!ready[index])
            {
                held[index] = up >= showPalmUpDot ? held[index] + Time.unscaledDeltaTime : 0;
                ready[index] = up >= showPalmUpDot && held[index] >= revealHoldSeconds;
            }
            palmPositions[index] = wrist.position + wrist.rotation * palm.position;
        }
    }
}
