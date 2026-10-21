using System;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunLifeTests
    {
        private static readonly Vector2[] Near = { Vector2.Zero, Vector2.UnitX };
        private static readonly bool[] Available = { true, true };
        private static RunLife Party() => new RunLife(RunLifeSnapshot.Fresh(2));
        private static void Down(RunLife life, int player) => Assert.That(life.TryDamage(new PlayerIdentity(player), Guid.NewGuid(), 1000000), Is.True);

        [TestCase(0)] [TestCase(3)] public void NewRunRejectsInvalidPlayerCounts(int players)
            => Assert.Throws<ArgumentOutOfRangeException>(() => RunLifeSnapshot.Fresh(players));

        [Test]
        public void SoloDamageIsIdentifiedAndZeroHealthEndsRunWithoutAllowingRevival()
        {
            var life = new RunLife(RunLifeSnapshot.Fresh(1)); var id = Guid.NewGuid();
            Assert.That(life[0].Current, Is.EqualTo(100)); Assert.That(life[0].Maximum, Is.EqualTo(100));
            Assert.That(life.TryDamage(new PlayerIdentity(1), id, 20), Is.True);
            Assert.That(life.TryDamage(new PlayerIdentity(1), id, 20), Is.False); Assert.That(life[0].Current, Is.EqualTo(80));
            Down(life, 1); Assert.That(life.Ended, Is.True); Assert.That(life[0].State, Is.EqualTo(PlayerLifeState.Dead));
            Assert.That(life.TrySetMaximum(new PlayerIdentity(1), 200), Is.False);
        }
        [Test]
        public void ThreeContinuousSecondsReviveAtHalfMaximumRoundedDown()
        {
            var life = Party(); life.TrySetMaximum(new PlayerIdentity(2), 101); Down(life, 2);
            Assert.That(life.Ended, Is.False);
            life.SetReviveHeld(new PlayerIdentity(1), false); life.SetReviveHeld(new PlayerIdentity(1), true);
            life.AdvanceRevival(2.99f, Near, Available); Assert.That(life[1].State, Is.EqualTo(PlayerLifeState.Downed));
            life.AdvanceRevival(.01f, Near, Available);
            Assert.That(life[1].Current, Is.EqualTo(50)); Assert.That(life[1].Maximum, Is.EqualTo(101));
            Assert.That(life[1].State, Is.EqualTo(PlayerLifeState.Alive)); Assert.That(life.ReviveProgress(0), Is.Zero);
        }
        [TestCase("release")]
        [TestCase("distance")]
        [TestCase("damage")]
        [TestCase("inactive")]
        public void InterruptedReviveCannotRetainPartialProgress(string interruption)
        {
            var life = Party(); Down(life, 2);
            life.SetReviveHeld(new PlayerIdentity(1), false); life.SetReviveHeld(new PlayerIdentity(1), true);
            life.AdvanceRevival(2, Near, Available);
            if (interruption == "release") life.SetReviveHeld(new PlayerIdentity(1), false);
            if (interruption == "distance") life.AdvanceRevival(.1f, new[] { Vector2.Zero, new Vector2(3, 0) }, Available);
            if (interruption == "damage") life.TryDamage(new PlayerIdentity(1), Guid.NewGuid(), 1);
            if (interruption == "inactive") life.AdvanceRevival(.1f, Near, new[] { false, true });
            Assert.That(life.ReviveProgress(0), Is.Zero);
            life.SetReviveHeld(new PlayerIdentity(1), false); life.SetReviveHeld(new PlayerIdentity(1), true);
            life.AdvanceRevival(1, Near, Available); Assert.That(life[1].State, Is.EqualTo(PlayerLifeState.Downed));
        }
        [Test]
        public void DownedPlayerDoesNotBleedOutAndPartyLossRejectsFurtherActions()
        {
            var life = Party(); Down(life, 1); life.AdvanceRevival(100000, Near, Available);
            Assert.That(life.Ended, Is.False); Assert.That(life[0].State, Is.EqualTo(PlayerLifeState.Downed));
            Assert.That(life.TryDamage(new PlayerIdentity(1), Guid.NewGuid(), 10), Is.False);
            Down(life, 2); Assert.That(life.Ended, Is.True);
            life.SetReviveHeld(new PlayerIdentity(1), true); life.AdvanceRevival(10, Near, Available);
            Assert.That(life[0].Current, Is.Zero); Assert.That(life.TryAwardRunXp(10), Is.False);
        }
        [Test]
        public void OneAreaAttackCanDamageBothPlayersButCannotDamageEitherTwice()
        {
            var life = Party(); var attack = Guid.NewGuid();
            Assert.That(life.TryDamage(new PlayerIdentity(1), attack, 10), Is.True);
            Assert.That(life.TryDamage(new PlayerIdentity(2), attack, 10), Is.True);
            Assert.That(life.TryDamage(new PlayerIdentity(1), attack, 10), Is.False);
            Assert.That(life.TryDamage(new PlayerIdentity(2), attack, 10), Is.False);
        }
        [Test]
        public void MaximumChangesClampHealthWithoutHealingAndLifeGateOverridesInputActivation()
        {
            var life = Party(); life.TrySetMaximum(new PlayerIdentity(1), 50); life.TrySetMaximum(new PlayerIdentity(1), 200);
            Assert.That(life[0].Current, Is.EqualTo(50)); Assert.That(life[0].Maximum, Is.EqualTo(200));
            var player = new PlayerSimulation(new PlayerIdentity(1), 5); player.SetActive(true);
            player.RequestMove(new MoveIntent(new PlayerIdentity(1), Vector2.UnitX)); player.SetLifeActive(false); player.SetActive(true);
            Assert.That(player.IsActive, Is.False); Assert.That(player.DesiredVelocity, Is.EqualTo(Vector2.Zero));
            Assert.That(player.RequestMove(new MoveIntent(new PlayerIdentity(1), Vector2.UnitX)), Is.False);
        }
        [Test]
        public void SnapshotRetainsRunIdentityXpVitalsProfileAndFutureClassId()
        {
            var id = Guid.NewGuid(); var profiles = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var life = new RunLife(new RunLifeSnapshot(id, 123, new[] { new PlayerVitals(67, 120, PlayerLifeState.Alive), new PlayerVitals(0, 100, PlayerLifeState.Downed) }, profiles, new[] { "", "future.class" }));
            var snapshot = life.Capture(); Assert.That(snapshot.RunId, Is.EqualTo(id)); Assert.That(snapshot.RunXp, Is.EqualTo(123));
            Assert.That(snapshot.Players[1].State, Is.EqualTo(PlayerLifeState.Downed)); Assert.That(snapshot.Classes[1], Is.EqualTo("future.class"));
            Assert.Throws<InvalidOperationException>(() => life.BindProfile(1, Guid.NewGuid()));
        }
    }
}
