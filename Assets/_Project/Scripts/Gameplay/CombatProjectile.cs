using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>One authoritative swept projectile; no pooling, piercing, or input dependency.</summary>
    public sealed class CombatProjectile : MonoBehaviour
    {
        private const float Radius = 0.08f;
        private CombatResolver world;
        private bool finished;
        public AttackExecution Execution { get; private set; }
        public Vector2 Position { get; private set; }
        public float RemainingLifetime { get; private set; }

        public void Initialize(AttackExecution execution, CombatResolver resolver)
        {
            if (Execution != null) throw new InvalidOperationException("Projectile is already initialized.");
            Execution = execution;
            world = resolver;
            Position = new Vector2(execution.Origin.X, execution.Origin.Y);
            RemainingLifetime = execution.Weapon.ProjectileLifetime;
            transform.position = Position;
        }

        internal void Advance(float deltaTime)
        {
            if (finished || !world.Simulation.IsActive) return;
            var elapsed = Mathf.Min(deltaTime, RemainingLifetime);
            if (world.ResolveProjectileStep(Execution, Position, Radius, Execution.Weapon.ProjectileSpeed * elapsed, out var nextPosition))
            {
                Position = nextPosition;
                transform.position = Position;
                Finish();
                return;
            }
            Position = nextPosition;
            transform.position = Position;
            RemainingLifetime -= elapsed;
            if (RemainingLifetime <= 0) Finish();
        }

        private void Finish()
        {
            finished = true;
            world.ForgetProjectile(this);
            Destroy(gameObject);
        }
        internal void Cancel()
        {
            if (finished) return;
            gameObject.SetActive(false);
            Finish();
        }
        private void OnDestroy() { if (world != null) world.ForgetProjectile(this); }
    }
}
