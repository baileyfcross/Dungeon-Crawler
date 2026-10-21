using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    public enum CombatFaction { Player = 1, Enemy = 2 }
    public enum EnemyLifecycle { Alive = 1, Dead = 2, Despawned = 3 }

    /// <summary>Room-incarnation identity. Local spawn IDs may repeat only in a new incarnation.</summary>
    public readonly struct EnemyIdentity : IEquatable<EnemyIdentity>
    {
        public uint Encounter { get; }
        public int Spawn { get; }
        public DamageableIdentity Damageable => new DamageableIdentity(0x40000000 + Spawn);
        public EnemyIdentity(uint encounter, int spawn)
        {
            if (encounter == 0) throw new ArgumentOutOfRangeException(nameof(encounter));
            if (spawn < 1 || spawn > 32) throw new ArgumentOutOfRangeException(nameof(spawn));
            Encounter = encounter; Spawn = spawn;
        }
        public bool Equals(EnemyIdentity other) => Encounter == other.Encounter && Spawn == other.Spawn;
        public override bool Equals(object obj) => obj is EnemyIdentity other && Equals(other);
        public override int GetHashCode() => unchecked((int)Encounter * 397 ^ Spawn);
    }

    /// <summary>Host-owned lifecycle only; deliberately contains no senses, movement or attack decisions.</summary>
    public sealed class EnemySimulation
    {
        private readonly HashSet<(PlayerIdentity, long)> acceptedAttacks = new HashSet<(PlayerIdentity, long)>();
        public EnemyIdentity Identity { get; }
        public HealthSimulation Health { get; }
        public EnemyLifecycle Lifecycle { get; private set; } = EnemyLifecycle.Alive;
        public PlayerIdentity? AggroTarget { get; private set; }
        public uint Revision { get; private set; } = 1;
        public event Action Changed;
        public event Action Died;

        public EnemySimulation(EnemyIdentity identity, int maximumHealth)
        {
            if (identity.Encounter == 0) throw new ArgumentException("An encounter identity is required.", nameof(identity));
            Identity = identity;
            Health = new HealthSimulation(identity.Damageable, maximumHealth);
        }

        public void RestoreHealth(int current, int maximum)
        {
            if (Revision != 1 || maximum != Health.Maximum) throw new ArgumentException("Enemy restore requires fresh matching authoring.");
            Health.RestoreCurrent(current); Lifecycle = current == 0 ? EnemyLifecycle.Dead : EnemyLifecycle.Alive;
            AggroTarget = null; Revision++; Changed?.Invoke();
        }

        public bool TryApplyDamage(EnemyIdentity destination, CombatFaction sourceFaction, DamageRequest request, out DamageResult result)
        {
            result = default;
            if (!Identity.Equals(destination) || Lifecycle != EnemyLifecycle.Alive || sourceFaction != CombatFaction.Player
                || (request.Attacker.Value != 1 && request.Attacker.Value != 2)
                || acceptedAttacks.Contains((request.Attacker, request.AttackSequence))
                || !Health.TryApplyDamage(request, out result)) return false;
            // Arrival order may differ from attack order for projectiles. Each accepted hit
            // removes at least one HP, bounding this set by the authored initial health.
            acceptedAttacks.Add((request.Attacker, request.AttackSequence));
            var died = !Health.IsAlive;
            if (died) { Lifecycle = EnemyLifecycle.Dead; AggroTarget = null; }
            Revision++;
            Changed?.Invoke();
            if (died) Died?.Invoke();
            return true;
        }

        public bool TrySetAggroTarget(PlayerIdentity? player)
        {
            if (Lifecycle != EnemyLifecycle.Alive || (player.HasValue && player.Value.Value != 1 && player.Value.Value != 2)
                || Nullable.Equals(AggroTarget, player)) return false;
            AggroTarget = player; Revision++; Changed?.Invoke(); return true;
        }

        public bool Despawn()
        {
            if (Lifecycle == EnemyLifecycle.Despawned) return false;
            Lifecycle = EnemyLifecycle.Despawned; AggroTarget = null;
            acceptedAttacks.Clear();
            Revision++; Changed?.Invoke(); return true;
        }
    }
}
