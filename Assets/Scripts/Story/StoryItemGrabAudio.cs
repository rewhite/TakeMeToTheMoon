using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Ignore the snap interactor's own Select event: docking is not another hand grab.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryItemGrabAudio : MonoBehaviour
    {
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private SnapInteractor snapInteractor;
        [SerializeField] private AudioTrigger grabAudio;
        private readonly HashSet<int> hands = new HashSet<int>();

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
            if (evt.Type == PointerEventType.Select)
            {
                if (hands.Add(evt.Identifier) && hands.Count == 1 && grabAudio != null)
                    grabAudio.PlayAudio();
            }
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
                hands.Remove(evt.Identifier);
        }
    }
}
