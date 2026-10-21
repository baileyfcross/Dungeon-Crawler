using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using CoreVector = System.Numerics.Vector2;
using Vector2 = UnityEngine.Vector2;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class MultiplayerRulesTests
    {
        private PlayerSimulation one, two;
        private CombatSimulation[] combat;
        private SessionAuthority authority;
        private static DungeonGraph Graph(ulong seed = 42, string prefix = "room.") => new RunGenerator().Generate(seed,
            new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3),
                Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c => new RoomConfiguration(prefix + c.ToString().ToLowerInvariant(), c, 1))));

        [SetUp]
        public void SetUp()
        {
            one = new PlayerSimulation(new PlayerIdentity(1), 5); two = new PlayerSimulation(new PlayerIdentity(2), 5);
            var knife = WeaponConfiguration.Melee("weapon.knife", 25, .5f, 2, 90);
            combat = new[] { new CombatSimulation(one, knife), new CombatSimulation(two, knife) };
            authority = new SessionAuthority(0, one, two, (i, intent) => combat[i].TryAttack(intent, out _));
            Assert.That(authority.Connect(73), Is.True); authority.SetRunning(true, 1);
        }

        private static PlayerInputMessage Command(int player, ulong sequence = 1, ulong attack = 0, bool active = true, uint epoch = 1)
            => new PlayerInputMessage { ClaimedPlayer = player, Sequence = sequence, AttackSequence = attack, Active = active,
                Epoch = epoch, Move = Vector2.up, Aim = Vector2.right };

        [Test]
        public void ActualSenderMapsToDistinctIdentitiesAndOnlyOneClientIsAdmitted()
        {
            Assert.That(authority.TryIdentity(0, out var host), Is.True); Assert.That(host.Value, Is.EqualTo(1));
            Assert.That(authority.TryIdentity(73, out var client), Is.True); Assert.That(client.Value, Is.EqualTo(2));
            Assert.That(authority.TryIdentity(999, out _), Is.False);
            Assert.That(authority.Connect(74), Is.False); Assert.That(authority.Connect(0), Is.False);
        }

        [TestCase(73UL, 1)]
        [TestCase(0UL, 2)]
        [TestCase(99UL, 2)]
        public void ForgedOrUnknownSenderCannotMoveAimOrAttackAnotherPlayer(ulong sender, int claimed)
        {
            var forged = Command(claimed, attack: 1); forged.Aim = Vector2.left;
            Assert.That(authority.Submit(sender, forged, 0), Is.False);
            Assert.That(one.DesiredVelocity, Is.EqualTo(CoreVector.Zero)); Assert.That(two.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(one.State.AimDirection, Is.EqualTo(CoreVector.UnitX)); Assert.That(two.State.AimDirection, Is.EqualTo(CoreVector.UnitX));
            Assert.That(combat.Sum(c => c.AcceptedAttackCount), Is.Zero);
            Assert.That(authority.RejectedOwnership, Is.EqualTo(1));
        }

        [Test]
        public void IndependentCommandsUseExistingMovementAimAndCooldownRules()
        {
            var host = Command(1, attack: 1); host.Move = new Vector2(8, 8); host.Aim = Vector2.up * 9;
            var client = Command(2, attack: 1); client.Move = Vector2.left; client.Aim = Vector2.down * 6;
            Assert.That(authority.Submit(0, host, 0), Is.True); Assert.That(authority.Submit(73, client, 0), Is.True);
            Assert.That(one.DesiredVelocity.Length(), Is.EqualTo(5).Within(.0001));
            Assert.That(two.DesiredVelocity, Is.EqualTo(-CoreVector.UnitX * 5));
            Assert.That(combat[0].LastExecution.AimDirection, Is.EqualTo(CoreVector.UnitY));
            Assert.That(combat[1].LastExecution.AimDirection, Is.EqualTo(-CoreVector.UnitY));
            Assert.That(authority.Submit(73, Command(2, 2, 2), .01), Is.False);
            Assert.That(combat[1].AcceptedAttackCount, Is.EqualTo(1));
            combat[1].AdvanceTime(.5f);
            Assert.That(authority.Submit(73, Command(2, 3, 3), .5), Is.True);
        }

        [Test]
        public void StaleEpochSequenceAndDuplicateReliableAttackAreRejected()
        {
            Assert.That(authority.Submit(73, Command(2, 5, 2), 0), Is.True);
            Assert.That(authority.Submit(73, Command(2, 4), .01), Is.False);
            combat[1].AdvanceTime(1);
            Assert.That(authority.Submit(73, Command(2, 5, 2), .02), Is.False);
            authority.SetRunning(false, 2); authority.SetRunning(true, 2);
            Assert.That(authority.Submit(73, Command(2, 6, 3), .03), Is.False);
            Assert.That(combat[1].AcceptedAttackCount, Is.EqualTo(1));
            Assert.That(two.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(authority.Submit(73, Command(2, 7, epoch: 2), .04), Is.True);
        }

        [Test]
        public void LocalGateNeutralizesOnlyItsPlayerWhileWorldCooldownContinues()
        {
            authority.Submit(0, Command(1, attack: 1), 0); authority.Submit(73, Command(2, attack: 1), 0);
            Assert.That(authority.Submit(0, Command(1, 3, active: false), .1), Is.True);
            Assert.That(one.DesiredVelocity, Is.EqualTo(CoreVector.Zero)); Assert.That(two.DesiredVelocity.Y, Is.EqualTo(5));
            Assert.That(one.IsActive && two.IsActive, Is.True);
            combat[0].AdvanceTime(1); combat[1].AdvanceTime(1);
            Assert.That(combat[0].CooldownRemaining + combat[1].CooldownRemaining, Is.Zero);
            Assert.That(authority.Submit(0, Command(1, 2, 2), .2), Is.False);
            Assert.That(authority.Submit(73, Command(2, 2, 2), .2), Is.True);
        }

        [Test]
        public void LostInputAndDisconnectCannotLeaveMovementOrOwnershipActive()
        {
            authority.Submit(73, Command(2), 0); authority.Submit(0, Command(1), .2);
            authority.Advance(.36);
            Assert.That(two.DesiredVelocity, Is.EqualTo(CoreVector.Zero)); Assert.That(one.DesiredVelocity.Y, Is.EqualTo(5));
            Assert.That(two.IsActive, Is.True); Assert.That(authority.TimedOut, Is.EqualTo(1));
            authority.Disconnect();
            Assert.That(authority.Running || authority.BothConnected || one.IsActive || two.IsActive, Is.False);
            Assert.That(one.DesiredVelocity + two.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(authority.TryIdentity(73, out _), Is.False);
        }

        [Test]
        public void InvalidMovementNeutralizesAndInvalidAimCannotExecuteAttack()
        {
            authority.Submit(73, Command(2), 0);
            var invalid = Command(2, 2, 1); invalid.Move.x = float.NaN;
            Assert.That(authority.Submit(73, invalid, .01), Is.False);
            Assert.That(two.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            invalid = Command(2, 3, 2); invalid.Aim = Vector2.zero;
            Assert.That(authority.Submit(73, invalid, .02), Is.False);
            Assert.That(combat[1].AcceptedAttackCount, Is.Zero);
        }

        [Test]
        public void FingerprintAgreesOnReconstructionAndDetectsSeedOrDefinitionDivergence()
        {
            var host = Graph(); var fingerprint = GraphFingerprint.Compute(host);
            Assert.That(fingerprint.Length, Is.EqualTo(64));
            Assert.That(GraphFingerprint.Compute(Graph()), Is.EqualTo(fingerprint));
            Assert.That(GraphFingerprint.Compute(Graph(43)), Is.Not.EqualTo(fingerprint));
            Assert.That(GraphFingerprint.Compute(Graph(42, "different.")), Is.Not.EqualTo(fingerprint));
            Assert.Throws<ArgumentNullException>(() => GraphFingerprint.Compute(null));
        }

        [Test]
        public void SameExitCoordinationRequiresBothAndExistingProgressionCommitsOnce()
        {
            var run = new RunProgression(Graph(), new PlayerIdentity(1)); run.SetGameplayActive(true); run.TryCompleteEntry(run.CurrentRoomId);
            var ready = new ExitReadiness(); ready.Reset(run.CurrentRoomId, run.Choices, true);
            var left = run.Choices[0].To; var right = run.Choices[1].To; var origin = run.CurrentRoomId;
            ready.SetReady(new ExitIntent(new PlayerIdentity(1), origin, left), true);
            Assert.That(ready.TryGetSharedDestination(out _), Is.False); Assert.That(ready.Count(left), Is.EqualTo(1));
            ready.SetReady(new ExitIntent(new PlayerIdentity(2), origin, right), true);
            Assert.That(ready.TryGetSharedDestination(out _), Is.False); Assert.That(ready.Count(right), Is.EqualTo(1));
            ready.SetReady(new ExitIntent(new PlayerIdentity(2), origin, right), false);
            Assert.That(ready.Count(right), Is.Zero);
            ready.SetReady(new ExitIntent(new PlayerIdentity(2), origin, left), true);
            Assert.That(ready.TryGetSharedDestination(out var destination), Is.True);
            Assert.That(run.CurrentRoomId, Is.EqualTo(origin));
            var intent = new ExitIntent(run.Player, origin, destination);
            Assert.That(run.TryRequestExit(intent), Is.True); Assert.That(run.TryRequestExit(intent), Is.False);
            Assert.That(run.TryBeginAcceptedEntry(), Is.True); Assert.That(run.TryCompleteEntry(destination), Is.True);
            Assert.That(run.TryCompleteEntry(destination), Is.False); Assert.That(run.RoomsEntered, Is.EqualTo(2));
            ready.Reset(destination, run.Choices, true);
            Assert.That(ready.SetReady(intent, true), Is.False); Assert.That(ready.TryGetSharedDestination(out _), Is.False);
        }

        [Test]
        public void DisconnectAndTerminalClearAllReadiness()
        {
            var graph = Graph(); var run = new RunProgression(graph, new PlayerIdentity(1));
            var ready = new ExitReadiness(); ready.Reset(run.CurrentRoomId, run.Choices, true);
            ready.SetReady(new ExitIntent(new PlayerIdentity(1), run.CurrentRoomId, run.Choices[0].To), true);
            ready.Disconnect(); Assert.That(ready.Count(run.Choices[0].To), Is.Zero);
            Assert.That(ready.SetReady(new ExitIntent(new PlayerIdentity(2), run.CurrentRoomId, run.Choices[0].To), true), Is.False);
            ready.Reset(graph.TerminalRoomId, run.ConnectionsFrom(graph.TerminalRoomId), true);
            Assert.That(ready.SetReady(new ExitIntent(new PlayerIdentity(1), graph.TerminalRoomId, graph.StartRoomId), true), Is.False);
            Assert.That(ready.TryGetSharedDestination(out _), Is.False);
        }
    }
}
