#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RangedCombatTests
    {
        private AuthoredRoom room;private EnemyCombatSystem system;private RunLife life;private Vector2[] positions;
        private EnemyActor Enemy=>room.Encounter.Enemies[0];
        [UnitySetUp]public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity",LoadSceneMode.Single);
            room=Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Engine.prefab"));
            var anchor=new GameObject("EnemySpawn").transform;anchor.SetParent(room.ContentRoot);anchor.position=new Vector2(0,6);
            var prefab=AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/RangedEnemy.prefab");
            var definition=AssetDatabase.LoadAssetAtPath<RangedEnemyDefinition>("Assets/_Project/Data/Enemies/RangedEnemy.asset");
            room.InitializeEncounter(true,1,new[]{new EnemySpawn(1,prefab,anchor).AsRanged(prefab,definition)},RunDifficulty.Normal);
            life=new RunLife(RunLifeSnapshot.Fresh(2));positions=new[]{new Vector2(5,6),new Vector2(100,6)};
            system=new EnemyCombatSystem(room.transform,i=>positions[i],()=>life,(p,id,amount)=>life.TryDamage(p,id,amount));Physics2D.SyncTransforms();
        }
        [UnityTearDown]public IEnumerator TearDown(){system?.Dispose();if(room!=null)Object.Destroy(room.gameObject);yield return null;}
        private void Tick(int count,bool running=true){for(int i=0;i<count;i++)system.Advance(room,running,.02f);}
        [UnityTest]public IEnumerator ShotHasTravelTimeDamagesOnceAndNeverTargetsAnotherRoom()
        {
            Assert.That(Enemy.Target.Health.Maximum,Is.EqualTo(80));Assert.That(Enemy.Configuration.Damage,Is.EqualTo(10));
            Tick(40);Assert.That(system.ProjectileCount,Is.EqualTo(1));Assert.That(life[0].Current,Is.EqualTo(100));
            Tick(30);Assert.That(life[0].Current,Is.EqualTo(90));Assert.That(life[1].Current,Is.EqualTo(100));Assert.That(system.ProjectileCount,Is.Zero);
            Tick(10);Assert.That(life[0].Current,Is.EqualTo(90));yield return null;
        }
        [UnityTest]public IEnumerator DodgingLockedAimAvoidsShotAndPauseFreezesProjectile()
        {
            Tick(20);positions[0]=new Vector2(5,8);Tick(20);Assert.That(system.ProjectileCount,Is.EqualTo(1));
            var before=system.ProjectileSnapshots.Single().Position;Tick(80,false);Assert.That(system.ProjectileSnapshots.Single().Position,Is.EqualTo(before));
            Tick(40);Assert.That(life[0].Current,Is.EqualTo(100));yield return null;
        }
        [UnityTest]public IEnumerator CoverSweepsStopFastShotsAndLeavingRoomCancelsThem()
        {
            Tick(40);var cover=new GameObject("Cover").AddComponent<BoxCollider2D>();cover.transform.SetParent(room.ContentRoot);cover.transform.position=new Vector2(2,6);cover.size=new Vector2(.15f,3);Physics2D.SyncTransforms();
            system.Advance(room,true,1);Assert.That(life[0].Current,Is.EqualTo(100));Assert.That(system.ProjectileCount,Is.Zero);
            Object.DestroyImmediate(cover.gameObject);Physics2D.SyncTransforms();for(int i=0;i<180 && system.ProjectileCount==0;i++)Tick(1);Assert.That(system.ProjectileCount,Is.GreaterThan(0));
            system.RetainOccupiedRooms(Array.Empty<AuthoredRoom>());Assert.That(system.ProjectileCount,Is.Zero);yield return null;
        }
        [UnityTest]public IEnumerator ShotsIgnoreDownedPlayersAndEnemyHitShapes()
        {
            life.TryDamage(new PlayerIdentity(1),Guid.NewGuid(),100);positions[1]=new Vector2(5.8f,6);
            var friendly=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/MeleeEnemy.prefab"),room.ContentRoot);
            friendly.transform.position=new Vector2(2,6);friendly.Initialize();Physics2D.SyncTransforms();
            Tick(80);Assert.That(life[0].State,Is.EqualTo(PlayerLifeState.Downed));Assert.That(life[1].Current,Is.EqualTo(90));
            Assert.That(friendly.Health.Current,Is.EqualTo(100));Assert.That(Enemy.Target.Health.Current,Is.EqualTo(80));yield return null;
        }
        [UnityTest]public IEnumerator RangedRosterAndHealthRestoreWithoutChangingArchetype()
        {
            Assert.Throws<InvalidOperationException>(()=>Enemy.Restore(new SavedEnemy(1,55,80,new System.Numerics.Vector2(0,6),System.Numerics.Vector2.UnitY,EnemyArchetype.Melee)));
            Enemy.Restore(new SavedEnemy(1,55,80,new System.Numerics.Vector2(0,6),System.Numerics.Vector2.UnitY,EnemyArchetype.Ranged));
            Assert.That(Enemy.Target.Health.Current,Is.EqualTo(55));Assert.That(Enemy.Archetype,Is.EqualTo(EnemyArchetype.Ranged));Assert.That(Enemy.BehaviourState,Is.EqualTo(EnemyBehaviourState.Recovery));
            Tick(20);Assert.That(system.ProjectileCount,Is.Zero);yield return null;
        }
        [UnityTest]public IEnumerator AllAuthoredRoomsKeepSafeStartsAndSeededDifficultyCaps()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Project/Prefabs/Rooms"}))
            {
                var authored=AssetDatabase.LoadAssetAtPath<AuthoredRoom>(AssetDatabase.GUIDToAssetPath(guid));if(authored==null)continue;
                foreach(var difficulty in new[]{RunDifficulty.Easy,RunDifficulty.Normal,RunDifficulty.Hard})
                {
                    Assert.That(authored.SelectEncounter(difficulty,true,false,42,1).Count,Is.Zero);Assert.That(authored.SelectEncounter(difficulty,false,true,42,1).Count,Is.Zero);
                    for(ulong seed=0;seed<20;seed++)
                    {
                        var chosen=authored.SelectEncounter(difficulty,false,false,seed,1);int count=difficulty==RunDifficulty.Easy?2:difficulty==RunDifficulty.Normal?3:5;
                        Assert.That(chosen.Count,Is.EqualTo(count));Assert.That(chosen.Count(e=>e.Archetype==EnemyArchetype.Ranged),Is.LessThanOrEqualTo(difficulty==RunDifficulty.Easy?0:difficulty==RunDifficulty.Normal?1:2));
                        Assert.That(chosen.Select(e=>e.Identity),Is.EqualTo(authored.SelectEncounter(difficulty,false,false,seed,1).Select(e=>e.Identity)));
                        Assert.That(authored.SelectEncounter(difficulty,false,false,seed,0).All(e=>e.Archetype==EnemyArchetype.Melee),Is.True);
                    }
                }
            }
            yield return null;
        }
    }
}
#endif
