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
    public sealed class KillRewardIntegrationTests
    {
        private GameplaySession session;private RunCheckpoint saved;private bool storageUnavailable;
        private static ApplicationFlow Playing()
        {var f=new ApplicationFlow();f.TryRequest(ApplicationCommand.Initialize);f.CompleteLoading(ApplicationState.MainMenu);f.TryRequest(ApplicationCommand.NewGame);f.CompleteLoading(ApplicationState.Gameplay);return f;}
        private void Start(RunCheckpoint checkpoint=null,bool rewards=true)
        {
            session=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab"));session.PollLocalDevices=false;
            session.Bind(new PlayerIdentity(1),Playing(),42,true,checkpoint,p=>{if(storageUnavailable)throw new System.IO.IOException("Simulated unavailable storage");saved=p;},checkpoint==null?null:AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Knife.asset"),difficulty:RunDifficulty.Normal,enableKillRewards:rewards);session.Input.HandleFocus(true);
        }
        [UnityTearDown]public IEnumerator TearDown(){storageUnavailable=false;if(session!=null)Object.Destroy(session.gameObject);yield return null;}
        [UnityTest]public IEnumerator KillAutosaveContainsBothXpAndDefeatedEnemyAndContinueCannotRepeatReward()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start();
            var edge=session.Rooms.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);
            Assert.That(session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),edge.From,edge.To)),Is.True);yield return null;
            var actor=session.Rooms.CurrentRoom.Encounter.Enemies.First();int spawn=actor.Identity.Spawn;
            long expected=session.RewardPolicy.KillXp(1,actor.Archetype,RunDifficulty.Normal);
            Assert.That(actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),7001,actor.Target.Identity,actor.Target.InitialHealth),out _),Is.True);
            Assert.That(session.Life.RunXp,Is.EqualTo(expected));Assert.That(saved.Life.RunXp,Is.EqualTo(expected));
            Assert.That(saved.World.Rooms.Single(r=>r.Level==1 && r.Room.Equals(edge.To)).Enemies.Single(e=>e.Spawn==spawn).Health,Is.Zero,"No saved dead enemy can exist without its XP.");
            var checkpoint=RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(saved));Object.Destroy(session.gameObject);yield return null;Start(checkpoint);yield return null;
            Assert.That(session.Life.RunXp,Is.EqualTo(expected));var restored=session.Rooms.CurrentRoom.Encounter.Enemies.Single(a=>a.Identity.Spawn==spawn);
            Assert.That(restored.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),7002,restored.Target.Identity,999),out _),Is.False);Assert.That(session.Life.RunXp,Is.EqualTo(expected));
            var next=session.Rooms.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),next.From,next.To));yield return null;
            var reverse=session.Rooms.Progression.Choices.Single(e=>e.To.Equals(edge.To));session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),reverse.From,reverse.To));yield return null;
            Assert.That(session.Life.RunXp,Is.EqualTo(expected));Assert.That(session.Rooms.CurrentRoom.Encounter.Enemies.Single(a=>a.Identity.Spawn==spawn).Lifecycle,Is.EqualTo(EnemyLifecycle.Dead));
        }
        [UnityTest]public IEnumerator StorageRetryCannotWriteDeathWithoutXpOrCreditTheKillTwice()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start();
            var edge=session.Rooms.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),edge.From,edge.To));yield return null;
            var before=saved;var actor=session.Rooms.CurrentRoom.Encounter.Enemies.First();long expected=session.RewardPolicy.KillXp(1,actor.Archetype,session.Difficulty);
            storageUnavailable=true;actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),7991,actor.Target.Identity,999),out _);
            Assert.That(saved,Is.SameAs(before));Assert.That(session.Life.RunXp,Is.EqualTo(expected));Assert.That(session.HealthFailure,Is.Not.Null);
            storageUnavailable=false;session.SaveCurrent();Assert.That(saved.Life.RunXp,Is.EqualTo(expected));Assert.That(saved.World.Rooms.Single(r=>r.Room.Equals(edge.To)).Enemies.Single(e=>e.Spawn==actor.Identity.Spawn).Health,Is.Zero);
            actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),7992,actor.Target.Identity,999),out _);session.SaveCurrent();Assert.That(saved.Life.RunXp,Is.EqualTo(expected));
        }
        [UnityTest]public IEnumerator OlderDisabledRunRemainsDisabledWhenContinueIsEnabledForNewRuns()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");Start(rewards:false);session.SaveCurrent();var old=saved;
            Object.Destroy(session.gameObject);yield return null;Start(old,rewards:true);Assert.That(session.RewardPolicy,Is.Null);
            var edge=session.Rooms.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),edge.From,edge.To));yield return null;
            var actor=session.Rooms.CurrentRoom.Encounter.Enemies.First();actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),7001,actor.Target.Identity,999),out _);
            Assert.That(session.Life.RunXp,Is.Zero);Assert.That(saved.Rewards,Is.Null);
        }
    }
}
#endif
