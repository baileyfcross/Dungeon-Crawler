using System;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class CombatSimulationTests
    {
        private readonly PlayerIdentity identity = new PlayerIdentity(1);
        private PlayerSimulation player;
        private CombatSimulation combat;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerSimulation(identity, 5);
            player.SetActive(true);
            combat = new CombatSimulation(player, WeaponConfiguration.Melee("weapon.knife", 25, 0.5f, 2, 90));
        }
        private AttackIntent Intent => new AttackIntent(identity, player.State.AimDirection);

        [Test]
        public void AcceptedAttackCapturesCurrentAimOriginWeaponAndStartsCooldown()
        {
            player.RequestMove(new MoveIntent(identity, Vector2.UnitY));
            player.RequestAim(new AimIntent(identity, -Vector2.UnitX));
            player.RecordPhysics(identity, new Vector2(2, 3), Vector2.UnitY * 5);
            Assert.That(combat.TryAttack(Intent, out var execution), Is.True);
            Assert.That(execution.Attacker, Is.EqualTo(identity));
            Assert.That(execution.AimDirection, Is.EqualTo(-Vector2.UnitX));
            Assert.That(execution.Origin, Is.EqualTo(new Vector2(2, 3)));
            Assert.That(execution.Weapon.Id, Is.EqualTo("weapon.knife"));
            Assert.That(combat.CooldownRemaining, Is.EqualTo(0.5f));
            player.RequestAim(new AimIntent(identity, Vector2.UnitY));
            Assert.That(execution.AimDirection, Is.EqualTo(-Vector2.UnitX), "Accepted attacks do not track later aim.");
        }

        [Test]
        public void WrongPlayerInactiveAndUnequippedCannotAttack()
        {
            Assert.That(combat.TryAttack(new AttackIntent(new PlayerIdentity(2), Vector2.UnitX), out _), Is.False);
            player.SetActive(false);
            Assert.That(combat.TryAttack(Intent, out _), Is.False);
            player.SetActive(true);
            Assert.That(new CombatSimulation(player, null).TryAttack(Intent, out _), Is.False);
            Assert.That(combat.AcceptedAttackCount, Is.Zero);
            Assert.That(combat.CooldownRemaining, Is.Zero);
        }

        [Test]
        public void InvalidOrStaleAimCannotReplaceAuthoritativeAim()
        {
            foreach (var direction in new[] { Vector2.Zero, Vector2.UnitY, Vector2.UnitX * 2, new Vector2(float.NaN, 1), new Vector2(float.PositiveInfinity, 0) })
                Assert.That(combat.TryAttack(new AttackIntent(identity, direction), out _), Is.False);
            Assert.That(combat.AcceptedAttackCount, Is.Zero);
            Assert.That(player.State.AimDirection, Is.EqualTo(Vector2.UnitX));
        }

        [Test]
        public void CooldownRejectsRepeatsAndFreezesWhilePaused()
        {
            Assert.That(combat.TryAttack(Intent, out var first), Is.True);
            Assert.That(combat.TryAttack(Intent, out _), Is.False);
            combat.AdvanceTime(0.25f);
            player.SetActive(false);
            combat.AdvanceTime(10);
            Assert.That(combat.CooldownRemaining, Is.EqualTo(0.25f));
            player.SetActive(true);
            Assert.That(combat.TryAttack(Intent, out _), Is.False);
            combat.AdvanceTime(0.25f);
            Assert.That(combat.TryAttack(Intent, out var second), Is.True);
            Assert.That(second.Sequence, Is.GreaterThan(first.Sequence));
            Assert.Throws<ArgumentOutOfRangeException>(() => combat.AdvanceTime(float.NaN));
        }

        [Test]
        public void SameAttackIntentResolvesAccordingToEquippedConfiguration()
        {
            var blaster = new CombatSimulation(player, WeaponConfiguration.Projectile("weapon.blaster", 20, 0.3f, 9, 2));
            combat.TryAttack(Intent, out var knifeExecution);
            blaster.TryAttack(Intent, out var blasterExecution);
            Assert.That(knifeExecution.Weapon.Kind, Is.EqualTo(AttackKind.Melee));
            Assert.That(blasterExecution.Weapon.Kind, Is.EqualTo(AttackKind.Projectile));
            Assert.That(blasterExecution.AimDirection, Is.EqualTo(knifeExecution.AimDirection));
            Assert.That(blasterExecution.Weapon.Damage, Is.EqualTo(20));
            Assert.That(blasterExecution.Weapon.Cooldown, Is.EqualTo(0.3f));
        }

        [TestCase(1.5f, 0, true)] [TestCase(1, 1, true)] [TestCase(-1, 0, false)]
        [TestCase(2.1f, 0, false)] [TestCase(0, 1, false)] [TestCase(0, 0, true)]
        public void MeleeUsesConfiguredCenterRangeAndForwardArc(float x, float y, bool expected)
        {
            combat.TryAttack(Intent, out var execution);
            Assert.That(MeleeRules.Contains(execution, new Vector2(x, y)), Is.EqualTo(expected));
        }

        [Test]
        public void DamageIsIdentifiedClampedAndCannotCreateInvalidHealth()
        {
            var targetId = new DamageableIdentity(1);
            var health = new HealthSimulation(targetId, 100);
            Assert.That(health.TryApplyDamage(new DamageRequest(identity, 1, targetId, 25), out var result), Is.True);
            Assert.That(result.AppliedDamage, Is.EqualTo(25));
            Assert.That(result.RemainingHealth, Is.EqualTo(75));
            Assert.That(health.TryApplyDamage(new DamageRequest(identity, 2, new DamageableIdentity(2), 10), out _), Is.False);
            Assert.That(health.TryApplyDamage(new DamageRequest(identity, 2, targetId, -10), out _), Is.False);
            Assert.That(health.TryApplyDamage(new DamageRequest(default, 2, targetId, 10), out _), Is.False);
            Assert.That(health.Current, Is.EqualTo(75));
            Assert.That(health.TryApplyDamage(new DamageRequest(identity, 2, targetId, int.MaxValue), out result), Is.True);
            Assert.That(result.AppliedDamage, Is.EqualTo(75));
            Assert.That(health.Current, Is.Zero);
            Assert.That(health.IsAlive, Is.False);
            Assert.That(health.TryApplyDamage(new DamageRequest(identity, 3, targetId, 10), out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new HealthSimulation(targetId, 0));
        }

        [Test]
        public void InvalidWeaponTuningFailsBeforeGameplay()
        {
            Assert.Throws<ArgumentException>(() => WeaponConfiguration.Melee("", 1, 1, 1, 90));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponConfiguration.Melee("knife", 0, 1, 1, 90));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponConfiguration.Melee("knife", 1, 0, 1, 90));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponConfiguration.Melee("knife", 1, 1, 1, 361));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponConfiguration.Projectile("blaster", 1, 1, float.PositiveInfinity, 1));
        }
    }
}
