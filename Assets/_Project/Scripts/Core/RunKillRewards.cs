using System;
using System.Collections.Generic;
namespace SpaceCrawler.Core
{
    /// <summary>Immutable per-run tuning. Serialized with the checkpoint, never read from live content on Continue.</summary>
    public sealed class RunRewardPolicy : IEquatable<RunRewardPolicy>
    {
        public const int CurrentVersion=1;
        public int Version => CurrentVersion;
        public int BaseXp { get; }
        // Melee, ranged; levels one through three; Easy, Normal, Hard.
        public IReadOnlyList<float> Factors { get; }
        public RunRewardPolicy(int baseXp,IReadOnlyList<float> factors)
        {
            if(baseXp<1 || baseXp>100000 || factors==null || factors.Count!=8)throw new ArgumentException("Invalid reward tuning.");
            var copy=new float[8];
            for(int i=0;i<8;i++)
            {float f=factors[i];if(float.IsNaN(f)||float.IsInfinity(f)||f<=0||f>100)throw new ArgumentException("Invalid reward multiplier.");copy[i]=f;}
            BaseXp=baseXp;Factors=Array.AsReadOnly(copy);
        }
        public static RunRewardPolicy Default()=>new RunRewardPolicy(RunLife.BaseRunXpGain,new[]{1f,1.25f,1f,1.25f,1.5f,.75f,1f,1.5f});
        public long KillXp(int level,EnemyArchetype archetype,RunDifficulty difficulty)
        {
            if(level<1||level>3 || (archetype!=EnemyArchetype.Melee && archetype!=EnemyArchetype.Ranged))throw new ArgumentException("Invalid kill identity.");
            DifficultyRules.Factor(difficulty);
            double factor=difficulty==RunDifficulty.Legacy?1:Factors[5+(int)difficulty-1];
            return checked((long)Math.Round(BaseXp*(double)Factors[(int)archetype-1]*Factors[level+1]*factor,0,MidpointRounding.AwayFromZero));
        }
        public bool Equals(RunRewardPolicy other)
        {if(other==null||BaseXp!=other.BaseXp)return false;for(int i=0;i<8;i++)if(Factors[i]!=other.Factors[i])return false;return true;}
        public override bool Equals(object other)=>other is RunRewardPolicy policy && Equals(policy);
        public override int GetHashCode(){int hash=BaseXp;foreach(float f in Factors)hash=unchecked(hash*31+f.GetHashCode());return hash;}
    }
    /// <summary>Authority-only kill accounting. Saved dead enemies seed the ledger; XP and dead state share one checkpoint.</summary>
    public sealed class RunKillRewards
    {
        private readonly RunLife life;
        private readonly RunDifficulty difficulty;
        private readonly HashSet<(int,int,int)> credited=new HashSet<(int,int,int)>();
        public RunRewardPolicy Policy { get; }
        public RunKillRewards(RunLife life,RunRewardPolicy policy,RunDifficulty difficulty,RunWorldSnapshot saved=null)
        {
            this.life=life ?? throw new ArgumentNullException(nameof(life));Policy=policy;DifficultyRules.Factor(difficulty);this.difficulty=difficulty;
            if(saved!=null)foreach(var room in saved.Rooms)foreach(var enemy in room.Enemies)
                if(enemy.Health==0)credited.Add((room.Level,room.Room.Value,enemy.Spawn));
        }
        public bool TryCredit(int level,RunRoomId room,int spawn,EnemyArchetype archetype)
        {
            if(level<1||level>3||spawn<1||spawn>32)throw new ArgumentException("Invalid kill identity.");
            if(Policy==null || life.Ended)return false;
            long amount=Policy.KillXp(level,archetype,difficulty);var key=(level,room.Value,spawn);
            if(amount==0 || !credited.Add(key))return false;
            // Reserve before Changed callbacks. If a storage callback throws after XP changed, keep the reservation.
            if(life.TryAwardRunXp(amount))return true;
            credited.Remove(key);return false;
        }
    }
}
