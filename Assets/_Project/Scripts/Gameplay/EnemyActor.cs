using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Stationary enemy presentation/authority adapter. Clients never create a simulation.</summary>
    public sealed class EnemyActor : MonoBehaviour
    {
        private DamageableTarget target;
        private bool bound;
        private uint revision;
        public EnemyIdentity Identity { get; private set; }
        public EnemySimulation Simulation { get; private set; }
        public EnemyLifecycle Lifecycle { get; private set; }
        public DamageableTarget Target => target;
        public int DeathEvents { get; private set; }
        public int AggroPlayer { get; private set; }
        public uint Revision => revision;
        public event Action Died;

        public void Bind(DamageableTarget damageable, EnemyIdentity identity, bool authoritative)
        {
            if (bound || damageable == null || damageable.gameObject != gameObject || identity.Encounter == 0 || identity.Spawn == 0)
                throw new InvalidOperationException("An unbound, explicitly referenced target on this actor is required.");
            bound = true; target = damageable; Identity = identity; Lifecycle = EnemyLifecycle.Alive;
            if (authoritative)
            {
                Simulation = new EnemySimulation(identity, target.InitialHealth);
                target.BindEnemy(this);
                Simulation.Changed += PresentSimulation;
                PresentSimulation();
            }
            else { target.BindEnemy(this); PresentReplica(EnemyLifecycle.Alive, target.InitialHealth, 1, 0); }
        }

        private void PresentSimulation()
        {
            Present(Simulation.Lifecycle, Simulation.Health.Current, Simulation.Revision,
                Simulation.AggroTarget?.Value ?? 0);
        }

        public bool ApplySnapshot(EnemyIdentity identity, EnemyLifecycle lifecycle, int health, int maximum, uint nextRevision, int aggro)
        {
            if (!bound || Simulation != null) throw new InvalidOperationException("Only a replica accepts enemy snapshots.");
            if (!Identity.Equals(identity)) return false;
            if (nextRevision <= revision) return false;
            if (maximum != target.InitialHealth || health < 0 || health > maximum || aggro < 0 || aggro > 2
                || lifecycle < EnemyLifecycle.Alive || lifecycle > EnemyLifecycle.Despawned
                || (lifecycle == EnemyLifecycle.Alive && health == 0) || (lifecycle == EnemyLifecycle.Dead && health != 0)
                || (lifecycle != EnemyLifecycle.Alive && aggro != 0) || lifecycle < Lifecycle
                || health > target.DisplayedHealth)
                throw new InvalidOperationException("Invalid enemy snapshot.");
            PresentReplica(lifecycle, health, nextRevision, aggro); return true;
        }

        private void PresentReplica(EnemyLifecycle lifecycle, int health, uint nextRevision, int aggro)
        { target.PresentAuthoritativeHealth(health, target.InitialHealth); Present(lifecycle, health, nextRevision, aggro); }

        private void Present(EnemyLifecycle lifecycle, int health, uint nextRevision, int aggro)
        {
            var died = Lifecycle == EnemyLifecycle.Alive && (lifecycle == EnemyLifecycle.Dead || health == 0);
            Lifecycle = lifecycle; revision = nextRevision; AggroPlayer = aggro;
            if (Simulation != null) target.PresentEnemyHealth();
            if (lifecycle != EnemyLifecycle.Alive) foreach (var shape in target.HitColliders) shape.enabled = false;
            if (lifecycle == EnemyLifecycle.Despawned) gameObject.SetActive(false);
            if (died) { DeathEvents++; Died?.Invoke(); }
        }

        public void Retire()
        {
            if (!bound) return;
            if (Simulation != null) Simulation.Despawn();
            else { Lifecycle = EnemyLifecycle.Despawned; gameObject.SetActive(false); }
        }

        private void OnDestroy()
        {
            if (Simulation == null) return;
            Simulation.Changed -= PresentSimulation;
            Simulation.Despawn();
        }
    }
}
