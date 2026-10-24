using System;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;
namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RangedEnemyTests
    {
        private static EnemyObservation Target(Vector2 point,bool visible=true,bool alive=true,int slot=1)
            =>new EnemyObservation(new PlayerIdentity(slot),point,alive,visible,true);
        [Test] public void TelegraphLocksAimAndFiresOnlyAfterFullWindup()
        {
            var enemy=new EnemySimulation(new EnemyIdentity(1,1),80);var brain=new RangedEnemyBehaviour(enemy,new RangedEnemyConfiguration(),Vector2.Zero);
            brain.Advance(.02f,Vector2.Zero,new[]{Target(new Vector2(5,0))},true);
            Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Windup));Assert.That(brain.StrikeTarget,Is.Null);
            brain.Advance(.74f,Vector2.Zero,new[]{Target(new Vector2(0,5))},true);
            Assert.That(brain.StrikeTarget,Is.Null);Assert.That(brain.Facing,Is.EqualTo(Vector2.UnitX));
            brain.Advance(.02f,Vector2.Zero,new[]{Target(new Vector2(0,5))},true);
            Assert.That(brain.StrikeSequence,Is.EqualTo(1));Assert.That(brain.StrikeTarget.Value.Value,Is.EqualTo(1));
            brain.Advance(.02f,Vector2.Zero,new[]{Target(new Vector2(0,5))},true);Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Recovery));
        }
        [Test] public void PauseCoverDeathAndDownedTargetsCannotProduceShots()
        {
            var enemy=new EnemySimulation(new EnemyIdentity(1,1),80);var brain=new RangedEnemyBehaviour(enemy,new RangedEnemyConfiguration(),Vector2.Zero);
            brain.Advance(.02f,Vector2.Zero,new[]{Target(new Vector2(5,0))},true);
            brain.Advance(10,Vector2.Zero,new[]{Target(new Vector2(5,0))},false);Assert.That(brain.StrikeSequence,Is.Zero);
            brain.Advance(1,Vector2.Zero,new[]{Target(new Vector2(5,0),false)},true);Assert.That(brain.StrikeTarget,Is.Null);
            brain.Advance(1,Vector2.Zero,new[]{Target(new Vector2(5,0),alive:false)},true);Assert.That(enemy.AggroTarget,Is.Null);
            enemy.TryApplyDamage(enemy.Identity,CombatFaction.Player,new DamageRequest(new PlayerIdentity(1),1,enemy.Identity.Damageable,80),out _);brain.Advance(1,Vector2.Zero,new[]{Target(new Vector2(5,0))},true);Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Dead));
        }
        [Test] public void RestoredEnemyCannotImmediatelyFireAndApproachesDistantPlayers()
        {
            var enemy=new EnemySimulation(new EnemyIdentity(1,1),80);var brain=new RangedEnemyBehaviour(enemy,new RangedEnemyConfiguration(),Vector2.Zero);
            brain.Restore(Vector2.UnitY);brain.Advance(.1f,Vector2.Zero,new[]{Target(new Vector2(5,0))},true);
            Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Recovery));Assert.That(brain.StrikeTarget,Is.Null);
            brain.Advance(2,Vector2.Zero,new[]{Target(new Vector2(8,0))},true);Assert.That(brain.Destination,Is.EqualTo(new Vector2(8,0)));
        }
        [Test] public void SeededCompositionRespectsDifficultyCapsAndFrozenLegacyPolicy()
        {
            var normal=new System.Collections.Generic.HashSet<int>();var hard=new System.Collections.Generic.HashSet<int>();
            for(ulong seed=0;seed<200;seed++)
            {
                Assert.That(EncounterComposition.RangedCount(seed,RunDifficulty.Hard,5,0),Is.Zero);
                Assert.That(EncounterComposition.RangedCount(seed,RunDifficulty.Easy,2,1),Is.Zero);
                int n=EncounterComposition.RangedCount(seed,RunDifficulty.Normal,3,1),h=EncounterComposition.RangedCount(seed,RunDifficulty.Hard,5,1);
                Assert.That(n,Is.InRange(0,1));Assert.That(h,Is.InRange(0,2));
                Assert.That(n,Is.EqualTo(EncounterComposition.RangedCount(seed,RunDifficulty.Normal,3,1)));normal.Add(n);hard.Add(h);
            }
            Assert.That(normal.Count,Is.EqualTo(2));Assert.That(hard.Count,Is.EqualTo(3));
            Assert.Throws<ArgumentException>(()=>EncounterComposition.RangedCount(1,RunDifficulty.Hard,5,2));
        }
        [Test] public void HostileProjectileStopsOnceAndHasBoundedLifetime()
        {
            var shot=new EnemyProjectileSimulation(Vector2.Zero,Vector2.UnitX,8);
            Assert.That(shot.Distance(.5f),Is.EqualTo(4));shot.Advance(.5f,2,true);
            Assert.That(shot.Position,Is.EqualTo(new Vector2(2,0)));Assert.That(shot.Active,Is.False);
            shot.Advance(1,0,false);Assert.That(shot.Position,Is.EqualTo(new Vector2(2,0)));
            var expires=new EnemyProjectileSimulation(Vector2.Zero,Vector2.UnitX,8);expires.Advance(10,24,false);Assert.That(expires.Active,Is.False);
        }
        [Test] public void SaveRoundTripPreservesArchetypesAndEncounterPolicy()
        {
            var enemy=new SavedEnemy(1,40,80,new Vector2(2,3),Vector2.UnitY,EnemyArchetype.Ranged);
            var world=new RunWorldSnapshot(new[]{new SavedPlayerPose(Vector2.Zero,Vector2.UnitX,0)},new[]{new SavedRoomState(1,new RunRoomId(0),new[]{enemy})});
            var save=new RunCheckpoint(42,1,new string('a',64),SavedSessionMode.Solo,new RunRoomId(0),new[]{new RunRoomId(0)},new[]{"weapon.knife"},world:world,difficulty:RunDifficulty.Normal,encounterVersion:1);
            var restored=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(save));
            Assert.That(restored.EncounterVersion,Is.EqualTo(1));Assert.That(restored.World.Rooms[0].Enemies[0].Archetype,Is.EqualTo(EnemyArchetype.Ranged));
            Assert.That(restored.World.Rooms[0].Enemies[0].Health,Is.EqualTo(40));
        }
        [Test] public void SchemaFiveWorldKeepsDamagedMeleeRosterOnUpgrade()
        {
            var world=new RunWorldSnapshot(new[]{new SavedPlayerPose(Vector2.Zero,Vector2.UnitX,0)},new[]{new SavedRoomState(1,new RunRoomId(0),new[]{new SavedEnemy(1,45,150,new Vector2(2,3),Vector2.UnitY)})});
            var save=new RunCheckpoint(42,1,new string('a',64),SavedSessionMode.Solo,new RunRoomId(0),new[]{new RunRoomId(0)},new[]{"weapon.knife"},world:world,difficulty:RunDifficulty.Hard);
            byte[] current=RunCheckpointCodec.Encode(save);int size=BitConverter.ToInt32(current,8);byte[] payload=new byte[size-12];
            // One enemy: remove its archetype before the 16-byte trailing difficulty/route/policy extension, and the final policy integer.
            Array.Copy(current,12,payload,0,size-24);Array.Copy(current,12+size-20,payload,size-24,12);
            byte[] old;using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))using(var hash=System.Security.Cryptography.SHA256.Create())
            {writer.Write(0x53435253);writer.Write(5);writer.Write(payload.Length);writer.Write(payload);writer.Write(hash.ComputeHash(payload));old=stream.ToArray();}
            var restored=RunCheckpointCodec.Decode(old);Assert.That(restored.EncounterVersion,Is.Zero);
            var enemy=restored.World.Rooms[0].Enemies[0];Assert.That(enemy.Archetype,Is.EqualTo(EnemyArchetype.Melee));Assert.That(enemy.Health,Is.EqualTo(45));Assert.That(enemy.Maximum,Is.EqualTo(150));
            var upgraded=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(restored));Assert.That(upgraded.EncounterVersion,Is.Zero);Assert.That(upgraded.World.Rooms[0].Enemies[0].Facing,Is.EqualTo(Vector2.UnitY));
        }
        [Test] public void SchemaFiveRetainsMeleePolicyAndArchivesOriginal()
        {
            var save=new RunCheckpoint(42,1,new string('a',64),SavedSessionMode.Solo,new RunRoomId(0),new[]{new RunRoomId(0)},new[]{"weapon.knife"},difficulty:RunDifficulty.Hard);
            byte[] current=RunCheckpointCodec.Encode(save);int length=BitConverter.ToInt32(current,8)-8;byte[] payload=new byte[length];Array.Copy(current,12,payload,0,length);
            byte[] old;using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))using(var hash=System.Security.Cryptography.SHA256.Create())
            {writer.Write(0x53435253);writer.Write(5);writer.Write(length);writer.Write(payload);writer.Write(hash.ComputeHash(payload));old=stream.ToArray();}
            var restored=RunCheckpointCodec.Decode(old);Assert.That(restored.EncounterVersion,Is.Zero);Assert.That(restored.Difficulty,Is.EqualTo(RunDifficulty.Hard));
            string path=System.IO.Path.Combine("Logs","Validation","Saves",Guid.NewGuid().ToString("N"),"run.save");System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));System.IO.File.WriteAllBytes(path,old);
            new RunCheckpointStore(path).Save(restored);Assert.That(System.IO.File.ReadAllBytes(path+".schema5"),Is.EqualTo(old));
        }
    }
}
