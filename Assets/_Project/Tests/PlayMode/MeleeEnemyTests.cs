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

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class MeleeEnemyTests
    {
        private AuthoredRoom room;
        private EnemyCombatSystem system;
        private RunLife life;
        private Vector2[] positions;
        private EnemyActor Enemy => room.Encounter.Enemies[0];
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity", LoadSceneMode.Single);
            room = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Engine.prefab"));
            room.InitializeEncounter(true, 1); life = new RunLife(RunLifeSnapshot.Fresh(2));
            positions = new[] { new Vector2(0, 1), new Vector2(8, 0) };
            system = new EnemyCombatSystem(room.transform, i => positions[i], () => life, (player, attack, amount) => life.TryDamage(player, attack, amount));
        }
        [UnityTearDown] public IEnumerator TearDown()
        { system?.Dispose(); if (room != null) Object.Destroy(room.gameObject); yield return null; }
        private void Tick(int count, bool running = true) { for (var i = 0; i < count; i++) system.Advance(room, running, .02f); }

        [UnityTest] public IEnumerator PursuesStrikesOnceAndIgnoresDownedPlayer()
        {
            var start = Enemy.transform.position; Tick(45);
            Assert.That(Vector3.Distance(Enemy.transform.position, start), Is.GreaterThan(1));
            Tick(75); Assert.That(life[0].Current, Is.EqualTo(80)); Assert.That(life[1].Current, Is.EqualTo(100));
            life.TryDamage(new PlayerIdentity(1), Guid.NewGuid(), 80); positions[1] = new Vector2(3, 3);
            Tick(1); Assert.That(Enemy.AggroPlayer, Is.EqualTo(2));
            Assert.That(life[0].State, Is.EqualTo(PlayerLifeState.Downed));
            Assert.That(life.RunXp, Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator PauseAndDeathDuringWindupPreventDamageAndFreshRoomResetsEnemy()
        {
            positions[0] = new Vector2(0, 5); Tick(1);
            Assert.That(Enemy.BehaviourState, Is.EqualTo(EnemyBehaviourState.Windup));
            Tick(100, false); Assert.That(life[0].Current, Is.EqualTo(100));
            Enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1), 1, Enemy.Target.Identity, 100), out _);
            Tick(100); Assert.That(life[0].Current, Is.EqualTo(100)); Assert.That(Enemy.DeathEvents, Is.EqualTo(1));
            var model = Enemy.Simulation; var fresh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Engine.prefab"));
            try { fresh.InitializeEncounter(true, 2); Assert.That(fresh.Encounter.Enemies[0].Target.Health.Current, Is.EqualTo(100)); }
            finally { Object.Destroy(fresh.gameObject); }
            room.RetireEncounter(); Tick(1); Assert.That(model.Lifecycle, Is.EqualTo(EnemyLifecycle.Despawned));
            yield return null;
        }
        [UnityTest] public IEnumerator MissingRoomAfterTeardownCannotContinueAnAttack()
        {
            positions[0] = new Vector2(0, 5); Tick(1);
            Assert.That(Enemy.BehaviourState, Is.EqualTo(EnemyBehaviourState.Windup));
            var model = Enemy.Simulation; Object.Destroy(room.gameObject); yield return null;
            Assert.That(model.Lifecycle, Is.EqualTo(EnemyLifecycle.Despawned));
            Assert.DoesNotThrow(() => system.Advance(null, true, 1));
            Assert.That(life[0].Current, Is.EqualTo(100));
        }

        [UnityTest] public IEnumerator GridRoutesAroundCoverWithBodyClearanceAndRejectsDisconnectedAreas()
        {
            var cover = new GameObject("Cover").AddComponent<BoxCollider2D>(); cover.transform.SetParent(room.ContentRoot); cover.transform.position = new Vector2(0, 6); cover.size = new Vector2(1, 4);
            using (var navigation = new RoomNavigation(room.transform))
            {
                navigation.Build(room); var path = navigation.FindPath(new Vector2(-4, 6), new Vector2(4, 6));
                Assert.That(path, Is.Not.Null); Assert.That(path.Any(p => Mathf.Abs(p.y - 6) > 2), Is.True);
                foreach (var point in path) Assert.That(RoomNavigation.Blocked(point, RoomNavigation.BodyRadius), Is.False);
                var stopped = RoomNavigation.Move(new Vector2(-1, 6), new Vector2(1, 6), 3);
                Assert.That(stopped.x, Is.LessThan(-.89f));
                cover.size = new Vector2(1, 30); navigation.Build(room);
                Assert.That(navigation.Reachable(new Vector2(-4, 6), new Vector2(4, 6)), Is.False);
                Assert.That(navigation.FindPath(new Vector2(-4, 6), new Vector2(4, 6)), Is.Null);
            }
            yield return null;
        }
        [UnityTest] public IEnumerator SolidCoverBlocksPlayerKnifeAndEnemyStrike()
        {
            positions[0] = new Vector2(0, 5); Tick(1);
            var cover = new GameObject("Cover").AddComponent<BoxCollider2D>(); cover.transform.SetParent(room.ContentRoot);
            cover.transform.position = new Vector2(0, 5.5f); cover.size = new Vector2(2, .15f); Physics2D.SyncTransforms();
            Tick(32); Assert.That(life[0].Current, Is.EqualTo(100));
            var session = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/PlayerTestEnvironment.prefab"));
            try
            {
                var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
                flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay);
                session.PollLocalDevices = false; session.Bind(new PlayerIdentity(1), flow); session.Input.HandleFocus(true);
                session.Combat.ClearRoom(); session.Combat.RegisterRoomTargets(new[] { Enemy.Target });
                session.Motor.PlaceAt(positions[0]); session.Motor.CaptureState(); Physics2D.SyncTransforms();
                Assert.That(session.Simulation.RequestAim(new AimIntent(new PlayerIdentity(1), System.Numerics.Vector2.UnitY)), Is.True);
                Assert.That(session.Combat.RequestAttack(new AttackIntent(new PlayerIdentity(1), System.Numerics.Vector2.UnitY)), Is.True);
                Assert.That(Enemy.Target.Health.Current, Is.EqualTo(100));
                Object.DestroyImmediate(cover.gameObject); Physics2D.SyncTransforms(); session.Combat.Simulation.AdvanceTime(1);
                Assert.That(session.Combat.RequestAttack(new AttackIntent(new PlayerIdentity(1), System.Numerics.Vector2.UnitY)), Is.True);
                Assert.That(Enemy.Target.Health.Current, Is.EqualTo(75));
            }
            finally { Object.Destroy(session.gameObject); }
            yield return null;
        }
    }
}
#endif
