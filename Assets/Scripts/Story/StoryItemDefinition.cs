using UnityEngine;

namespace TakeMeToTheMoon.Story
{
    [CreateAssetMenu(menuName = "Story/Item", fileName = "Story Item")]
    public sealed class StoryItemDefinition : ScriptableObject
    {
        [Tooltip("Stable English inventory name sent to the Director; matches uses_item in WebTest.")]
        [SerializeField] private string directorName;
        [SerializeField] private string displayName;
        [TextArea, SerializeField] private string description;
        [SerializeField] private Sprite icon;

        public string DirectorName => directorName;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? directorName : displayName;
        public string Description => description;
        public Sprite Icon => icon;
    }
}
