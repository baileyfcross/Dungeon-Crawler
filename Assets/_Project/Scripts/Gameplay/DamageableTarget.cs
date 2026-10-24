using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Shared hit-shape and health presentation adapter for targets and enemy actors.</summary>
    public sealed class DamageableTarget : MonoBehaviour
    {
        [SerializeField, Min(1)] private int identity = 1;
        [SerializeField, Min(1)] private int initialHealth = 100;
        [SerializeField] private Collider2D[] hitColliders;
        [SerializeField] private TargetPresentation presentation;
        public HealthSimulation Health { get; private set; }
        private EnemyActor enemy;
        public EnemyActor Enemy => enemy;
        public DamageableIdentity Identity => enemy != null ? enemy.Identity.Damageable : new DamageableIdentity(identity);
        public Collider2D[] HitColliders => hitColliders;
        public int DisplayedHealth { get; private set; }
        public int InitialHealth => initialHealth;
        internal void ConfigureEnemyHealth(int maximum)
        {
            if (Health != null || enemy != null || maximum < 1) throw new InvalidOperationException("Configure enemy health before binding.");
            initialHealth = maximum;
        }

        public void ValidateEnemyAuthoring()
        {
            if (initialHealth <= 0 || presentation == null || hitColliders == null || hitColliders.Length == 0)
                throw new InvalidOperationException("An enemy requires health, presentation and hit shapes.");
            var unique = new System.Collections.Generic.HashSet<Collider2D>();
            foreach (var shape in hitColliders)
                if (shape == null || !shape.transform.IsChildOf(transform) || !shape.enabled || !shape.isTrigger || !unique.Add(shape))
                    throw new InvalidOperationException("Enemy hit shapes must be distinct enabled child triggers.");
        }

        public void Initialize()
        {
            if (enemy != null)
            {
                if (enemy.Simulation == null) throw new InvalidOperationException("A replica cannot initialize authoritative health.");
                return;
            }
            if (Health != null) throw new InvalidOperationException("Target is already initialized.");
            Health = new HealthSimulation(Identity, initialHealth);
            presentation.Present(Health);
            DisplayedHealth = Health.Current;
        }

        internal void BindEnemy(EnemyActor actor)
        {
            if (enemy != null || Health != null) throw new InvalidOperationException("Target is already bound.");
            enemy = actor; Health = actor.Simulation?.Health;
        }

        internal void PresentEnemyHealth()
        { presentation.Present(Health.Current,Health.Maximum,enemy.IsRestoring); DisplayedHealth = Health.Current; }

        public bool ReceiveDamage(DamageRequest request, out DamageResult result)
            => ReceiveDamage(request, CombatFaction.Player, out result);

        public bool ReceiveDamage(DamageRequest request, CombatFaction sourceFaction, out DamageResult result)
        {
            result = default;
            if (enemy != null)
                return enemy.Simulation != null && enemy.Simulation.TryApplyDamage(enemy.Identity, sourceFaction, request, out result);
            if (sourceFaction != CombatFaction.Player) return false;
            if (Health == null) throw new InvalidOperationException("Initialize target health before resolving damage.");
            if (!Health.TryApplyDamage(request, out result)) return false;
            if (!Health.IsAlive)
                foreach (var collider in hitColliders) collider.enabled = false;
            presentation.Present(Health);
            DisplayedHealth = Health.Current;
            return true;
        }

        // A client owns presentation only: no HealthSimulation and no damage execution.
        public void PresentAuthoritativeHealth(int current, int maximum,bool silent=false)
        {
            if (Health != null || maximum != initialHealth || current < 0 || current > maximum)
                throw new InvalidOperationException("Invalid replicated target presentation.");
            DisplayedHealth = current;
            foreach (var collider in hitColliders) collider.enabled = false;
            presentation.Present(current, maximum,silent);
        }
    }
}
