using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpaceCrawler.Core
{
    public enum EnemyArchetype { Melee = 1, Ranged = 2 }
    public interface IEnemyBehaviour
    {
        EnemyBehaviourState State { get; }
        Vector2 Facing { get; }
        Vector2? Destination { get; }
        long StrikeSequence { get; }
        PlayerIdentity? StrikeTarget { get; }
        float WindupProgress { get; }
        void Restore(Vector2 facing);
        void Advance(float seconds, Vector2 position, IReadOnlyList<EnemyObservation> observations, bool running);
    }
    public sealed class RangedEnemyConfiguration
    {
        public EnemyCombatConfiguration Combat { get; }
        public float MinimumRange { get; }
        public float ProjectileSpeed { get; }
        public RangedEnemyConfiguration(int damage = 10, float speed = 3, float detection = 9,
            float minimumRange = 4, float maximumRange = 6, float windup = .75f, float recovery = 1.5f, float projectileSpeed = 8)
        {
            Combat = new EnemyCombatConfiguration(speed,detection,maximumRange,windup,recovery,3,damage);
            if (!PlayerSimulation.IsFinite(new Vector2(minimumRange,projectileSpeed)) || minimumRange <= 0
                || minimumRange >= maximumRange || projectileSpeed <= 0) throw new ArgumentException("Invalid ranged configuration.");
            MinimumRange=minimumRange; ProjectileSpeed=projectileSpeed;
        }
    }
    /// <summary>Authority-only ranged intentions. Aim locks at windup; physics and damage stay outside Core.</summary>
    public sealed class RangedEnemyBehaviour : IEnemyBehaviour
    {
        private readonly EnemySimulation enemy;
        private readonly RangedEnemyConfiguration config;
        private readonly Vector2 spawn;
        private float remaining;
        public EnemyBehaviourState State { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.UnitX;
        public Vector2? Destination { get; private set; }
        public long StrikeSequence { get; private set; }
        public PlayerIdentity? StrikeTarget { get; private set; }
        public float WindupProgress => State == EnemyBehaviourState.Windup ? Math.Max(0,1-remaining/config.Combat.Windup) : 0;
        public RangedEnemyBehaviour(EnemySimulation enemy,RangedEnemyConfiguration config,Vector2 spawn)
        {
            this.enemy=enemy ?? throw new ArgumentNullException(nameof(enemy));
            this.config=config ?? throw new ArgumentNullException(nameof(config));
            if(!PlayerSimulation.IsFinite(spawn))throw new ArgumentException("Invalid spawn.");this.spawn=spawn;
        }
        public void Restore(Vector2 facing)
        {
            if(!PlayerSimulation.IsFinite(facing) || Math.Abs(facing.LengthSquared()-1)>.001f)throw new ArgumentException("Invalid facing.");
            Facing=facing;remaining=config.Combat.Recovery;State=enemy.Health.IsAlive?EnemyBehaviourState.Recovery:EnemyBehaviourState.Dead;
            Destination=null;StrikeTarget=null;enemy.TrySetAggroTarget(null);
        }
        public void Advance(float seconds,Vector2 position,IReadOnlyList<EnemyObservation> observations,bool running)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0||!PlayerSimulation.IsFinite(position)||observations==null||observations.Count>2)
                throw new ArgumentException("Invalid behaviour tick.");
            Destination=null;StrikeTarget=null;
            if(enemy.Lifecycle!=EnemyLifecycle.Alive){State=EnemyBehaviourState.Dead;return;}
            if(!running)return;
            if(State==EnemyBehaviourState.Strike){State=EnemyBehaviourState.Recovery;remaining=config.Combat.Recovery;return;}
            if(State==EnemyBehaviourState.Recovery)
            {
                remaining=Math.Max(0,remaining-seconds);
                if(remaining>0)
                {
                    foreach(var o in observations)
                    {
                        var away=position-o.Position;float distance=away.Length();
                        if(o.Alive && o.Visible && o.Reachable && distance>.01f && distance<config.MinimumRange)
                        {Destination=position+Vector2.Normalize(away)*(config.MinimumRange-distance);break;}
                    }
                    return;
                }
                State=EnemyBehaviourState.Idle;
            }
            EnemyObservation? target=null;float nearest=config.Combat.Detection*config.Combat.Detection;
            foreach(var o in observations)
            {
                float distance=Vector2.DistanceSquared(position,o.Position);
                if(!o.Alive||!o.Visible||!o.Reachable||distance>nearest)continue;
                if(target.HasValue && distance==nearest && o.Player.Value>target.Value.Player.Value)continue;
                target=o;nearest=distance;
            }
            // Losing sight or changing target cancels a telegraph; a new shot always gets its full windup.
            if(!target.HasValue || (State==EnemyBehaviourState.Windup && !enemy.AggroTarget.Equals(target.Value.Player)))
            {
                enemy.TrySetAggroTarget(null);State=EnemyBehaviourState.Return;
                if(!target.HasValue){if(Vector2.DistanceSquared(position,spawn)>.04f)Destination=spawn;else State=EnemyBehaviourState.Idle;return;}
            }
            enemy.TrySetAggroTarget(target.Value.Player);
            if(State==EnemyBehaviourState.Windup)
            {
                remaining=Math.Max(0,remaining-seconds);
                if(remaining==0){State=EnemyBehaviourState.Strike;StrikeSequence++;StrikeTarget=target.Value.Player;}
                return;
            }
            var delta=target.Value.Position-position;
            if(delta.LengthSquared()>.000001f)Facing=Vector2.Normalize(delta);
            if(nearest>config.Combat.Reach*config.Combat.Reach){State=EnemyBehaviourState.Pursue;Destination=target.Value.Position;return;}
            State=EnemyBehaviourState.Windup;remaining=config.Combat.Windup;
        }
    }
    public static class EncounterComposition
    {
        public const int CurrentVersion=1;
        public static int RangedCount(ulong seed,RunDifficulty difficulty,int count,int version)
        {
            if(version<0||version>CurrentVersion||count<0||count>32)throw new ArgumentException("Unsupported encounter policy.");
            DifficultyRules.Factor(difficulty);
            int limit=version==0||difficulty==RunDifficulty.Legacy||difficulty==RunDifficulty.Easy?0:difficulty==RunDifficulty.Normal?1:2;
            return new DeterministicRandom(seed ^ 0xD1B54A32D192ED03UL).NextInt(Math.Min(limit,count)+1);
        }
    }
}
