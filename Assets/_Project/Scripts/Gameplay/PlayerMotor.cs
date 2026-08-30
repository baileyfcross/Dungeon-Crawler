using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D body;
        private PlayerSimulation simulation;
        public Rigidbody2D Body => body;
        public void Bind(PlayerSimulation playerSimulation) => simulation = playerSimulation;

        private void FixedUpdate()
        {
            if (simulation == null) return;
            var velocity = simulation.DesiredVelocity;
            body.linearVelocity = new Vector2(velocity.X, velocity.Y);
        }

        public void CaptureState()
        {
            if (simulation == null) return;
            var position = body.position;
            var velocity = body.linearVelocity;
            simulation.RecordPhysics(simulation.State.Player, new CoreVector(position.x, position.y), new CoreVector(velocity.x, velocity.y));
        }
    }
}
