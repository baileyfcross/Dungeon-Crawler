#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CoreVector = System.Numerics.Vector2;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class EnemyCombatTests
    {
        private GameplaySession session;
        private GameObject content;
        private RoomEncounter encounter;
        private DamageableTarget Prefab => AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/DamageableTestTarget.prefab");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity", LoadSceneMode.Single);
            content = new GameObject("Encounter");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            encounter?.Retire();
            if (session != null) Object.Destroy(session.gameObject);
            if (content != null) Object.Destroy(content);
            yield return null;
        }

        private EnemySpawn Spawn(int id = 1)
        {
            var anchor = new GameObject("EnemySpawn").transform;
            anchor.SetParent(content.transform); anchor.position = Vector3.right;
            return new EnemySpawn(id, Prefab, anchor);
        }

        private IEnumerator StartCombat(string weapon)
        {
            session = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>("Assets/_Project/Prefabs/Gameplay/PlayerTestEnvironment.prefab"));
            var data = new SerializedObject(session);
            data.FindProperty("startingWeapon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"Assets/_Project/Data/Weapons/{weapon}.asset");
            data.ApplyModifiedPropertiesWithoutUndo();
            var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay);
            session.PollLocalDevices = false; session.Bind(new PlayerIdentity(1), flow);
            session.Input.HandleFocus(true);
            session.Combat.ClearRoom();
            encounter = new RoomEncounter(content.transform, new[] { Spawn() }, true, 1);
            session.Combat.RegisterRoomTargets(new[] { encounter.Enemies[0].Target });
            Physics2D.SyncTransforms(); yield return null;
        }

        private void Attack()
        {
            session.Simulation.RequestAim(new AimIntent(new PlayerIdentity(1), CoreVector.UnitX));
            Assert.That(session.Combat.RequestAttack(new AttackIntent(new PlayerIdentity(1), CoreVector.UnitX)), Is.True);
        }
        [UnityTest]
        public IEnumerator ReplicatedDamageFlashesOnlyOnHealthLossAndResetsAfterFeedback()
        {
            var target=Object.Instantiate(Prefab,content.transform);
            var presentation=target.GetComponentInChildren<TargetPresentation>();
            var data=new SerializedObject(presentation);
            var sprite=(SpriteRenderer)data.FindProperty("targetSprite").objectReferenceValue;
            Color original=sprite.color;
            target.PresentAuthoritativeHealth(100,100);yield return null;
            Assert.That(sprite.color,Is.EqualTo(original));
            target.PresentAuthoritativeHealth(75,100);yield return null;
            Assert.That(sprite.color,Is.EqualTo(Color.white));
            target.PresentAuthoritativeHealth(75,100);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(sprite.color,Is.EqualTo(original),"Repeated snapshots cannot extend the hit flash.");
            target.PresentAuthoritativeHealth(0,100);yield return null;
            Assert.That(sprite.color,Is.Not.EqualTo(Color.white));
            Assert.That(target.Health,Is.Null,"Presentation cannot create authority.");
        }

        [UnityTest]
        public IEnumerator MeleeUsesExistingResolverOnceAcrossCompoundShapesAndDeathIsFinal()
        {
            yield return StartCombat("Knife");
            var enemy = encounter.Enemies[0];
            Assert.That(enemy.Target.HitColliders.Length, Is.GreaterThan(1));
            Attack(); Assert.That(enemy.Target.Health.Current, Is.EqualTo(75));
            var duplicate = new DamageRequest(new PlayerIdentity(1), 1, enemy.Target.Identity, 25);
            Assert.That(enemy.Target.ReceiveDamage(duplicate, out _), Is.False);
            for (var i = 0; i < 3; i++) { session.Combat.Simulation.AdvanceTime(1); Attack(); }
            Assert.That(enemy.DeathEvents, Is.EqualTo(1)); Assert.That(enemy.Lifecycle, Is.EqualTo(EnemyLifecycle.Dead));
            foreach (var shape in enemy.Target.HitColliders) Assert.That(shape.enabled, Is.False);
            session.Combat.Simulation.AdvanceTime(1); Attack();
            Assert.That(enemy.DeathEvents, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ProjectileHitsEnemyAndRetirementCancelsRoomProjectilesAndRejectsOldReferences()
        {
            yield return StartCombat("Blaster");
            var enemy = encounter.Enemies[0]; Attack();
            var deadline = Time.realtimeSinceStartup + 5;
            while (enemy.Target.Health.Current == 100)
            { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            Assert.That(enemy.Target.Health.Current, Is.EqualTo(80));
            session.Combat.Simulation.AdvanceTime(1); Attack();
            Assert.That(session.Combat.Projectiles.Count, Is.GreaterThan(0));
            session.Combat.ClearRoom(); encounter.Retire();
            Assert.That(session.Combat.Projectiles.Count, Is.Zero);
            Assert.That(session.Combat.RegisteredColliderCount, Is.Zero);
            Assert.That(enemy.Lifecycle, Is.EqualTo(EnemyLifecycle.Despawned));
            Assert.That(enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1), 9, enemy.Target.Identity, 100), out _), Is.False);
            Assert.That(enemy.Target.Health.Current, Is.EqualTo(80));
            var fresh = new RoomEncounter(content.transform, new[] { Spawn() }, true, 2);
            try
            {
                Assert.That(fresh.Enemies[0].Target.Health.Current, Is.EqualTo(100));
                Assert.That(fresh.Enemies[0].Identity, Is.Not.EqualTo(enemy.Identity));
                Assert.That(enemy.Simulation.TryApplyDamage(fresh.Enemies[0].Identity, CombatFaction.Player,
                    new DamageRequest(new PlayerIdentity(1), 10, enemy.Target.Identity, 100), out _), Is.False);
            }
            finally { fresh.Retire(); }
        }

        [UnityTest]
        public IEnumerator ReplicaRejectsDamageStaleSnapshotsResurrectionAndDuplicateDeathEvents()
        {
            encounter = new RoomEncounter(content.transform, new[] { Spawn() }, false, 7);
            var enemy = encounter.Enemies[0]; var identity = enemy.Identity;
            Assert.That(enemy.Simulation, Is.Null); Assert.That(enemy.Target.Health, Is.Null);
            Assert.That(enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1), 1, enemy.Target.Identity, 100), out _), Is.False);
            Assert.That(enemy.ApplySnapshot(new EnemyIdentity(6, 1), EnemyLifecycle.Dead, 0, 100, 2, 0), Is.False);
            Assert.That(enemy.ApplySnapshot(identity, EnemyLifecycle.Dead, 0, 100, 3, 0), Is.True);
            Assert.That(enemy.ApplySnapshot(identity, EnemyLifecycle.Alive, 100, 100, 2, 0), Is.False);
            Assert.That(enemy.ApplySnapshot(identity, EnemyLifecycle.Dead, 0, 100, 3, 0), Is.False);
            Assert.Throws<InvalidOperationException>(() => enemy.ApplySnapshot(identity, EnemyLifecycle.Alive, 100, 100, 4, 0));
            Assert.That(enemy.ApplySnapshot(identity, EnemyLifecycle.Despawned, 0, 100, 5, 0), Is.True);
            Assert.That(enemy.DeathEvents, Is.EqualTo(1)); Assert.That(enemy.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredSpawnSlotsJoinRoomCombatRegistryAndRetireWithTheRoom()
        {
            var room = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Starting.prefab"), content.transform);
            var anchor = new GameObject("EnemySpawn").transform;
            anchor.SetParent(room.ContentRoot); anchor.position = new Vector3(0, 5, 0);
            var data = new SerializedObject(room); var slots = data.FindProperty("enemySpawns"); slots.arraySize = 1;
            var slot = slots.GetArrayElementAtIndex(0);
            slot.FindPropertyRelative("identity").intValue = 1;
            slot.FindPropertyRelative("anchor").objectReferenceValue = anchor;
            slot.FindPropertyRelative("prefab").objectReferenceValue = Prefab;
            data.ApplyModifiedPropertiesWithoutUndo();
            room.ValidateAuthoring(); room.InitializeEncounter(true, 4);
            var enemy = room.Encounter.Enemies[0]; var model = enemy.Simulation;
            Assert.That(room.CombatTargets.Count, Is.EqualTo(room.Targets.Count + 1));
            Assert.That(room.CombatTargets, Has.Member(enemy.Target));
            Assert.That(room.Targets, Has.No.Member(enemy.Target));
            Assert.Throws<InvalidOperationException>(() => room.InitializeEncounter(true, 5));
            Object.Destroy(room.gameObject); yield return null;
            Assert.That(model.Lifecycle, Is.EqualTo(EnemyLifecycle.Despawned));
        }

        [UnityTest]
        public IEnumerator DuplicateSpawnIdsFailBeforeAnyActorsAreCreated()
        {
            var one = Spawn(); var two = Spawn(); var before = content.transform.childCount;
            Assert.Throws<ArgumentException>(() => new RoomEncounter(content.transform, new[] { one, two }, true, 1));
            Assert.That(content.transform.childCount, Is.EqualTo(before));
            yield return null;
        }
    }
}
#endif
