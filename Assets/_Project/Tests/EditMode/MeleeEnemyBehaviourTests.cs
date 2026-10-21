using System;
using NUnit.Framework;
using SpaceCrawler.Core;
using V = System.Numerics.Vector2;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class MeleeEnemyBehaviourTests
    {
        private EnemySimulation enemy;
        private MeleeEnemyBehaviour brain;
        [SetUp] public void SetUp()
        { enemy = new EnemySimulation(new EnemyIdentity(1, 1), 100); brain = new MeleeEnemyBehaviour(enemy, new MeleeEnemyConfiguration(), V.Zero); }
        private static EnemyObservation Observe(int player, float x, float y = 0, bool alive = true, bool visible = true, bool reachable = true)
            => new EnemyObservation(new PlayerIdentity(player), new V(x, y), alive, visible, reachable);

        [Test] public void AcquiresClosestVisibleReachableLivingPlayerWithStableTieBreak()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(2, 3), Observe(1, -3) }, true);
            Assert.That(enemy.AggroTarget.Value.Value, Is.EqualTo(1));
            brain.Advance(.02f, V.Zero, new[] { Observe(2, .5f), Observe(1, -3) }, true);
            Assert.That(enemy.AggroTarget.Value.Value, Is.EqualTo(1));
        }
        [TestCase(false, true, true)] [TestCase(true, false, true)] [TestCase(true, true, false)]
        public void RejectsIneligibleAcquisition(bool alive, bool visible, bool reachable)
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 1, alive: alive, visible: visible, reachable: reachable) }, true);
            Assert.That(enemy.AggroTarget, Is.Null); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Idle));
        }
        [Test] public void RejectsTargetOutsideDetectionDistance()
        { brain.Advance(.02f, V.Zero, new[] { Observe(1, 8) }, true); Assert.That(enemy.AggroTarget, Is.Null); }
        [Test] public void LocksWindupDirectionAndDodgeMisses()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 1) }, true);
            Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Windup));
            brain.Advance(.61f, V.Zero, new[] { Observe(1, 0, 1) }, true);
            Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Strike)); Assert.That(brain.Facing, Is.EqualTo(V.UnitX));
            Assert.That(brain.StrikeTarget, Is.Null);
        }
        [Test] public void StrikeIsEmittedOnceThenRecoveryPreventsImmediateRepeat()
        {
            var observations = new[] { Observe(1, 1) };
            brain.Advance(.02f, V.Zero, observations, true); brain.Advance(.61f, V.Zero, observations, true);
            Assert.That(brain.StrikeTarget.Value.Value, Is.EqualTo(1)); Assert.That(brain.StrikeSequence, Is.EqualTo(1));
            brain.Advance(.02f, V.Zero, observations, true);
            Assert.That(brain.StrikeTarget, Is.Null); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Recovery));
            brain.Advance(.4f, V.Zero, observations, true); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Recovery));
            brain.Advance(.41f, V.Zero, observations, true); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Windup));
        }
        [Test] public void OcclusionAtStrikePreventsDamage()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 1) }, true);
            brain.Advance(.61f, V.Zero, new[] { Observe(1, 1, visible: false) }, true);
            Assert.That(brain.StrikeTarget, Is.Null);
        }
        [Test] public void DownedTargetCancelsWindupAndSwitchesToLivingPlayer()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 1), Observe(2, 4) }, true);
            brain.Advance(.61f, V.Zero, new[] { Observe(1, 1, alive: false), Observe(2, 4) }, true);
            Assert.That(enemy.AggroTarget.Value.Value, Is.EqualTo(2)); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Pursue));
            Assert.That(brain.StrikeSequence, Is.Zero);
        }
        [Test] public void LostSightUsesLastSeenPositionThenReturnsToSpawn()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 4) }, true);
            brain.Advance(1, V.UnitX, new[] { Observe(1, 6, visible: false) }, true);
            Assert.That(brain.Destination, Is.EqualTo(new V(4, 0)));
            brain.Advance(2, V.UnitX, new[] { Observe(1, 6, visible: false) }, true);
            Assert.That(enemy.AggroTarget, Is.Null); Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Return));
            Assert.That(brain.Destination, Is.EqualTo(V.Zero));
        }
        [Test] public void PauseFreezesWindupAndDeathCancelsIt()
        {
            brain.Advance(.02f, V.Zero, new[] { Observe(1, 1) }, true);
            brain.Advance(10, V.Zero, new[] { Observe(1, 1) }, false);
            Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Windup)); Assert.That(brain.StrikeSequence, Is.Zero);
            enemy.TryApplyDamage(enemy.Identity, CombatFaction.Player,
                new DamageRequest(new PlayerIdentity(1), 1, enemy.Identity.Damageable, 100), out _);
            brain.Advance(1, V.Zero, new[] { Observe(1, 1) }, true);
            Assert.That(brain.State, Is.EqualTo(EnemyBehaviourState.Dead)); Assert.That(brain.StrikeTarget, Is.Null);
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1)] [TestCase(0)]
        public void RejectsInvalidTuning(float value) => Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeEnemyConfiguration(speed: value));
    }
}
