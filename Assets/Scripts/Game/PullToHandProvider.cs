using Oculus.Interaction;
using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    /// <summary>Meta ray selection pulls the hit point to the selecting hand/controller in 0.1 seconds.</summary>
    public sealed class PullToHandProvider : MonoBehaviour, IMovementProvider
    {
        public IMovement CreateMovement() => new MoveTowardsTarget(PoseTravelData.FAST);
    }
}
