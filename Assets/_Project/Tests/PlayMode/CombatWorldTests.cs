#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using CoreVector = System.Numerics.Vector2;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class CombatWorldTests
    {
        private GameplaySession session;
        private WeaponDefinition definition;
        private ApplicationFlow flow;
        private CombatResolver Combat => session.Combat;

        private IEnumerator StartFixture(string weapon, float lifetime = 2, float speed = 9)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Gameplay/PlayerTestEnvironment.prefab");
            session = Object.Instantiate(prefab).GetComponent<GameplaySession>();
            definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"Assets/_Project/Data/Weapons/{weapon}.asset"));
            var weaponData = new SerializedObject(definition);
            weaponData.FindProperty("projectileLifetime").floatValue = lifetime;
            weaponData.FindProperty("projectileSpeed").floatValue = speed;
            weaponData.ApplyModifiedPropertiesWithoutUndo();
            var sessionData = new SerializedObject(session);
            sessionData.FindProperty("startingWeapon").objectReferenceValue = definition;
            sessionData.ApplyModifiedPropertiesWithoutUndo();
            flow = new ApplicationFlow();
            flow.TryRequest(ApplicationCommand.Initialize);
            flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame);
            flow.CompleteLoading(ApplicationState.Gameplay);
            session.Bind(new PlayerIdentity(1), flow);
            session.Input.HandleFocus(true);
            yield return null;
            yield return null;
        }

        private bool Attack(CoreVector aim)
        {
            var player = session.Simulation;
            player.RequestAim(new AimIntent(player.State.Player, aim));
            return Combat.RequestAttack(new AttackIntent(player.State.Player, player.State.AimDirection));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (session != null) Object.Destroy(session.gameObject);
            if (definition != null) Object.Destroy(definition);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KnifeHitsForwardRangeOncePerTargetAndUsesAimRatherThanMovement()
        {
            yield return StartFixture("Knife");
            Assert.That(Combat.Targets[0].HitColliders.Length, Is.GreaterThan(1), "Exercise duplicate candidate shapes.");
            var player = session.Simulation;
            player.RequestMove(new MoveIntent(player.State.Player, CoreVector.UnitY));
            Assert.That(Attack(CoreVector.UnitX), Is.True);
            Assert.That(Combat.Targets[0].Health.Current, Is.EqualTo(75));
            Assert.That(Combat.Targets[1].Health.Current, Is.EqualTo(100), "Behind the attack.");
            Assert.That(Combat.Targets[2].Health.Current, Is.EqualTo(100), "Outside center range.");
            Assert.That(player.DesiredVelocity, Is.EqualTo(CoreVector.UnitY * 5));
            Assert.That(Attack(CoreVector.UnitX), Is.False);
            Assert.That(Combat.Targets[0].Health.Current, Is.EqualTo(75));
            Combat.Simulation.AdvanceTime(1);
            Assert.That(Attack(-CoreVector.UnitX), Is.True);
            Assert.That(Combat.Targets[1].Health.Current, Is.EqualTo(75));
            Assert.That(Combat.Targets[0].Health.Current, Is.EqualTo(75));
        }

        [UnityTest]
        public IEnumerator DepletedTargetDisablesHitShapesAndNeverTakesMoreDamage()
        {
            yield return StartFixture("Knife");
            for (var i = 0; i < 5; i++) { Assert.That(Attack(CoreVector.UnitX), Is.True); Combat.Simulation.AdvanceTime(1); }
            var target = Combat.Targets[0];
            Assert.That(target.Health.Current, Is.Zero);
            Assert.That(target.Health.IsAlive, Is.False);
            foreach (var collider in target.HitColliders) Assert.That(collider.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator BlasterKeepsAcceptedDirectionIgnoresOwnerAndStopsAtFirstTarget()
        {
            yield return StartFixture("Blaster");
            Assert.That(Attack(CoreVector.UnitX), Is.True);
            Assert.That(Combat.Projectiles.Count, Is.EqualTo(1));
            var projectile = Combat.Projectiles[0];
            session.Simulation.RequestAim(new AimIntent(session.Simulation.State.Player, CoreVector.UnitY));
            yield return new WaitForFixedUpdate();
            Assert.That(projectile != null, Is.True, "The projectile must ignore its owner's spawn overlap.");
            Assert.That(projectile.Position.x, Is.GreaterThan(0));
            Assert.That(Mathf.Abs(projectile.Position.y), Is.LessThan(0.001f));
            Assert.That(projectile.Execution.Attacker, Is.EqualTo(session.Simulation.State.Player));
            Assert.That(projectile.Execution.AimDirection, Is.EqualTo(CoreVector.UnitX));
            yield return WaitFor(() => Combat.Projectiles.Count == 0);
            Assert.That(projectile == null, Is.True);
            Assert.That(Combat.Targets[0].Health.Current, Is.EqualTo(80), "One impact despite multiple hit shapes.");
            Assert.That(Combat.Targets[2].Health.Current, Is.EqualTo(100), "No piercing into the next target.");
            Assert.That(Combat.Targets[1].Health.Current, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator MissedProjectileAndCooldownFreezeDuringPauseThenExpire()
        {
            yield return StartFixture("Blaster", 0.15f, 1);
            Assert.That(Attack(CoreVector.UnitY), Is.True);
            var projectile = Combat.Projectiles[0];
            flow.TryRequest(ApplicationCommand.Pause);
            var position = projectile.Position;
            var lifetime = projectile.RemainingLifetime;
            var cooldown = Combat.Simulation.CooldownRemaining;
            yield return new WaitForSeconds(0.25f);
            Assert.That(projectile.Position, Is.EqualTo(position));
            Assert.That(projectile.RemainingLifetime, Is.EqualTo(lifetime));
            Assert.That(Combat.Simulation.CooldownRemaining, Is.EqualTo(cooldown));
            Assert.That(Attack(CoreVector.UnitY), Is.False);
            flow.TryRequest(ApplicationCommand.Resume);
            yield return WaitFor(() => Combat.Projectiles.Count == 0);
            Assert.That(projectile == null, Is.True);
            foreach (var target in Combat.Targets) Assert.That(target.Health.Current, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator SweptProjectileStopsAtSolidBoundaryBeforeLifetime()
        {
            yield return StartFixture("Blaster", 2, 40);
            Assert.That(Attack(CoreVector.UnitY), Is.True);
            var projectile = Combat.Projectiles[0];
            yield return WaitFor(() => Combat.Projectiles.Count == 0);
            Assert.That(projectile == null, Is.True);
            Assert.That(projectile.RemainingLifetime, Is.GreaterThan(1), "The wall ends the shot with lifetime remaining, independently of test-runner frame timing.");
            foreach (var target in Combat.Targets) Assert.That(target.Health.Current, Is.EqualTo(100));
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 5;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            yield return null;
        }
    }
}
#endif
