using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Prefab-local delayed UI. Does not track story choices or progression.</summary>
    public sealed class GameStageView : MonoBehaviour
    {
        [SerializeField] private GameObject[] revealObjects;
        public void SetRevealed(bool visible)
        {
            if (revealObjects == null) return;
            foreach (var target in revealObjects)
                if (target != null && target != gameObject && !transform.IsChildOf(target.transform))
                    target.SetActive(visible);
        }
    }
}
