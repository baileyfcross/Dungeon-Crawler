using System;
using System.Collections.Generic;
using System.Text;
using SpaceCrawler.Core;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Read-only text for one player's authoritative run state. Never grants upgrades.</summary>
    public sealed class RunStatusPresentation
    {
        private readonly string classId, weaponName;
        private readonly RunDifficulty difficulty;
        private readonly int level, slot;
        private readonly PlayerVitals vitals;
        private readonly WeaponConfiguration weapon;
        private readonly RunUpgrades upgrades;

        public RunStatusPresentation(string classId, RunDifficulty difficulty, int level, string weaponName,
            WeaponConfiguration weapon, PlayerVitals vitals, RunUpgrades upgrades, int slot)
        {
            this.classId=classId;this.difficulty=difficulty;this.level=level;this.weaponName=weaponName;
            this.weapon=weapon;this.vitals=vitals;this.upgrades=upgrades;this.slot=slot;
            if(upgrades!=null && (slot<0 || slot>=upgrades.Players))throw new ArgumentOutOfRangeException(nameof(slot));
        }
        private int Rank(int id)=>upgrades?.Rank(slot,(RunUpgrade)id) ?? 0;
        private int MovementBonus=>upgrades?.MovementPercent(slot) ?? 0;
        private int DamageBonus=>upgrades?.DamagePercent(slot) ?? 0;
        private int Damage(int percent)=>weapon.WithDamageBonus(percent).Damage;
        private string Health=>$"{vitals.Current}/{vitals.Maximum} HP";
        private string ClassName
        {
            get { for(int i=0;i<PlayerClasses.Ids.Count;i++)if(PlayerClasses.Ids[i]==classId)return PlayerClasses.Names[i];return "Not selected"; }
        }
        public string Summary
        {
            get
            {
                var text=new StringBuilder();
                text.AppendLine($"Class: {ClassName}  ·  {DifficultyRules.DisplayName(difficulty)}  ·  Level {level}/{RunSequence.LevelCount}");
                text.AppendLine($"Weapon: {weaponName}  ·  Health: {Health}");
                if(upgrades==null)text.AppendLine("Run upgrades are unavailable for this saved run.");
                else for(int id=1;id<=3;id++)text.AppendLine($"{RunUpgrades.Name(id)}: Rank {Rank(id)}/2");
                text.Append($"Total bonuses: +{MovementBonus}% base movement, +{DamageBonus}% base damage");
                return text.ToString();
            }
        }
        public string Offer(int id)
        {
            string heading=$"{RunUpgrades.Name(id)}  ·  Owned: {Rank(id)}/2";
            bool preview=upgrades?.Pending==true && upgrades.GrantedLevels<upgrades.PendingLevel;
            var next=preview?ProjectedGrant(id):upgrades;
            if(id==1)return heading+"\nHealth: "+Health+(preview?$" → {vitals.Current+20}/{vitals.Maximum+20} HP":"");
            if(id==2)return heading+"\nBase movement bonus: +"+MovementBonus+"%"+(preview?" → +"+next.MovementPercent(slot)+"%":"");
            if(weapon==null)return heading+"\nWeapon unavailable";
            return heading+$"\n{weaponName} damage: {Damage(DamageBonus)}"+(preview?$" → {Damage(next.DamagePercent(slot))}":"")
                +$" (+{DamageBonus}%"+(preview?" → +"+next.DamagePercent(slot)+"%":"")+" base)";
        }
        private RunUpgrades ProjectedGrant(int choice)
        {
            // Project through the existing immutable rank/bonus rules; this copy is never persisted.
            var choices=new List<int>(upgrades.Choices).ToArray();
            int start=(upgrades.PendingLevel-1)*upgrades.Players;
            for(int player=0;player<upgrades.Players;player++)if(choices[start+player]==0)choices[start+player]=choice;
            choices[start+slot]=choice;
            return new RunUpgrades(upgrades.Players,upgrades.GrantedLevels,upgrades.PendingLevel,choices).Grant();
        }
    }
}
