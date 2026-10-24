using System;
using System.IO;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    public sealed partial class GameplaySession
    {
        public RunUpgrades Upgrades { get; private set; }
        public bool UpgradePending => Upgrades?.Pending==true;
        private float nextUpgradeRetry;
        private void ApplyUpgradeStats()
        {
            Simulation.SetMovementBonusPercent(Upgrades?.MovementPercent(0) ?? 0);
            combat.Simulation.SetDamageBonusPercent(Upgrades?.DamagePercent(0) ?? 0);
        }
        public bool SelectUpgrade(int choice)
        {
            if(flow.State!=ApplicationState.Gameplay || Life.Ended || Upgrades==null || !Upgrades.TrySelect(0,choice,out var next))return false;
            Upgrades=next;TrySave();HealthChanged?.Invoke();return true;
        }
        private void TickUpgrades()
        {
            if(!UpgradePending || rooms?.CurrentRoom==null || flow.State!=ApplicationState.Gameplay || Life.Ended)return;
            input.SetModalAvailable(false);
            if(Time.unscaledTime<nextUpgradeRetry)return;nextUpgradeRetry=Time.unscaledTime+.5f;
            try
            {
                if(Upgrades.CanGrant)
                {
                    var health=Upgrades.PreviewGrantHealth(Life.Capture());var granted=Upgrades.Grant();
                    // Persist the grant and healed health together before either becomes live.
                    CommitCheckpoint(rooms.Progression,health,granted);
                    Upgrades=granted;ApplyUpgradeStats();Life.AcceptUpgradeHealth(health);
                }
                else SaveCurrent();
                HealthFailure=null;
                if(Upgrades.CanAdvance)rooms.AdvanceAfterUpgrade();
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {HealthFailure="Upgrade progress could not be saved. Retrying; this run has not advanced.";}
            HealthChanged?.Invoke();
        }
    }
}
