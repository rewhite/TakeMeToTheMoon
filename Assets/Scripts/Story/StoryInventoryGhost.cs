using System;
using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Preplaced visual-only meshes preview the exact snap anchor pose; no runtime cloning.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryInventoryGhost : MonoBehaviour
    {
        [Serializable]
        public sealed class Preview
        {
            public SnapInteractor item;
            public Transform visual;
        }
        [SerializeField] private StoryInventoryTray tray;
        [SerializeField] private Preview[] previews;
        public SnapInteractor PreviewedItem { get; private set; }
        public Transform VisibleGhost { get; private set; }
        public int PreviewedSlot { get; private set; } = -1;

        private void OnEnable() => HideAll();
        private void OnDisable() => HideAll();

        private void LateUpdate()
        {
            SnapInteractor candidate = null;
            Preview selected = null;
            Pose destination = Pose.identity;
            float nearest = float.PositiveInfinity;
            if (tray != null && tray.isActiveAndEnabled && previews != null)
            {
                foreach (var preview in previews)
                {
                    var item = preview.item;
                    if (item == null || preview.visual == null || !item.isActiveAndEnabled ||
                        !(item.PointableElement is PointableElement pointable) || pointable.SelectingPointsCount == 0 ||
                        item.State != InteractorState.Hover || tray.IndexOf(item.Interactable) < 0) continue;
                    if (!item.Interactable.PoseForInteractor(item, out Pose target)) continue;
                    float distance = (item.SnapPose.position - target.position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    candidate = item;
                    selected = preview;
                    destination = target;
                }
            }
            // Show the exact socket being hovered, with one clear preview for simultaneous hands.
            if (selected != null)
            {
                var anchor = candidate.SnapPose;
                Quaternion delta = destination.rotation * Quaternion.Inverse(anchor.rotation);
                selected.visual.SetPositionAndRotation(
                    destination.position + delta * (candidate.transform.position - anchor.position),
                    delta * candidate.transform.rotation);
                Vector3 parentScale = selected.visual.parent != null ? selected.visual.parent.lossyScale : Vector3.one;
                Vector3 scale = candidate.transform.lossyScale;
                selected.visual.localScale = new Vector3(SafeDivide(scale.x, parentScale.x),
                    SafeDivide(scale.y, parentScale.y), SafeDivide(scale.z, parentScale.z));
            }
            if (previews != null)
                foreach (var preview in previews)
                    if (preview.visual != null && preview.visual.gameObject.activeSelf != (preview == selected))
                        preview.visual.gameObject.SetActive(preview == selected);
            PreviewedSlot = candidate != null ? tray.IndexOf(candidate.Interactable) : -1;
            PreviewedItem = candidate;
            VisibleGhost = selected?.visual;
        }
        private static float SafeDivide(float value, float divisor) => Mathf.Abs(divisor) > .00001f ? value / divisor : value;
        private void HideAll()
        {
            if (previews != null)
                foreach (var preview in previews)
                    if (preview.visual != null) preview.visual.gameObject.SetActive(false);
            PreviewedSlot = -1;
            PreviewedItem = null;
            VisibleGhost = null;
        }
    }
}
