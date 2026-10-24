using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Enemy presentation/authority adapter. Clients never create a simulation or behaviour.</summary>
    public sealed class EnemyActor : MonoBehaviour
    {
        private DamageableTarget target;
        private bool bound, replicaHasSnapshot;
        private uint revision;
        public EnemyIdentity Identity { get; private set; }
        public EnemySimulation Simulation { get; private set; }
        public IEnemyBehaviour Behaviour { get; private set; }
        public EnemyCombatConfiguration Configuration { get; private set; }
        public RangedEnemyConfiguration RangedConfiguration { get; private set; }
        public EnemyArchetype Archetype { get; private set; } = EnemyArchetype.Melee;
        public void ConfigureRanged(RangedEnemyDefinition definition,RunDifficulty difficulty)
        {
            if(!bound || Configuration!=null)throw new InvalidOperationException("Enemy already configured.");
            Archetype=EnemyArchetype.Ranged;RangedConfiguration=definition.CreateConfiguration(difficulty);Configuration=RangedConfiguration.Combat;
            if(Simulation!=null)Behaviour=new RangedEnemyBehaviour(Simulation,RangedConfiguration,new System.Numerics.Vector2(transform.position.x,transform.position.y));
        }
        public EnemyBehaviourState BehaviourState { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.right;
        public float WindupProgress { get; private set; }
        public long StrikeSequence { get; private set; }
        public EnemyLifecycle Lifecycle { get; private set; }
        public DamageableTarget Target => target;
        public int DeathEvents { get; private set; }
        public int AggroPlayer { get; private set; }
        public uint Revision => revision;
        public event Action Died;
        public bool Restored { get; private set; }
        internal bool IsRestoring { get; private set; }

        public void ConfigureMelee(MeleeEnemyDefinition definition, RunDifficulty difficulty = RunDifficulty.Legacy)
        {
            if (!bound || Configuration != null) throw new InvalidOperationException("Bind an enemy only once before configuring its behaviour.");
            var configuration=definition.CreateConfiguration(difficulty);Configuration=configuration;
            if (Simulation != null) Behaviour = new MeleeEnemyBehaviour(Simulation, configuration,
                new System.Numerics.Vector2(transform.position.x, transform.position.y));
        }
        public void SynchronizeBehaviour()
        {
            if (Behaviour == null) return;
            BehaviourState = Behaviour.State; Facing = new Vector2(Behaviour.Facing.X, Behaviour.Facing.Y); WindupProgress = Behaviour.WindupProgress;
            StrikeSequence = Behaviour.StrikeSequence;
        }
        public void ApplyBehaviourSnapshot(int state, Vector2 facing, float progress, long strikeSequence)
        {
            if (Simulation != null || state < 0 || state > (int)EnemyBehaviourState.Dead || float.IsNaN(facing.sqrMagnitude)
                || float.IsInfinity(facing.sqrMagnitude) || Mathf.Abs(facing.sqrMagnitude - 1) > .01f
                || float.IsNaN(progress) || float.IsInfinity(progress) || progress < 0 || progress > 1 || strikeSequence < StrikeSequence)
                throw new InvalidOperationException("Invalid enemy behaviour snapshot.");
            BehaviourState = (EnemyBehaviourState)state; Facing = facing; WindupProgress = progress;
            StrikeSequence = strikeSequence;
        }

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

        public bool ApplySnapshot(EnemyIdentity identity, EnemyLifecycle lifecycle, int health, int maximum, uint nextRevision, int aggro, bool restoring = false)
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
            if (restoring && !replicaHasSnapshot && lifecycle == EnemyLifecycle.Dead) Lifecycle = EnemyLifecycle.Dead;
            replicaHasSnapshot = true;
            PresentReplica(lifecycle, health, nextRevision, aggro,restoring); return true;
        }

        private void PresentReplica(EnemyLifecycle lifecycle, int health, uint nextRevision, int aggro,bool silent=false)
        { target.PresentAuthoritativeHealth(health, target.InitialHealth,silent); Present(lifecycle, health, nextRevision, aggro); }

        private void Present(EnemyLifecycle lifecycle, int health, uint nextRevision, int aggro)
        {
            var died = Lifecycle == EnemyLifecycle.Alive && (lifecycle == EnemyLifecycle.Dead || health == 0);
            Lifecycle = lifecycle; revision = nextRevision; AggroPlayer = aggro;
            if (Simulation != null) target.PresentEnemyHealth();
            if (lifecycle != EnemyLifecycle.Alive) foreach (var shape in target.HitColliders) shape.enabled = false;
            if (lifecycle == EnemyLifecycle.Despawned) gameObject.SetActive(false);
            if (died) { GameAudio.Emit(SoundCue.EnemyDown);DeathEvents++; Died?.Invoke(); }
        }

        public void Restore(SavedEnemy saved)
        {
            if (Simulation == null || saved.Spawn != Identity.Spawn || saved.Archetype != Archetype) throw new InvalidOperationException("Only an authoritative matching enemy can be restored.");
            // Restore is not a new death or damage event.
            IsRestoring=true;
            try
            {
                Restored = true;
                Lifecycle = saved.Health == 0 ? EnemyLifecycle.Dead : EnemyLifecycle.Alive;
                Simulation.RestoreHealth(saved.Health,saved.Maximum);
                transform.position = new Vector3(saved.Position.X,saved.Position.Y,0);
                Behaviour?.Restore(saved.Facing); SynchronizeBehaviour();
            }
            finally{IsRestoring=false;}
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
