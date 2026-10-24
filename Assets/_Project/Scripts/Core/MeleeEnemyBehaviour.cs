using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpaceCrawler.Core
{
    public enum EnemyBehaviourState { Idle, Pursue, Windup, Strike, Recovery, Return, Dead }

    public class EnemyCombatConfiguration
    {
        public float Speed { get; }
        public float Detection { get; }
        public float Reach { get; }
        public float Windup { get; }
        public float Recovery { get; }
        public float LostTargetSeconds { get; }
        public int Damage { get; }
        public EnemyCombatConfiguration(float speed = 3, float detection = 7, float reach = 1.5f,
            float windup = .6f, float recovery = .8f, float lostTargetSeconds = 3, int damage = 20)
        {
            foreach (var value in new[] { speed, detection, reach, windup, recovery, lostTargetSeconds })
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (damage < 1) throw new ArgumentOutOfRangeException(nameof(damage));
            Speed = speed; Detection = detection; Reach = reach; Windup = windup; Recovery = recovery;
            LostTargetSeconds = lostTargetSeconds; Damage = damage;
        }
    }

    public sealed class MeleeEnemyConfiguration : EnemyCombatConfiguration
    {
        public MeleeEnemyConfiguration(float speed=3,float detection=7,float reach=1.5f,float windup=.6f,float recovery=.8f,float lostTargetSeconds=3,int damage=20)
            :base(speed,detection,reach,windup,recovery,lostTargetSeconds,damage) { }
    }

    public readonly struct EnemyObservation
    {
        public PlayerIdentity Player { get; }
        public Vector2 Position { get; }
        public bool Alive { get; }
        public bool Visible { get; }
        public bool Reachable { get; }
        public EnemyObservation(PlayerIdentity player, Vector2 position, bool alive, bool visible, bool reachable)
        {
            if (player.Value < 1 || player.Value > 2 || !PlayerSimulation.IsFinite(position)) throw new ArgumentException("Invalid enemy observation.");
            Player = player; Position = position; Alive = alive; Visible = visible; Reachable = reachable;
        }
    }

    /// <summary>Common lifecycle and target guards wrap the active combat states. Unity supplies
    /// observations and physics results; this authority emits movement and strike intentions.</summary>
    public sealed class MeleeEnemyBehaviour : IEnemyBehaviour
    {
        private readonly EnemySimulation enemy;
        private readonly MeleeEnemyConfiguration configuration;
        private readonly Vector2 spawn;
        private float remaining, unseen, restoredRecovery;
        private Vector2 lastSeen;
        public EnemyBehaviourState State { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.UnitX;
        public Vector2? Destination { get; private set; }
        public long StrikeSequence { get; private set; }
        public PlayerIdentity? StrikeTarget { get; private set; }
        public float WindupProgress => State == EnemyBehaviourState.Windup ? 1 - remaining / configuration.Windup : 0;

        public MeleeEnemyBehaviour(EnemySimulation enemy, MeleeEnemyConfiguration configuration, Vector2 spawn)
        {
            this.enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (!PlayerSimulation.IsFinite(spawn)) throw new ArgumentException("Invalid enemy spawn.");
            this.spawn = spawn;
        }

        public void Restore(Vector2 facing)
        {
            if (!PlayerSimulation.IsFinite(facing) || Math.Abs(facing.LengthSquared()-1) > .001f) throw new ArgumentException("Invalid restored facing.");
            Facing = facing; restoredRecovery = configuration.Recovery; State = enemy.Health.IsAlive ? EnemyBehaviourState.Recovery : EnemyBehaviourState.Dead;
            Destination = null; StrikeTarget = null;
        }

        public void Advance(float seconds, Vector2 position, IReadOnlyList<EnemyObservation> observations, bool running)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || !PlayerSimulation.IsFinite(position)
                || observations == null || observations.Count > 2) throw new ArgumentException("Invalid behaviour tick.");
            Destination = null; StrikeTarget = null;
            if (enemy.Lifecycle != EnemyLifecycle.Alive) { State = EnemyBehaviourState.Dead; return; }
            if (!running) return;
            if (restoredRecovery > 0)
            { restoredRecovery = Math.Max(0,restoredRecovery-seconds); State = EnemyBehaviourState.Recovery; return; }
            EnemyObservation? target = null;
            foreach (var observation in observations)
                if (enemy.AggroTarget.HasValue && observation.Player.Equals(enemy.AggroTarget.Value) && observation.Alive) target = observation;
            if (!target.HasValue && enemy.AggroTarget.HasValue)
            { enemy.TrySetAggroTarget(null); State = EnemyBehaviourState.Return; unseen = 0; }

            if (!target.HasValue)
            {
                var nearest = configuration.Detection * configuration.Detection;
                foreach (var observation in observations)
                {
                    var distance = Vector2.DistanceSquared(position, observation.Position);
                    if (!observation.Alive || !observation.Visible || !observation.Reachable || distance > nearest) continue;
                    if (target.HasValue && distance == nearest && observation.Player.Value > target.Value.Player.Value) continue;
                    nearest = distance; target = observation;
                }
                if (target.HasValue) { enemy.TrySetAggroTarget(target.Value.Player); State = EnemyBehaviourState.Pursue; unseen = 0; }
            }
            if (!target.HasValue)
            {
                if (Vector2.DistanceSquared(position, spawn) > .04f) { State = EnemyBehaviourState.Return; Destination = spawn; Face(spawn - position); }
                else State = EnemyBehaviourState.Idle;
                return;
            }

            var sensed = target.Value.Visible && target.Value.Reachable;
            if (sensed) { lastSeen = target.Value.Position; unseen = 0; }
            else unseen += seconds;
            if (unseen >= configuration.LostTargetSeconds)
            { enemy.TrySetAggroTarget(null); State = EnemyBehaviourState.Return; Destination = spawn; return; }

            switch (State)
            {
                case EnemyBehaviourState.Windup:
                    remaining = Math.Max(0, remaining - seconds);
                    if (remaining == 0)
                    {
                        State = EnemyBehaviourState.Strike; StrikeSequence++;
                        if (target.Value.Visible && ContainsStrike(position, target.Value.Position)) StrikeTarget = target.Value.Player;
                    }
                    return;
                case EnemyBehaviourState.Strike:
                    State = EnemyBehaviourState.Recovery; remaining = configuration.Recovery; return;
                case EnemyBehaviourState.Recovery:
                    remaining = Math.Max(0, remaining - seconds);
                    if (remaining > 0) return;
                    State = EnemyBehaviourState.Pursue; break;
            }
            if (sensed && Vector2.DistanceSquared(position, target.Value.Position) <= configuration.Reach * configuration.Reach)
            { Face(target.Value.Position - position); State = EnemyBehaviourState.Windup; remaining = configuration.Windup; }
            else { State = EnemyBehaviourState.Pursue; Destination = lastSeen; Face(lastSeen - position); }
        }

        public bool ContainsStrike(Vector2 origin, Vector2 point)
        {
            var delta = point - origin; var distance = delta.LengthSquared();
            return distance <= configuration.Reach * configuration.Reach
                && (distance < .000001f || Vector2.Dot(Vector2.Normalize(delta), Facing) >= .7071067f);
        }
        private void Face(Vector2 direction) { if (direction.LengthSquared() > .000001f) Facing = Vector2.Normalize(direction); }
    }
}
