using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    public enum RunUpgrade { ReinforcedSuit = 1, LightweightGear = 2, WeaponCalibration = 3 }

    /// <summary>Immutable, versioned choices and grant ledger. A grant may be durable before level loading finishes.</summary>
    public sealed class RunUpgrades
    {
        // Policy 1 saves keep their choices and grant ledger, adopting the retuned damage without regranting.
        public const int CurrentVersion = 2;
        public int Version => CurrentVersion;
        public int Players { get; }
        public int GrantedLevels { get; }
        public int PendingLevel { get; }
        public IReadOnlyList<int> Choices { get; }
        public bool Pending => PendingLevel > 0;
        public bool AllConfirmed
        {
            get { if (!Pending) return false; for(int i=0;i<Players;i++)if(Choice(i)==0)return false;return true; }
        }
        public bool CanGrant => AllConfirmed && GrantedLevels < PendingLevel;
        public bool CanAdvance => AllConfirmed && GrantedLevels == PendingLevel;

        public RunUpgrades(int players, int grantedLevels = 0, int pendingLevel = 0, IReadOnlyList<int> choices = null)
        {
            if(players<1 || players>2 || grantedLevels<0 || grantedLevels>2 || pendingLevel<0 || pendingLevel>2
                || (pendingLevel>0 && (pendingLevel<grantedLevels || pendingLevel>grantedLevels+1)))throw new ArgumentException("Invalid upgrade progression.");
            var copy=choices==null?new int[players*2]:new List<int>(choices).ToArray();
            if(copy.Length!=players*2)throw new ArgumentException("Invalid upgrade slots.");
            for(int level=1;level<=2;level++)for(int slot=0;slot<players;slot++)
            {
                int value=copy[(level-1)*players+slot];
                if(value<0 || value>3 || (level<=grantedLevels && value==0)
                    || (level>grantedLevels && level!=pendingLevel && value!=0))throw new ArgumentException("Invalid upgrade choices.");
            }
            Players=players;GrantedLevels=grantedLevels;PendingLevel=pendingLevel;Choices=Array.AsReadOnly(copy);
        }
        public RunUpgrades Begin(int level)
        {
            if(level<1 || level>2)throw new InvalidOperationException("Upgrade stop is out of range.");
            if(PendingLevel==level)return this;
            if(Pending || level!=GrantedLevels+1 || level>2)throw new InvalidOperationException("Upgrade stop is out of sequence.");
            return new RunUpgrades(Players,GrantedLevels,level,Choices);
        }
        public int Choice(int slot)
        { if(slot<0 || slot>=Players)throw new ArgumentOutOfRangeException(nameof(slot));return Pending?Choices[(PendingLevel-1)*Players+slot]:0; }
        public bool TrySelect(int slot, int upgrade, out RunUpgrades next)
        {
            next=this;
            if(!Pending || GrantedLevels>=PendingLevel || slot<0 || slot>=Players || upgrade<1 || upgrade>3 || Choice(slot)!=0)return false;
            var copy=new List<int>(Choices).ToArray();copy[(PendingLevel-1)*Players+slot]=upgrade;
            next=new RunUpgrades(Players,GrantedLevels,PendingLevel,copy);return true;
        }
        public RunUpgrades Grant()
        { if(!CanGrant)throw new InvalidOperationException("Upgrade grant is not ready.");return new RunUpgrades(Players,PendingLevel,PendingLevel,Choices); }
        public RunUpgrades EnterLevel(int level)
        {
            if(level<1 || level>3 || (!Pending && GrantedLevels!=level-1) || (Pending && (!CanAdvance || level!=PendingLevel+1)))throw new InvalidOperationException("Upgrade transition is not committed.");
            return Pending?new RunUpgrades(Players,GrantedLevels,0,Choices):this;
        }
        public int Rank(int slot, RunUpgrade upgrade)
        {
            if(slot<0 || slot>=Players || upgrade<RunUpgrade.ReinforcedSuit || upgrade>RunUpgrade.WeaponCalibration)throw new ArgumentOutOfRangeException();
            int count=0;for(int level=0;level<GrantedLevels;level++)if(Choices[level*Players+slot]==(int)upgrade)count++;return count;
        }
        public int MovementPercent(int slot) => 8*Rank(slot,RunUpgrade.LightweightGear);
        public int DamagePercent(int slot) => 20*Rank(slot,RunUpgrade.WeaponCalibration);
        public RunLifeSnapshot PreviewGrantHealth(RunLifeSnapshot life)
        {
            if(!CanGrant || life.Players.Count!=Players)throw new InvalidOperationException("Upgrade health cannot be granted.");
            var vitals=new PlayerVitals[Players];
            for(int i=0;i<Players;i++)
            {
                var old=life.Players[i];if(old.State!=PlayerLifeState.Alive)throw new InvalidOperationException("Only a living party may advance.");
                int increase=Choice(i)==(int)RunUpgrade.ReinforcedSuit?20:0;
                vitals[i]=new PlayerVitals(checked(old.Current+increase),checked(old.Maximum+increase),old.State);
            }
            return new RunLifeSnapshot(life.RunId,life.RunXp,vitals,life.Profiles,life.Classes);
        }
        public static string Name(int id) => id==1?"Reinforced Suit":id==2?"Lightweight Gear":id==3?"Weapon Calibration":throw new ArgumentOutOfRangeException(nameof(id));
        public static string Description(int id) => id==1?"+20 maximum health and heal 20 HP":id==2?"+8% base movement speed":id==3?"+20% base weapon damage":throw new ArgumentOutOfRangeException(nameof(id));
    }
}

