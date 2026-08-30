using System;
using System.Numerics;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class PlayerSimulationTests
    {
        private readonly PlayerIdentity player = new PlayerIdentity(1);
        private PlayerSimulation Create()
        {
            var simulation = new PlayerSimulation(player, 5);
            simulation.SetActive(true);
            return simulation;
        }

        [TestCase(1, 0, 5)] [TestCase(-1, 0, 5)] [TestCase(0, 1, 5)] [TestCase(0, -1, 5)]
        [TestCase(1, 1, 5)] [TestCase(-1, -1, 5)] [TestCase(0.5f, 0, 2.5f)]
        public void MovementHonorsMaximumSpeedAndAnalogMagnitude(float x, float y, float speed)
        {
            var simulation = Create();
            Assert.That(simulation.RequestMove(new MoveIntent(player, new Vector2(x, y))), Is.True);
            Assert.That(simulation.DesiredVelocity.Length(), Is.EqualTo(speed).Within(0.0001f));
            Assert.That(simulation.State.Position, Is.EqualTo(Vector2.Zero), "Only physics records displacement.");
        }

        [Test]
        public void AimIsNormalizedAndIndependentOfMovement()
        {
            var simulation = Create();
            simulation.RequestMove(new MoveIntent(player, Vector2.UnitY));
            simulation.RequestAim(new AimIntent(player, new Vector2(-3, 4)));
            Assert.That(simulation.State.AimDirection, Is.EqualTo(new Vector2(-0.6f, 0.8f)));
            Assert.That(simulation.DesiredVelocity, Is.EqualTo(Vector2.UnitY * 5));
            simulation.RecordPhysics(player, new Vector2(0, 2), Vector2.Zero);
            Assert.That(simulation.State.AimDirection, Is.EqualTo(new Vector2(-0.6f, 0.8f)));
            Assert.That(simulation.State.Position, Is.EqualTo(new Vector2(0, 2)));
        }

        [Test]
        public void WrongPlayerAndInvalidVectorsCannotChangeSimulation()
        {
            var simulation = Create();
            var other = new PlayerIdentity(2);
            Assert.That(simulation.RequestMove(new MoveIntent(other, Vector2.One)), Is.False);
            Assert.That(simulation.RequestAim(new AimIntent(other, Vector2.UnitY)), Is.False);
            foreach (var value in new[] { Vector2.Zero, new Vector2(0.00001f, 0), new Vector2(float.NaN, 1), new Vector2(float.PositiveInfinity, 0) })
                Assert.That(simulation.RequestAim(new AimIntent(player, value)), Is.False);
            Assert.That(simulation.RequestMove(new MoveIntent(player, new Vector2(float.NaN, 0))), Is.False);
            Assert.That(simulation.State.AimDirection, Is.EqualTo(Vector2.UnitX));
            Assert.That(simulation.DesiredVelocity, Is.EqualTo(Vector2.Zero));
            Assert.Throws<ArgumentException>(() => simulation.RecordPhysics(other, Vector2.Zero, Vector2.Zero));
        }

        [Test]
        public void InactiveSimulationClearsMovementAndRetainsAimUntilNewCommands()
        {
            var simulation = Create();
            simulation.RequestMove(new MoveIntent(player, Vector2.One));
            simulation.RequestAim(new AimIntent(player, Vector2.UnitY));
            simulation.SetActive(false);
            Assert.That(simulation.RequestMove(new MoveIntent(player, Vector2.One)), Is.False);
            Assert.That(simulation.RequestAim(new AimIntent(player, Vector2.UnitX)), Is.False);
            simulation.SetActive(true);
            Assert.That(simulation.DesiredVelocity, Is.EqualTo(Vector2.Zero));
            Assert.That(simulation.State.AimDirection, Is.EqualTo(Vector2.UnitY));
        }

        [Test]
        public void PauseAndResumePreserveGameplayWithoutSceneLoading()
        {
            var flow = new ApplicationFlow();
            Assert.That(flow.TryRequest(ApplicationCommand.Pause), Is.False);
            flow.TryRequest(ApplicationCommand.Initialize);
            flow.CompleteLoading(ApplicationState.MainMenu);
            Assert.That(flow.TryRequest(ApplicationCommand.Resume), Is.False);
            flow.TryRequest(ApplicationCommand.NewGame);
            flow.CompleteLoading(ApplicationState.Gameplay);
            Assert.That(flow.TryRequest(ApplicationCommand.Pause), Is.True);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Paused));
            Assert.That(flow.Destination, Is.Null);
            Assert.That(flow.TryRequest(ApplicationCommand.Pause), Is.False);
            Assert.That(flow.TryRequest(ApplicationCommand.Resume), Is.True);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Gameplay));
            flow.TryRequest(ApplicationCommand.Pause);
            Assert.That(flow.TryRequest(ApplicationCommand.ReturnToMainMenu), Is.True);
            Assert.That(flow.Destination, Is.EqualTo(ApplicationState.MainMenu));
        }
    }
}
