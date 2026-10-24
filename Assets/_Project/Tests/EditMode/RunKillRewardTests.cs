using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunKillRewardTests
    {
        [TestCase(1,EnemyArchetype.Melee,RunDifficulty.Easy,8)]
        [TestCase(1,EnemyArchetype.Melee,RunDifficulty.Normal,10)]
        [TestCase(2,EnemyArchetype.Melee,RunDifficulty.Normal,13)]
        [TestCase(3,EnemyArchetype.Melee,RunDifficulty.Normal,15)]
        [TestCase(1,EnemyArchetype.Ranged,RunDifficulty.Normal,13)]
        [TestCase(3,EnemyArchetype.Ranged,RunDifficulty.Hard,28)]
        public void ApprovedFormulaRoundsHalvesUpward(int level,EnemyArchetype type,RunDifficulty difficulty,long expected)
            =>Assert.That(RunRewardPolicy.Default().KillXp(level,type,difficulty),Is.EqualTo(expected));
        [Test]public void CoreDeathRewardsAreCompleteBeforeStateObserversRun()
        {
            var life=new RunLife(RunLifeSnapshot.Fresh(1));var ledger=new RunKillRewards(life,RunRewardPolicy.Default(),RunDifficulty.Normal);
            var enemy=new EnemySimulation(new EnemyIdentity(1,1),100);int observed=0;
            enemy.Died+=()=>{Assert.That(enemy.Lifecycle,Is.EqualTo(EnemyLifecycle.Dead));Assert.That(enemy.Health.Current,Is.Zero);ledger.TryCredit(1,new RunRoomId(1),1,EnemyArchetype.Melee);};
            enemy.Changed+=()=>{observed++;Assert.That(life.RunXp,Is.EqualTo(10),"Presentation and saves must not observe an unrewarded death.");};
            Assert.That(enemy.TryApplyDamage(enemy.Identity,CombatFaction.Player,new DamageRequest(new PlayerIdentity(1),1,enemy.Identity.Damageable,100),out _),Is.True);Assert.That(observed,Is.EqualTo(1));
        }
        [Test]public void SharedPoolAwardsOnceEvenIfChangedCallbacksReenter()
        {
            var life=new RunLife(RunLifeSnapshot.Fresh(2));life.TryDamage(new PlayerIdentity(2),Guid.NewGuid(),100);
            var rewards=new RunKillRewards(life,RunRewardPolicy.Default(),RunDifficulty.Normal);int notifications=0;
            life.Changed+=()=>{notifications++;Assert.That(rewards.TryCredit(1,new RunRoomId(2),1,EnemyArchetype.Melee),Is.False);};
            Assert.That(rewards.TryCredit(1,new RunRoomId(2),1,EnemyArchetype.Melee),Is.True);
            Assert.That(life.RunXp,Is.EqualTo(10));Assert.That(notifications,Is.EqualTo(1));Assert.That(life[1].State,Is.EqualTo(PlayerLifeState.Downed));
        }
        [Test]public void RestoredDeathsCannotPayAgainButRoomsAndLevelsHaveDistinctKills()
        {
            var world=new RunWorldSnapshot(new[]{new SavedPlayerPose(Vector2.Zero,Vector2.UnitX,0)},new[]{new SavedRoomState(1,new RunRoomId(2),new[]{new SavedEnemy(1,0,100,Vector2.Zero,Vector2.UnitX)})});
            var life=new RunLife(new RunLifeSnapshot(Guid.NewGuid(),10,new[]{PlayerVitals.Starting}));
            var ledger=new RunKillRewards(life,RunRewardPolicy.Default(),RunDifficulty.Normal,world);
            Assert.That(ledger.TryCredit(1,new RunRoomId(2),1,EnemyArchetype.Melee),Is.False);
            Assert.That(ledger.TryCredit(1,new RunRoomId(3),1,EnemyArchetype.Melee),Is.True);
            Assert.That(ledger.TryCredit(2,new RunRoomId(2),1,EnemyArchetype.Melee),Is.True);Assert.That(life.RunXp,Is.EqualTo(33));
        }
        [Test]public void DisabledAndEndedRunsCannotEarnAndInvalidConfigurationIsRejected()
        {
            var life=new RunLife(RunLifeSnapshot.Fresh(1));Assert.That(new RunKillRewards(life,null,RunDifficulty.Normal).TryCredit(1,new RunRoomId(0),1,EnemyArchetype.Melee),Is.False);
            var ledger=new RunKillRewards(life,RunRewardPolicy.Default(),RunDifficulty.Normal);life.TryDamage(new PlayerIdentity(1),Guid.NewGuid(),100);
            Assert.That(ledger.TryCredit(1,new RunRoomId(0),1,EnemyArchetype.Melee),Is.False);
            Assert.Throws<ArgumentException>(()=>new RunRewardPolicy(10,new[]{float.NaN,1f,1f,1f,1f,1f,1f,1f}));
            Assert.Throws<ArgumentException>(()=>RunRewardPolicy.Default().KillXp(4,EnemyArchetype.Melee,RunDifficulty.Normal));
        }
        private static RunCheckpoint Save(RunRewardPolicy policy)=>new RunCheckpoint(42,1,new string('a',64),SavedSessionMode.Solo,new RunRoomId(0),new[]{new RunRoomId(0)},new[]{"weapon.knife"},difficulty:RunDifficulty.Normal,rewards:policy);
        [Test]public void CheckpointFreezesConfigurableRewardValuesAndUpgradeKeepsDisabledPolicy()
        {
            var factors=new[]{2f,3f,1f,2f,3f,1f,2f,3f};var policy=new RunRewardPolicy(20,factors);factors[0]=99;
            var restored=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(Save(policy)));
            Assert.That(restored.Rewards,Is.EqualTo(policy));Assert.That(restored.Rewards.KillXp(1,EnemyArchetype.Melee,RunDifficulty.Normal),Is.EqualTo(80));
            byte[] current=RunCheckpointCodec.Encode(Save(null));int length=BitConverter.ToInt32(current,8)-4;byte[] payload=new byte[length];Array.Copy(current,12,payload,0,length);
            byte[] old;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))using(var sha=System.Security.Cryptography.SHA256.Create())
            {writer.Write(0x53435253);writer.Write(6);writer.Write(length);writer.Write(payload);writer.Write(sha.ComputeHash(payload));old=stream.ToArray();}
            var legacy=RunCheckpointCodec.Decode(old);Assert.That(legacy.Rewards,Is.Null);Assert.That(legacy.SourceSchemaVersion,Is.EqualTo(6));
            string path=Path.Combine("Logs","Validation","RewardSaves",Guid.NewGuid().ToString("N"),"run.save");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,old);
            new RunCheckpointStore(path).Save(legacy);Assert.That(File.ReadAllBytes(path+".schema6"),Is.EqualTo(old));Assert.That(new RunCheckpointStore(path).Load(out var loaded,out _),Is.EqualTo(CheckpointLoadStatus.Valid));Assert.That(loaded.Rewards,Is.Null);
        }
        [Test]public void EndRunConvertsSixPercentOnceAndFractionCarriesAcrossRuns()
        {
            string folder=Path.Combine("Logs","Validation","RewardSaves",Guid.NewGuid().ToString("N"));var profile=new ProfileProgressStore(folder);
            for(int i=0;i<2;i++)
            {
                var life=new RunLife(RunLifeSnapshot.Fresh(1));life.BindProfile(0,profile.Profile.Id);
                new RunKillRewards(life,RunRewardPolicy.Default(),RunDifficulty.Normal).TryCredit(1,new RunRoomId(1),1,EnemyArchetype.Melee);
                Assert.That(profile.Profile.Experience,Is.Zero);life.TryDamage(new PlayerIdentity(1),Guid.NewGuid(),100);
                var receipt=life.CreateReceipt(RunEndReason.Loss);profile.EndRun(receipt);long xp=profile.Profile.Experience;int fraction=profile.Profile.FractionHundredths;
                profile.EndRun(receipt);Assert.That(profile.Profile.Experience,Is.EqualTo(xp));Assert.That(profile.Profile.FractionHundredths,Is.EqualTo(fraction));
            }
            var reloaded=new ProfileProgressStore(folder);Assert.That(reloaded.Profile.Experience,Is.EqualTo(1));Assert.That(reloaded.Profile.FractionHundredths,Is.EqualTo(20));
        }
    }
}
