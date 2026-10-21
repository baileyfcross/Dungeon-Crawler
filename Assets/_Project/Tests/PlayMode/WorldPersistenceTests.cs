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
using Object = UnityEngine.Object;
using CoreVector = System.Numerics.Vector2;
namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class WorldPersistenceTests
    {
        private GameplaySession session;
        private RunCheckpoint saved;
        private ApplicationFlow Playing()
        { var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu); flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay); return flow; }
        private void Start(RunCheckpoint checkpoint = null)
        {
            session = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab"));
            session.PollLocalDevices = false;
            session.Bind(new PlayerIdentity(1),Playing(),42,true,checkpoint,p=>saved=p,
                checkpoint == null ? null : AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Knife.asset"));
            session.Input.HandleFocus(true);
        }
        [UnityTearDown] public IEnumerator TearDown() { if (session != null) Object.Destroy(session.gameObject); yield return null; }
        [UnityTest]
        public IEnumerator SoloRevisitContinueAndSafeFallbackPreserveSemanticState()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity"); Start();
            var route = SessionValidationDriver.RouteToEngine(session.Run); Assert.That(route,Is.Not.Null);
            foreach (var edge in route) { Go(edge); yield return null; }
            var roomId = session.Rooms.Progression.CurrentRoomId; var enemy = session.Rooms.CurrentRoom.Encounter.Enemies.Single();
            enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),1000,enemy.Target.Identity,25),out _);
            enemy.transform.position = new Vector3(0,7,0); session.SaveCurrent();
            var outgoing = session.Rooms.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal); Go(outgoing); yield return null;
            Go(session.Rooms.Progression.Choices.Single(e=>e.To.Equals(roomId))); yield return null;
            enemy = session.Rooms.CurrentRoom.Encounter.Enemies.Single(); Assert.That(enemy.Target.Health.Current,Is.EqualTo(75));
            Assert.That(Vector2.Distance(enemy.transform.position,new Vector2(0,7)),Is.LessThan(.05)); Assert.That(enemy.BehaviourState,Is.EqualTo(EnemyBehaviourState.Recovery));
            enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),1001,enemy.Target.Identity,100),out _);
            session.Motor.PlaceAt(new Vector2(6,3));
            yield return new WaitForSecondsRealtime(5.2f);
            Assert.That(saved.World.Players[0].Position,Is.EqualTo(new CoreVector(6,3)),"dirty position autosaved within five seconds");
            session.Motor.PlaceAt(new Vector2(3,2)); session.Simulation.RestoreAim(CoreVector.UnitY); session.DamagePlayer(Guid.NewGuid(),23);
            session.Combat.Simulation.RestoreCooldown(.1f); session.SaveCurrent(); var checkpoint = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(saved));
            Object.Destroy(session.gameObject); yield return null; Start(checkpoint); yield return null;
            Assert.That(session.Motor.Body.position,Is.EqualTo(new Vector2(3,2))); Assert.That(session.Life[0].Current,Is.EqualTo(77));
            Assert.That(session.Simulation.State.AimDirection,Is.EqualTo(CoreVector.UnitY)); Assert.That(session.Simulation.DesiredVelocity,Is.EqualTo(CoreVector.Zero));
            Assert.That(session.Rooms.CurrentRoom.Encounter.Enemies.Single().Target.Health.Current,Is.Zero); Assert.That(session.Combat.Projectiles,Is.Empty);
            Assert.That(session.Combat.Simulation.CooldownRemaining,Is.GreaterThan(0));
            // A finite but now obstructed/invalid location is recoverable, never teleported into walls.
            var world = new RunWorldSnapshot(new[] {new SavedPlayerPose(new CoreVector(999,999),CoreVector.UnitX,0)},checkpoint.World.Rooms);
            var recovered = new RunCheckpoint(checkpoint.Seed,checkpoint.GenerationVersion,checkpoint.Fingerprint,checkpoint.Mode,checkpoint.CurrentRoom,checkpoint.RoomVisits,checkpoint.EquipmentIds,
                checkpoint.RunSeed,checkpoint.SequenceVersion,checkpoint.LevelNumber,checkpoint.LevelSeeds,checkpoint.LevelFingerprints,checkpoint.CompletedRoutes,checkpoint.Life,world:world);
            Object.Destroy(session.gameObject); yield return null; LogAssert.Expect(LogType.Warning,new System.Text.RegularExpressions.Regex("safe position")); Start(recovered);
            Assert.That(session.Rooms.CurrentRoom.IsSafePosition(session.Motor.Body.position),Is.True); Assert.That(session.Persistence.RecoveryMessage,Is.Not.Empty);
        }
        [UnityTest]
        public IEnumerator ContinueInsideDoorRequiresDepartureBeforeAnotherTransition()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity"); Start();
            var edge = session.Rooms.Progression.Choices[0]; var source = session.Rooms.Progression.CurrentRoomId;
            var position = (Vector2)session.Rooms.CurrentRoom.ExitFor(edge,session.Rooms.Progression.Choices).transform.position;
            session.Motor.PlaceAt(position); session.SaveCurrent(); var checkpoint = saved;
            Object.Destroy(session.gameObject); yield return null; Start(checkpoint);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            Assert.That(session.LevelNumber,Is.EqualTo(1)); Assert.That(session.Rooms.Progression.CurrentRoomId,Is.EqualTo(source));
            Assert.That(session.Motor.Body.position,Is.EqualTo(position),"Continue keeps the exact doorway position without immediately travelling");
            session.Motor.PlaceAt(session.Rooms.CurrentRoom.Arrival(edge.FromPort));
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            session.Motor.PlaceAt(position);
            var deadline = Time.realtimeSinceStartup+3;
            while (session.LevelNumber == 1 && session.Rooms.Progression.CurrentRoomId.Equals(source))
            { Assert.That(Time.realtimeSinceStartup,Is.LessThan(deadline)); yield return null; }
            Assert.That(session.Rooms.Progression.CurrentRoomId,Is.EqualTo(edge.To));
        }
        private void Go(RoomConnection edge)
        {
            var target = session.Run.GetRoom(edge.To); Assert.That(target.IsTerminal,Is.False);
            Assert.That(session.Rooms.RequestExit(new ExitIntent(new PlayerIdentity(1),edge.From,edge.To)),Is.True);
            Assert.That(Vector2.Distance(session.Motor.Body.position,session.Rooms.CurrentRoom.Arrival(edge.ToPort)),Is.LessThan(.001));
        }
    }
}
#endif
