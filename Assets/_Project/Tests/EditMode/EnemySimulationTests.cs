using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class EnemySimulationTests
    {
        private static DamageRequest Hit(EnemySimulation enemy, int player = 1, long sequence = 1, int amount = 25)
            => new DamageRequest(new PlayerIdentity(player), sequence, enemy.Identity.Damageable, amount);

        [Test]
        public void IdentityUsesReservedRangeAndRoomIncarnation()
        {
            var one = new EnemyIdentity(1, 1); var two = new EnemyIdentity(2, 1);
            Assert.That(one, Is.Not.EqualTo(two));
            Assert.That(one.Damageable.Value, Is.EqualTo(0x40000001));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyIdentity(0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyIdentity(1, 33));
            Assert.Throws<System.ArgumentException>(() => new EnemySimulation(default, 100));
        }

        [Test]
        public void InvalidDamageDoesNotConsumeAttackAndFriendlyFireIsRejected()
        {
            var enemy = new EnemySimulation(new EnemyIdentity(1, 1), 100);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Enemy, Hit(enemy), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(new EnemyIdentity(2, 1), CombatFaction.Player, Hit(enemy), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, amount: 0), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, player: 3), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy), out var result), Is.True);
            Assert.That(result.AppliedDamage, Is.EqualTo(25));
        }

        [Test]
        public void DamageIsDeduplicatedPerPlayerWithoutRejectingOutOfOrderProjectileHits()
        {
            var enemy = new EnemySimulation(new EnemyIdentity(1, 1), 100);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, sequence: 3), out _), Is.True);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, sequence: 3), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, sequence: 2), out _), Is.True);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, sequence: 2), out _), Is.False);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, player: 2, sequence: 3), out _), Is.True);
            Assert.That(enemy.Health.Current, Is.EqualTo(25));
        }

        [Test]
        public void DeathOccursOnceClearsTargetAndRetirementRejectsAllActions()
        {
            var enemy = new EnemySimulation(new EnemyIdentity(1, 1), 100); var deaths = 0;
            enemy.Died += () => deaths++;
            Assert.That(enemy.TrySetAggroTarget(new PlayerIdentity(2)), Is.True);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, amount: 200), out var result), Is.True);
            Assert.That(result.AppliedDamage, Is.EqualTo(100)); Assert.That(enemy.Lifecycle, Is.EqualTo(EnemyLifecycle.Dead));
            Assert.That(enemy.AggroTarget, Is.Null); Assert.That(deaths, Is.EqualTo(1));
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy, sequence: 2), out _), Is.False);
            Assert.That(enemy.TrySetAggroTarget(new PlayerIdentity(1)), Is.False);
            Assert.That(enemy.Despawn(), Is.True); Assert.That(enemy.Despawn(), Is.False);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void LiveDespawnDoesNotReportDeathAndRejectsStaleDamage()
        {
            var enemy = new EnemySimulation(new EnemyIdentity(1, 1), 100); var deaths = 0;
            enemy.Died += () => deaths++;
            Assert.That(enemy.TrySetAggroTarget(new PlayerIdentity(3)), Is.False);
            Assert.That(enemy.Despawn(), Is.True);
            Assert.That(enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player, Hit(enemy), out _), Is.False);
            Assert.That(enemy.Health.Current, Is.EqualTo(100)); Assert.That(deaths, Is.Zero);
        }
    }
}
