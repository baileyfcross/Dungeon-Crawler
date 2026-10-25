using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunUpgradeTests
    {
        [Test]
        public void PartyChoicesAreIndependentImmutableAndGrantedOnlyOnce()
        {
            var fresh=new RunUpgrades(2);var pending=fresh.Begin(1);
            Assert.That(fresh.Pending,Is.False);Assert.That(pending.TrySelect(0,1,out var chosen),Is.True);
            Assert.That(chosen.TrySelect(0,2,out _),Is.False);Assert.That(chosen.TrySelect(2,2,out _),Is.False);
            Assert.That(chosen.TrySelect(1,99,out _),Is.False);Assert.That(chosen.CanGrant,Is.False);
            Assert.Throws<InvalidOperationException>(()=>chosen.Grant());
            Assert.That(chosen.TrySelect(1,2,out var both),Is.True);Assert.That(both.CanGrant,Is.True);
            var granted=both.Grant();Assert.That(granted.CanAdvance,Is.True);Assert.That(granted.Rank(0,RunUpgrade.ReinforcedSuit),Is.EqualTo(1));
            Assert.That(granted.MovementPercent(1),Is.EqualTo(8));Assert.Throws<InvalidOperationException>(()=>granted.Grant());
            var next=granted.EnterLevel(2);Assert.That(next.Pending,Is.False);Assert.That(next.Begin(2).PendingLevel,Is.EqualTo(2));
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void TwoStopsCapRanksAtTwoAndDoNotCompound(int upgrade)
        {
            var state=new RunUpgrades(1);
            for(int level=1;level<=2;level++){state=state.Begin(level);state.TrySelect(0,upgrade,out state);state=state.Grant().EnterLevel(level+1);}
            Assert.That(state.Rank(0,(RunUpgrade)upgrade),Is.EqualTo(2));Assert.That(state.MovementPercent(0),Is.EqualTo(upgrade==2?16:0));
            Assert.That(state.DamagePercent(0),Is.EqualTo(upgrade==3?40:0));Assert.Throws<InvalidOperationException>(()=>state.Begin(3));
        }
        [Test]
        public void HealthGrantKeepsDamageDeficitAndDoesNotAwardXp()
        {
            var life=new RunLife(RunLifeSnapshot.Fresh(1));life.TryDamage(new PlayerIdentity(1),Guid.NewGuid(),65);
            var state=new RunUpgrades(1).Begin(1);state.TrySelect(0,1,out state);
            var staged=state.PreviewGrantHealth(life.Capture());Assert.That(life[0].Current,Is.EqualTo(35));
            Assert.That(staged.Players[0].Current,Is.EqualTo(55));Assert.That(staged.Players[0].Maximum,Is.EqualTo(120));
            life.AcceptUpgradeHealth(staged);Assert.That(life.RunXp,Is.Zero);Assert.That(life.Capture().RunId,Is.EqualTo(staged.RunId));
            Assert.Throws<InvalidOperationException>(()=>state.Grant().PreviewGrantHealth(life.Capture()));
        }
        [TestCase(25,15,29)] [TestCase(25,30,33)] [TestCase(20,15,23)] [TestCase(20,30,26)]
        [TestCase(25,20,30)] [TestCase(25,40,35)] [TestCase(20,20,24)] [TestCase(20,40,28)]
        public void DamageRoundsOnceFromBaseAndPreservesWeaponDefinition(int baseline,int percent,int expected)
        {
            var player=new PlayerSimulation(new PlayerIdentity(1),5);player.SetActive(true);
            var weapon=WeaponConfiguration.Melee("weapon.knife",baseline,.5f,2,90);var combat=new CombatSimulation(player,weapon);
            combat.SetDamageBonusPercent(percent);combat.SetDamageBonusPercent(percent);
            Assert.That(combat.TryAttack(new AttackIntent(new PlayerIdentity(1),Vector2.UnitX),out var attack),Is.True);
            Assert.That(attack.Weapon.Damage,Is.EqualTo(expected));Assert.That(combat.EquippedWeapon.Damage,Is.EqualTo(baseline));
        }
        [Test]
        public void MovementUsesAdditiveBaseBonusBeforeSprint()
        {
            var player=new PlayerSimulation(new PlayerIdentity(1),5);player.SetActive(true);
            player.SetMovementBonusPercent(16);player.SetMovementBonusPercent(16);
            player.RequestMove(new MoveIntent(new PlayerIdentity(1),Vector2.UnitX,true));Assert.That(player.DesiredVelocity.X,Is.EqualTo(8.7f).Within(.001));
        }
        private static RunCheckpoint Save(RunUpgrades state)=>new RunCheckpoint(1,2,new string('a',64),SavedSessionMode.Cooperative,new RunRoomId(0),new[]{new RunRoomId(0)},new[]{"weapon.knife","weapon.knife"},sequenceVersion:2,levelSeeds:new ulong[]{1,2,3},levelFingerprints:Enumerable.Repeat(new string('a',64),3).ToArray(),upgrades:state);
        [Test]
        public void PendingAndDurableGrantRoundTripWithoutRerollOrDuplicateHealth()
        {
            var state=new RunUpgrades(2).Begin(1);state.TrySelect(0,1,out state);
            var pending=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(Save(state))).Upgrades;
            Assert.That(pending.Choice(0),Is.EqualTo(1));Assert.That(pending.Choice(1),Is.Zero);
            pending.TrySelect(1,3,out pending);var granted=pending.Grant();
            var restored=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(Save(granted))).Upgrades;
            Assert.That(restored.CanAdvance,Is.True);Assert.That(restored.CanGrant,Is.False);Assert.That(restored.Choices,Is.EqualTo(granted.Choices));
        }
        [Test]
        public void SchemaSevenStaysUpgradeFreeAndIsArchivedOnWrite()
        {
            byte[] current=RunCheckpointCodec.Encode(Save(null));int size=BitConverter.ToInt32(current,8)-4;var payload=new byte[size];Array.Copy(current,12,payload,0,size);
            byte[] old;using(var data=new MemoryStream())using(var writer=new BinaryWriter(data))using(var sha=SHA256.Create())
            {writer.Write(0x53435253);writer.Write(7);writer.Write(size);writer.Write(payload);writer.Write(sha.ComputeHash(payload));old=data.ToArray();}
            var saved=RunCheckpointCodec.Decode(old);Assert.That(saved.Upgrades,Is.Null);
            string path=Path.GetFullPath("Logs/Validation/UpgradeSaves/"+Guid.NewGuid().ToString("N")+"/run.save");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,old);
            new RunCheckpointStore(path).Save(saved);Assert.That(File.ReadAllBytes(path+".schema7"),Is.EqualTo(old));
            Assert.That(RunCheckpointCodec.Decode(File.ReadAllBytes(path)).Upgrades,Is.Null);
        }
        [Test]
        public void OriginalUpgradeSaveMigratesChoicesToRetunedDamageWithoutRegranting()
        {
            var state=new RunUpgrades(2).Begin(1);state.TrySelect(0,3,out state);state.TrySelect(1,2,out state);state=state.Grant();
            byte[] old=RunCheckpointCodec.Encode(Save(state));int size=BitConverter.ToInt32(old,8);
            Array.Copy(BitConverter.GetBytes(1),0,old,12+size-28,4);
            using(var sha=SHA256.Create())Array.Copy(sha.ComputeHash(old,12,size),0,old,12+size,32);
            var restored=RunCheckpointCodec.Decode(old);
            Assert.That(restored.Upgrades.Version,Is.EqualTo(2));Assert.That(restored.Upgrades.Choices,Is.EqualTo(state.Choices));
            Assert.That(restored.Upgrades.DamagePercent(0),Is.EqualTo(20));Assert.That(restored.Upgrades.CanGrant,Is.False);
            Assert.That(restored.Life.Players[0].Maximum,Is.EqualTo(100));Assert.That(restored.Life.RunXp,Is.Zero);
        }
        [TestCase(25,4,3)] [TestCase(20,5,4)]
        public void FullCalibrationCrossesTheNormalMeleeHealthBreakpoint(int baseline,int originalHits,int upgradedHits)
        {
            int full=WeaponConfiguration.Melee("weapon.test",baseline,.5f,2,90).WithDamageBonus(40).Damage;
            Assert.That((100+baseline-1)/baseline,Is.EqualTo(originalHits));Assert.That((100+full-1)/full,Is.EqualTo(upgradedHits));
        }
        [Test]
        public void InvalidFutureAndUncommittedStatesAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>new RunUpgrades(2,1,0,new[]{1,0,0,0}));
            Assert.Throws<ArgumentException>(()=>new RunUpgrades(1,0,0,new[]{1,0}));
            Assert.Throws<ArgumentException>(()=>new RunUpgrades(1,0,2));
            Assert.Throws<InvalidOperationException>(()=>new RunUpgrades(1).Begin(1).EnterLevel(2));
        }
    }
}
