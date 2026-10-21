using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Authoritative Unity execution: query the world, spawn projectiles, apply identified damage.</summary>
    public sealed class CombatResolver : MonoBehaviour
    {
        [SerializeField] private CombatProjectile projectilePrefab;
        [SerializeField] private CombatPresentation presentation;
        [SerializeField] private DamageableTarget[] targets;
        [SerializeField] private Collider2D[] ownerColliders;
        private readonly Dictionary<Collider2D, DamageableTarget> targetByCollider = new Dictionary<Collider2D, DamageableTarget>();
        private readonly HashSet<Collider2D> ignoredOwnerColliders = new HashSet<Collider2D>();
        private readonly List<CombatProjectile> projectiles = new List<CombatProjectile>();
        public CombatSimulation Simulation { get; private set; }
        public IReadOnlyList<DamageableTarget> Targets => targets;
        public IReadOnlyList<CombatProjectile> Projectiles => projectiles;
        public int RegisteredColliderCount => targetByCollider.Count;
        public CombatPresentation Presentation => presentation;
        public CombatProjectile ProjectilePrefab => projectilePrefab;

        public void IgnorePlayerColliders(IEnumerable<Collider2D> colliders)
        { foreach (var collider in colliders) ignoredOwnerColliders.Add(collider); }

        public void Bind(PlayerSimulation player, WeaponConfiguration startingWeapon)
        {
            if (Simulation != null) throw new InvalidOperationException("Combat is already bound.");
            Simulation = new CombatSimulation(player, startingWeapon);
            foreach (var collider in ownerColliders) ignoredOwnerColliders.Add(collider);
            RegisterRoomTargets(targets);
        }

        public void RegisterRoomTargets(IReadOnlyList<DamageableTarget> roomTargets, bool initializeHealth = true)
        {
            if (roomTargets == null || targetByCollider.Count != 0) throw new InvalidOperationException("Clear old room registrations before binding a new room.");
            var copy = new DamageableTarget[roomTargets.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = roomTargets[i];
            targets = copy;
            var identities = new HashSet<DamageableIdentity>();
            foreach (var target in targets)
            {
                if (!identities.Add(target.Identity)) throw new InvalidOperationException("Duplicate target identity.");
                if (initializeHealth) target.Initialize();
                else if (target.Health == null) throw new InvalidOperationException("Shared target health must be initialized by the room authority first.");
                foreach (var collider in target.HitColliders) targetByCollider.Add(collider, target);
            }
        }

        public void ClearRoom()
        {
            foreach (var projectile in projectiles.ToArray()) if (projectile != null) projectile.Cancel();
            projectiles.Clear();
            targetByCollider.Clear();
            targets = Array.Empty<DamageableTarget>();
            if (presentation != null) presentation.ClearRoom();
        }

        public bool RequestAttack(AttackIntent intent)
        {
            if (Simulation == null || !Simulation.TryAttack(intent, out var execution)) return false;
            if (execution.Weapon.Kind == AttackKind.Melee) ResolveMelee(execution);
            else
            {
                var projectile = Instantiate(projectilePrefab, transform);
                projectiles.Add(projectile);
                projectile.Initialize(execution, this);
            }
            presentation.Present(execution);
            return true;
        }

        private void ApplyDamage(AttackExecution execution, DamageableTarget target)
        {
            target.ReceiveDamage(new DamageRequest(execution.Attacker, execution.Sequence, target.Identity, execution.Weapon.Damage), out _);
        }

        private void ResolveMelee(AttackExecution execution)
        {
            var origin = new Vector2(execution.Origin.X, execution.Origin.Y);
            var hitTargets = new HashSet<DamageableIdentity>();
            foreach (var collider in Physics2D.OverlapCircleAll(origin, execution.Weapon.MeleeRange))
            {
                if (ignoredOwnerColliders.Contains(collider) || !targetByCollider.TryGetValue(collider, out var target) || !target.Health.IsAlive) continue;
                var position = target.transform.position;
                if (!MeleeRules.Contains(execution, new CoreVector(position.x, position.y)) || !hitTargets.Add(target.Identity)) continue;
                ApplyDamage(execution, target);
            }
        }

        public bool ResolveProjectileStep(AttackExecution execution, Vector2 position, float radius, float distance, out Vector2 impactPosition)
        {
            var direction = new Vector2(execution.AimDirection.X, execution.AimDirection.Y);
            RaycastHit2D? nearest = null;
            foreach (var hit in Physics2D.CircleCastAll(position, radius, direction, distance))
            {
                if (ignoredOwnerColliders.Contains(hit.collider)) continue;
                var isTarget = targetByCollider.TryGetValue(hit.collider, out var target) && target.Health.IsAlive;
                if (!isTarget && hit.collider.isTrigger) continue;
                if (!nearest.HasValue || hit.distance < nearest.Value.distance) nearest = hit;
            }
            impactPosition = position + direction * distance;
            if (!nearest.HasValue) return false;
            impactPosition = nearest.Value.centroid;
            if (targetByCollider.TryGetValue(nearest.Value.collider, out var damageable) && damageable.Health.IsAlive)
                ApplyDamage(execution, damageable);
            // Solid walls also terminate a shot; unknown triggers do not.
            return true;
        }

        private void FixedUpdate()
        {
            if (Simulation == null || !Simulation.IsActive) return;
            Simulation.AdvanceTime(Time.fixedDeltaTime);
            for (var i = projectiles.Count - 1; i >= 0; i--) projectiles[i].Advance(Time.fixedDeltaTime);
        }

        internal void ForgetProjectile(CombatProjectile projectile) => projectiles.Remove(projectile);
        private void OnDestroy() => ClearRoom();
    }
}
