using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunProgressionTests
    {
        private static DungeonGraph Graph(ulong seed = 42) => new RunGenerator().Generate(seed,
            new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3),
                Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c => new RoomConfiguration("room." + c.ToString().ToLowerInvariant(), c, 1))));
        private static RunProgression Playing(ulong seed = 42)
        {
            var run = new RunProgression(Graph(seed), new PlayerIdentity(1));
            Assert.That(run.TryCompleteEntry(run.CurrentRoomId), Is.True);
            run.SetGameplayActive(true);
            return run;
        }
        private static ExitIntent Forward(RunProgression run) => new ExitIntent(run.Player, run.CurrentRoomId, run.Choices[0].To);

        [Test]
        public void InitialEntryOwnsStartAndCompletesOnceBeforeGameplayActivation()
        {
            var graph = Graph();
            var run = new RunProgression(graph, new PlayerIdentity(1));
            Assert.That(run.CurrentRoomId, Is.EqualTo(graph.StartRoomId));
            Assert.That(run.State, Is.EqualTo(RoomFlowState.EnteringRoom));
            Assert.That(run.GameplayActive, Is.False);
            Assert.That(run.AcceptedDestination, Is.Null);
            Assert.That(run.TryRequestExit(Forward(run)), Is.False);
            Assert.That(run.TryCompleteEntry(run.Choices[0].To), Is.False);
            Assert.That(run.TryCompleteEntry(graph.StartRoomId), Is.True);
            Assert.That(run.TryCompleteEntry(graph.StartRoomId), Is.False);
            Assert.That(run.RoomsEntered, Is.EqualTo(1));
        }

        [TestCase(0UL, 1)]
        [TestCase(42UL, 2)]
        public void ChoicesPreserveAllExistingOutgoingConnectionsInOrder(ulong seed, int count)
        {
            var run = Playing(seed);
            Assert.That(run.Choices.Count, Is.EqualTo(count));
            Assert.That(run.Choices.Select(c => (c.From, c.To)),
                Is.EqualTo(run.Graph.Connections.Where(c => c.From.Equals(run.CurrentRoomId)).Select(c => (c.From, c.To))));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void EitherBranchCommitsOnlyItsAcceptedDestinationExactlyOnce(int choice)
        {
            var run = Playing(); var origin = run.CurrentRoomId; var destination = run.Choices[choice].To;
            var intent = new ExitIntent(run.Player, origin, destination);
            Assert.That(run.TryRequestExit(intent), Is.True);
            Assert.That(run.State, Is.EqualTo(RoomFlowState.LeavingRoom));
            Assert.That(run.CurrentRoomId, Is.EqualTo(origin));
            Assert.That(run.AcceptedDestination, Is.EqualTo(destination));
            Assert.That(run.TryRequestExit(intent), Is.False);
            Assert.That(run.TryCompleteEntry(destination), Is.False);
            Assert.That(run.TryBeginAcceptedEntry(), Is.True);
            Assert.That(run.TryBeginAcceptedEntry(), Is.False);
            Assert.That(run.TryRequestExit(intent), Is.False);
            Assert.That(run.TryCompleteEntry(origin), Is.False);
            Assert.That(run.TryCompleteEntry(destination), Is.True);
            Assert.That(run.TryCompleteEntry(destination), Is.False);
            Assert.That(run.CurrentRoomId, Is.EqualTo(destination));
            Assert.That(run.AcceptedDestination, Is.Null);
            Assert.That(run.RoomsEntered, Is.EqualTo(2));
            Assert.That(run.State, Is.EqualTo(RoomFlowState.Playing));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void InvalidOrWrongPlayerCannotRequestExit(int player)
        {
            var run = Playing();
            var identity = player == 0 ? default : new PlayerIdentity(player);
            Assert.That(run.TryRequestExit(new ExitIntent(identity, run.CurrentRoomId, run.Choices[0].To)), Is.False);
            Assert.That(run.State, Is.EqualTo(RoomFlowState.Playing));
        }

        [Test]
        public void StaleRoomUnconnectedDestinationAndBacktrackingAreRejected()
        {
            var run = Playing(); var start = run.CurrentRoomId; var destination = run.Choices[0].To;
            Assert.That(run.TryRequestExit(new ExitIntent(run.Player, destination, destination)), Is.False);
            Assert.That(run.TryRequestExit(new ExitIntent(run.Player, start, run.Graph.TerminalRoomId)), Is.False);
            Assert.That(run.TryRequestExit(new ExitIntent(run.Player, start, new RunRoomId(99999))), Is.False);
            Assert.That(run.TryRequestExit(Forward(run)), Is.True);
            Assert.That(run.TryBeginAcceptedEntry(), Is.True);
            Assert.That(run.TryCompleteEntry(destination), Is.True);
            Assert.That(run.TryRequestExit(new ExitIntent(run.Player, start, destination)), Is.False);
            Assert.That(run.TryRequestExit(new ExitIntent(run.Player, destination, start)), Is.False);
        }

        [Test]
        public void InactiveGameplayCannotRequestBeginOrCommitAcceptedTransition()
        {
            var run = Playing(); var intent = Forward(run);
            run.SetGameplayActive(false);
            Assert.That(run.TryRequestExit(intent), Is.False);
            run.SetGameplayActive(true); Assert.That(run.TryRequestExit(intent), Is.True);
            run.SetGameplayActive(false); Assert.That(run.TryBeginAcceptedEntry(), Is.False);
            Assert.That(run.CurrentRoomId, Is.EqualTo(intent.CurrentRoom));
            run.SetGameplayActive(true); Assert.That(run.TryBeginAcceptedEntry(), Is.True);
            run.SetGameplayActive(false); Assert.That(run.TryCompleteEntry(intent.Destination), Is.False);
            run.SetGameplayActive(true); Assert.That(run.TryCompleteEntry(intent.Destination), Is.True);
        }

        [Test]
        public void TwoHundredCompleteRoutesPreserveGraphAndEndAtStructuralTerminal()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var run = Playing(seed); var graph = run.Graph;
                var nodes = graph.Nodes.Select(n => (n.Id, n.DefinitionId, n.Category, n.IsTerminal)).ToArray();
                var edges = graph.Connections.Select(c => (c.From, c.To)).ToArray();
                var previous = -1;
                while (!run.IsTerminal)
                {
                    Assert.That(run.CurrentRoomId.Value, Is.GreaterThan(previous)); previous = run.CurrentRoomId.Value;
                    var destination = run.Choices[(int)(seed % (ulong)run.Choices.Count)].To;
                    Assert.That(run.TryRequestExit(new ExitIntent(run.Player, run.CurrentRoomId, destination)), Is.True);
                    Assert.That(run.TryBeginAcceptedEntry(), Is.True);
                    Assert.That(run.TryCompleteEntry(destination), Is.True);
                    Assert.That(run.RoomsEntered, Is.LessThanOrEqualTo(11));
                }
                Assert.That(run.CurrentRoomId, Is.EqualTo(graph.TerminalRoomId));
                Assert.That(run.State, Is.EqualTo(RoomFlowState.Terminal));
                Assert.That(run.Choices, Is.Empty);
                Assert.That(run.RoomsEntered, Is.InRange(9, 11));
                Assert.That(run.TryRequestExit(new ExitIntent(run.Player, run.CurrentRoomId, graph.StartRoomId)), Is.False);
                Assert.That(run.Graph, Is.SameAs(graph));
                Assert.That(graph.Nodes.Select(n => (n.Id, n.DefinitionId, n.Category, n.IsTerminal)), Is.EqualTo(nodes));
                Assert.That(graph.Connections.Select(c => (c.From, c.To)), Is.EqualTo(edges));
            }
        }

        [Test]
        public void FailedRoomLoadClosesAuthorityAndInvalidConstructionFailsExplicitly()
        {
            Assert.Throws<ArgumentNullException>(() => new RunProgression(null, new PlayerIdentity(1)));
            Assert.Throws<ArgumentException>(() => new RunProgression(Graph(), default));
            var run = Playing(); var intent = Forward(run);
            Assert.That(run.TryRequestExit(intent), Is.True);
            run.Fail("Missing spawn anchor");
            Assert.That(run.State, Is.EqualTo(RoomFlowState.Failed));
            Assert.That(run.Failure, Is.EqualTo("Missing spawn anchor"));
            run.SetGameplayActive(true);
            Assert.That(run.TryRequestExit(intent), Is.False);
            Assert.That(run.TryBeginAcceptedEntry(), Is.False);
            Assert.That(run.TryCompleteEntry(intent.Destination), Is.False);
        }
    }
}
