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
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
using CoreVector=System.Numerics.Vector2;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RunUpgradeIntegrationTests
    {
        private GameplaySession session;
        private ApplicationScreen screen;
        private RunCheckpoint saved;
        private ApplicationFlow flow;
        private bool blocked,blockNextLevel;
        private void Start(RunCheckpoint checkpoint=null,bool enabled=true,string classId="")
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab");session=Object.Instantiate(prefab);
            flow=new ApplicationFlow();flow.TryRequest(ApplicationCommand.Initialize);flow.CompleteLoading(ApplicationState.MainMenu);flow.TryRequest(ApplicationCommand.NewGame);flow.CompleteLoading(ApplicationState.Gameplay);
            session.PollLocalDevices=false;session.Bind(new PlayerIdentity(1),flow,42,true,checkpoint,c=>
            {if(blocked || blockNextLevel && c.LevelNumber==2)throw new IOException("Storage fixture");saved=c;},
                checkpoint==null?null:AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Knife.asset"),enableRunUpgrades:enabled,classId:classId);
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
        [UnityTearDown]public IEnumerator TearDown(){if(screen!=null)Object.Destroy(screen.gameObject);if(session!=null)Object.Destroy(session.gameObject);yield return null;}
        private void AttachScreen()
        {
            screen=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ApplicationScreen>("Assets/_Project/Prefabs/UI/GameplayScreen.prefab"));
            screen.Bind(flow);screen.BindRun(session);
        }
        private void Submit(string name)
        {
            var button=screen.View.Q<Button>(name);Assert.That(button.enabledInHierarchy,Is.True);
            using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
        }
        [UnityTest]public IEnumerator RunSummaryAndRepeatedUpgradePreviewSurviveContinue()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start(classId:"captain");
            session.DamagePlayer(Guid.NewGuid(),65);ReachExit();AttachScreen();yield return null;yield return null;
            Assert.That(screen.View.Q<Button>("upgrade-1").text,Does.Contain("35/100 HP → 55/120 HP"));
            Assert.That(screen.View.Q<Button>("upgrade-3").text,Does.Contain("25 → 30").And.Contain("Owned: 0/2"));
            Submit("upgrade-3");Assert.That(session.Upgrades.Choice(0),Is.Zero,"Selection alone cannot grant or confirm.");
            Assert.That(session.Life[0].Current,Is.EqualTo(35));
            screen.GetComponent<RunUpgradePanel>().ConsumePause();yield return null;yield return null;
            Assert.That(screen.View.Q<Label>("run-summary").text,Does.Contain("Captain").And.Contain("Level 1/3").And.Contain("35/100 HP").And.Contain("+0% base damage"));
            flow.TryRequest(ApplicationCommand.Resume);yield return null;yield return null;Submit("confirm-upgrade");
            yield return Until(()=>session.LevelNumber==2);flow.TryRequest(ApplicationCommand.Pause);yield return null;
            Assert.That(screen.View.Q<Label>("run-summary").text,Does.Contain("Weapon Calibration: Rank 1/2").And.Contain("+20% base damage"));
            session.SaveCurrent();var checkpoint=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(saved));
            Object.Destroy(screen.gameObject);Object.Destroy(session.gameObject);yield return null;Start(checkpoint);AttachScreen();
            flow.TryRequest(ApplicationCommand.Pause);yield return null;
            Assert.That(screen.View.Q<Label>("run-summary").text,Does.Contain("Captain").And.Contain("Weapon Calibration: Rank 1/2").And.Contain("+20% base damage"));
            flow.TryRequest(ApplicationCommand.Resume);ReachExit();yield return null;yield return null;
            Assert.That(screen.View.Q<Button>("upgrade-3").text,Does.Contain("Owned: 1/2").And.Contain("30 → 35"));
            Assert.That(session.Upgrades.Choice(0),Is.Zero);Assert.That(session.Combat.Simulation.EquippedWeapon.Damage,Is.EqualTo(25));
        }
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
                Assert.That(attack.Weapon.Damage,Is.EqualTo(level==1?30:35));Assert.That(session.Combat.Simulation.EquippedWeapon.Damage,Is.EqualTo(25));
            }
            var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/MeleeEnemy.prefab"));
            try
            {
                target.Initialize();Assert.That(target.Health.Maximum,Is.EqualTo(100));
                for(int hit=1;hit<=3;hit++)
                {
                    session.Combat.Simulation.AdvanceTime(1);
                    Assert.That(session.Combat.Simulation.TryAttack(new AttackIntent(new PlayerIdentity(1),CoreVector.UnitX),out var attack),Is.True);
                    target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),1000+hit,target.Identity,attack.Weapon.Damage),out _);
                    Assert.That(target.Health.Current>0,Is.EqualTo(hit<3));
                }
            }
            finally{Object.Destroy(target.gameObject);}
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
