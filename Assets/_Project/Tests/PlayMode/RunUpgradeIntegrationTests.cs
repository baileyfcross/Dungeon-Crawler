#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
using CoreVector=System.Numerics.Vector2;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RunUpgradeIntegrationTests
    {
        private GameplaySession session;
        private RunCheckpoint saved;
        private ApplicationFlow flow;
        private bool blocked,blockNextLevel;
        private void Start(RunCheckpoint checkpoint=null,bool enabled=true)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab");session=Object.Instantiate(prefab);
            flow=new ApplicationFlow();flow.TryRequest(ApplicationCommand.Initialize);flow.CompleteLoading(ApplicationState.MainMenu);flow.TryRequest(ApplicationCommand.NewGame);flow.CompleteLoading(ApplicationState.Gameplay);
            session.PollLocalDevices=false;session.Bind(new PlayerIdentity(1),flow,42,true,checkpoint,c=>
            {if(blocked || blockNextLevel && c.LevelNumber==2)throw new IOException("Storage fixture");saved=c;},
                checkpoint==null?null:AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Knife.asset"),enableRunUpgrades:enabled);
            session.Input.HandleFocus(true);
        }
        private void ReachExit()
        {
            int level=session.LevelNumber;
            while(session.LevelNumber==level && !session.Rooms.Progression.IsTerminal)
            {
                var edge=session.Run.Route(session.Rooms.Progression.CurrentRoomId,session.Run.TerminalRoomId)[0];
                var door=session.Rooms.CurrentRoom.ExitFor(edge,session.Rooms.Progression.Choices);
                Assert.That(door.IsLevelExit,Is.EqualTo(session.Run.GetRoom(edge.To).IsTerminal));
                Assert.That(session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),edge.From,edge.To)),Is.True);
            }
        }
        private static IEnumerator Until(Func<bool> condition)
        {float end=Time.realtimeSinceStartup+8;while(!condition()){Assert.That(Time.realtimeSinceStartup,Is.LessThan(end));yield return null;}yield return null;}
        [UnityTearDown]public IEnumerator TearDown(){if(session!=null)Object.Destroy(session.gameObject);yield return null;}
        [UnityTest]public IEnumerator HealthUpgradeWaitsForStorageAndContinueDoesNotHealAgain()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start();
            session.DamagePlayer(Guid.NewGuid(),65);ReachExit();
            Assert.That(session.LevelNumber,Is.EqualTo(1));Assert.That(session.UpgradePending,Is.True);Assert.That(session.Input.GameplayInputEnabled,Is.False);
            Assert.That(session.Combat.RequestAttack(new AttackIntent(new PlayerIdentity(1),CoreVector.UnitX)),Is.False);
            Assert.That(session.DamagePlayer(Guid.NewGuid(),1),Is.False);
            blocked=true;Assert.That(session.SelectUpgrade(1),Is.True);yield return new WaitForSecondsRealtime(.65f);
            Assert.That(session.Life[0].Current,Is.EqualTo(35));Assert.That(session.LevelNumber,Is.EqualTo(1));Assert.That(session.HealthFailure,Is.Not.Null);
            blocked=false;yield return Until(()=>session.LevelNumber==2 && session.Rooms.CurrentRoom!=null);
            Assert.That(session.Life[0].Current,Is.EqualTo(55));Assert.That(session.Life[0].Maximum,Is.EqualTo(120));Assert.That(session.Upgrades.Rank(0,RunUpgrade.ReinforcedSuit),Is.EqualTo(1));
            var checkpoint=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(saved));Object.Destroy(session.gameObject);yield return null;Start(checkpoint);
            Assert.That(session.Life[0].Current,Is.EqualTo(55));Assert.That(session.Life[0].Maximum,Is.EqualTo(120));Assert.That(session.UpgradePending,Is.False);
        }
        [UnityTest]public IEnumerator InterruptedLevelLoadRestoresDurableGrantWithoutApplyingHealthTwice()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start();session.DamagePlayer(Guid.NewGuid(),65);ReachExit();
            blockNextLevel=true;session.SelectUpgrade(1);yield return Until(()=>session.LevelNumber==2 && session.Rooms.CurrentRoom==null);
            Assert.That(saved.LevelNumber,Is.EqualTo(1));Assert.That(saved.Upgrades.CanAdvance,Is.True);Assert.That(saved.Life.Players[0].Current,Is.EqualTo(55));
            var checkpoint=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(saved));Object.Destroy(session.gameObject);yield return null;blockNextLevel=false;Start(checkpoint);
            Assert.That(session.LevelNumber,Is.EqualTo(2));Assert.That(session.Life[0].Current,Is.EqualTo(55));Assert.That(session.Life[0].Maximum,Is.EqualTo(120));
        }
        [UnityTest]public IEnumerator RepeatedDamageUpgradeUsesBaseAndFinalLevelHasNoOffer()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start();
            for(int level=1;level<=2;level++)
            {
                ReachExit();Assert.That(session.UpgradePending,Is.True);Assert.That(session.SelectUpgrade(3),Is.True);Assert.That(session.SelectUpgrade(2),Is.False);
                yield return Until(()=>session.LevelNumber==level+1);
                session.Simulation.RequestAim(new AimIntent(new PlayerIdentity(1),CoreVector.UnitX));
                session.Combat.Simulation.AdvanceTime(1);
                Assert.That(session.Combat.Simulation.TryAttack(new AttackIntent(new PlayerIdentity(1),CoreVector.UnitX),out var attack),Is.True);
                Assert.That(attack.Weapon.Damage,Is.EqualTo(level==1?29:33));Assert.That(session.Combat.Simulation.EquippedWeapon.Damage,Is.EqualTo(25));
            }
            ReachExit();Assert.That(session.LevelNumber,Is.EqualTo(3));Assert.That(session.UpgradePending,Is.False);Assert.That(session.Life.Ended,Is.False);
        }
        [UnityTest]public IEnumerator LegacySaveRemainsUpgradeFreeEvenWhenNewRunsEnableUpgrades()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start(enabled:false);var checkpoint=saved;
            Object.Destroy(session.gameObject);yield return null;Start(checkpoint,enabled:true);Assert.That(session.Upgrades,Is.Null);
            ReachExit();Assert.That(session.LevelNumber,Is.EqualTo(2));Assert.That(session.UpgradePending,Is.False);
        }
    }
}
#endif
