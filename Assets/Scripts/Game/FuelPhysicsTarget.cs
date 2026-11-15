using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    public sealed class FuelPhysicsTarget : MonoBehaviour
    {
        public FuelThrowGame game;
        public Rigidbody body;
        public Transform home;
        public bool KnockedDown { get; private set; }
        private FuelThrowItem lastProjectile;
        public void ResetTarget()
        {
            KnockedDown = false; lastProjectile = null;
            body.isKinematic = false;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = home.position; body.rotation = home.rotation;
            transform.SetPositionAndRotation(home.position, home.rotation);
            body.WakeUp();
        }
        public void BeginThrow()
        {
            lastProjectile = null;
            if (Vector3.Angle(body.rotation * Vector3.up, home.up) >= 40) KnockedDown = true;
        }
        private void OnCollisionEnter(Collision collision)
        {
            var projectile = collision.rigidbody != null ? collision.rigidbody.GetComponent<FuelThrowItem>() : null;
            if (projectile != null && projectile.InFlight) lastProjectile = projectile;
        }
        private void FixedUpdate()
        {
            if (KnockedDown || lastProjectile == null || !lastProjectile.InFlight) return;
            if (Vector3.Angle(body.rotation * Vector3.up, home.up) < 40) return;
            game.TargetFell(this, lastProjectile);
            KnockedDown = true;
        }
    }
}
