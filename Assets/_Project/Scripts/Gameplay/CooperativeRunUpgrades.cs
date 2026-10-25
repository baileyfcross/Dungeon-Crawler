using System;
using System.IO;
using SpaceCrawler.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [Serializable] public sealed class UpgradeData
    {
        public int version,players,granted,pending;public int[] choices;
        public static UpgradeData From(RunUpgrades state)
        {return state==null?null:new UpgradeData{version=state.Version,players=state.Players,granted=state.GrantedLevels,pending=state.PendingLevel,choices=new System.Collections.Generic.List<int>(state.Choices).ToArray()};}
        public RunUpgrades ToCore()
        {
            // JsonUtility materializes null nested serializable objects as empty objects.
            if(version==0 && players==0 && granted==0 && pending==0 && (choices==null || choices.Length==0))return null;
            if(version!=RunUpgrades.CurrentVersion)throw new NotSupportedException("Unsupported upgrade policy.");
            return new RunUpgrades(players,granted,pending,choices);
        }
    }
    [Serializable] public sealed class UpgradeChoiceRequest {public string runId;public uint epoch;public int level,choice;}
    [Serializable] public sealed class UpgradeUpdate {public string runId;public uint epoch,revision;public UpgradeData data;public bool saveBlocked;}

    public sealed partial class MultiplayerSession
    {
        private const string UpgradeChoiceMessage="session.upgrade-choice.v14",UpgradeStateMessage="session.upgrades.v14";
        public RunUpgrades Upgrades { get; private set; }
        public bool UpgradePending=>Upgrades?.Pending==true;
        public string UpgradeNotice=>Mode==SessionRole.Host?HealthFailure:upgradeSaveBlocked?"The host could not save upgrade progress. Waiting for retry.":null;
        private uint upgradeRevision,lastUpgradeRevision;
        private bool upgradeSaveBlocked;
        private float nextUpgradeRetry;
        private void ApplyUpgradeStats()
        {
            if(Mode!=SessionRole.Host)return;
            for(int i=0;i<2;i++)if(players[i]!=null)
            {players[i].Simulation.SetMovementBonusPercent(Upgrades?.MovementPercent(i) ?? 0);players[i].Combat.Simulation.SetDamageBonusPercent(Upgrades?.DamagePercent(i) ?? 0);}
        }
        private void ApplyUpgradeGate()
        {
            if(players[LocalPlayer-1]!=null)players[LocalPlayer-1].Input.SetModalAvailable(!UpgradePending);
            if(Mode==SessionRole.Host && UpgradePending)
            {
                if(Authority?.Running==true)Authority.SetRunning(false,epoch);
                Array.Clear(pendingDoors,0,pendingDoors.Length);
                foreach(var player in players)if(player!=null)player.Motor.Stop();
            }
        }
        public bool SelectUpgrade(int choice)
        {
            if(!roomReady || !UpgradePending || Upgrades.Choice(LocalPlayer-1)!=0 || choice<1 || choice>3 || RunEnded)return false;
            if(Mode==SessionRole.Host)return AcceptUpgradeChoice(0,choice);
            if(!network.IsConnectedClient)return false;
            SendJson(UpgradeChoiceMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new UpgradeChoiceRequest{runId=runId.ToString("N"),epoch=epoch,level=LevelNumber,choice=choice}));return true;
        }
        private bool AcceptUpgradeChoice(int slot,int choice)
        {
            if(!roomReady || Life.Ended || !UpgradePending || !Upgrades.TrySelect(slot,choice,out var next))return false;
            Upgrades=next;TrySave();SendUpgradeState();Changed?.Invoke();return true;
        }
        private void ReceiveUpgradeChoice(ulong sender,FastBufferReader reader)
        {
            if(Mode!=SessionRole.Host || !remoteSender.HasValue || sender!=remoteSender.Value)return;
            try
            {
                reader.ReadValueSafe(out string json);var request=JsonUtility.FromJson<UpgradeChoiceRequest>(json);
                if(request==null || request.runId!=runId.ToString("N") || request.epoch!=epoch || request.level!=LevelNumber)return;
                AcceptUpgradeChoice(1,request.choice);
            }
            catch(Exception error) when(error is ArgumentException || error is InvalidOperationException){SendUpgradeState();}
        }
        private void SendUpgradeState()
        {
            if(Mode!=SessionRole.Host || !remoteSender.HasValue || !network.IsListening)return;
            SendJson(UpgradeStateMessage,remoteSender.Value,JsonUtility.ToJson(new UpgradeUpdate{runId=runId.ToString("N"),epoch=epoch,revision=++upgradeRevision,data=UpgradeData.From(Upgrades),saveBlocked=HealthFailure!=null}));
        }
        private void ReceiveUpgradeState(ulong sender,FastBufferReader reader)
        {
            if(Mode!=SessionRole.Client || sender!=NetworkManager.ServerClientId)return;
            try
            {
                reader.ReadValueSafe(out string json);var update=JsonUtility.FromJson<UpgradeUpdate>(json);
                if(update.runId!=runId.ToString("N") || update.epoch!=epoch || update.revision<=lastUpgradeRevision)return;
                var next=update.data?.ToCore();
                ValidateUpgradeSnapshot(next,LevelNumber,true);
                Upgrades=next;lastUpgradeRevision=update.revision;upgradeSaveBlocked=update.saveBlocked;ApplyUpgradeGate();Changed?.Invoke();
            }
            catch(Exception error){Fail("Rejected upgrade state: "+error.Message);}
        }
        private void ValidateUpgradeSnapshot(RunUpgrades next,int level,bool established)
        {
            if(established && (next==null)!=(Upgrades==null))throw new InvalidOperationException("Upgrade policy changed.");
            if(next==null)return;
            if(next.Players!=2 || (next.Pending?next.PendingLevel!=level:next.GrantedLevels!=level-1))
                throw new InvalidOperationException("Upgrade state does not match the level.");
            if(!established || Upgrades==null)return;
            if(next.GrantedLevels<Upgrades.GrantedLevels)throw new InvalidOperationException("Upgrade grant regressed.");
            for(int i=0;i<Upgrades.Choices.Count;i++)
                if(Upgrades.Choices[i]!=0 && next.Choices[i]!=Upgrades.Choices[i])throw new InvalidOperationException("Confirmed upgrade changed.");
        }
        private void TickUpgrades()
        {
            if(Mode!=SessionRole.Host || !UpgradePending || Life==null || Life.Ended || stopped || !roomReady)return;
            ApplyUpgradeGate();
            if(Time.unscaledTime<nextUpgradeRetry)return;nextUpgradeRetry=Time.unscaledTime+.5f;
            try
            {
                if(Upgrades.CanGrant)
                {
                    var health=Upgrades.PreviewGrantHealth(Life.Capture());var granted=Upgrades.Grant();
                    SaveCooperativeWorld(health,granted);
                    Upgrades=granted;ApplyUpgradeStats();Life.AcceptUpgradeHealth(health);
                }
                else SaveCurrent();
                HealthFailure=null;SendUpgradeState();
                if(Upgrades.CanAdvance)AdvanceCooperativeTravel();
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
            {HealthFailure="Upgrade progress could not be saved. Retrying; the party has not advanced.";SendUpgradeState();}
            Changed?.Invoke();
        }
    }
}
