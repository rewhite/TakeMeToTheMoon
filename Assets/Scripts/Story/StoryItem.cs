using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    /// <summary>Place on the grabbed object's root, above its Meta SnapInteractor.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryItem : MonoBehaviour
    {
        [SerializeField] private StoryItemDefinition definition;
        public StoryItemDefinition Definition => definition;
    }
}
