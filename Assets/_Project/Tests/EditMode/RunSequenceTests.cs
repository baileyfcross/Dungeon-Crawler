using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunSequenceTests
    {
        private static RunGenerationConfiguration Catalog() => new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3),
            Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c => new RoomConfiguration("room." + c, c)));

        [Test]
        public void ThousandsOfSeedsHaveThreeReachableLevelsWithTenToSeventeenTotalRooms()
        {
            var totals = new System.Collections.Generic.HashSet<int>();
            var catalog = Catalog();
            for (ulong seed = 0; seed < 5000; seed++)
            {
                var sequence = new RunSequence(seed, catalog);
                Assert.That(sequence.Levels.Count, Is.EqualTo(3));
                Assert.That(sequence.Levels.Select(g => g.Seed).Distinct().Count(), Is.EqualTo(3));
                foreach (var graph in sequence.Levels)
                {
                    totals.Add(graph.Nodes.Count);
                    Assert.That(graph.Nodes.Count, Is.InRange(10, 17));
                    Assert.That(graph.GenerationVersion, Is.EqualTo(1));
                    var reachable = new System.Collections.Generic.HashSet<RunRoomId> { graph.StartRoomId };
                    foreach (var room in graph.Nodes)
                    {
                        Assert.That(reachable.Contains(room.Id), Is.True);
                        var exits = graph.Connections.Where(c => c.From.Equals(room.Id)).ToArray();
                        if (room.IsTerminal) Assert.That(exits, Is.Empty);
                        if (!room.IsTerminal) Assert.That(exits.Length, Is.InRange(1, 2));
                        foreach (var edge in exits) reachable.Add(edge.To);
                    }
                    Assert.That(graph.Nodes.Count(n => n.IsTerminal), Is.EqualTo(1));
                }
            }
            Assert.That(totals.OrderBy(n => n), Is.EqualTo(Enumerable.Range(10, 8)));
        }

        [TestCase(0UL)]
        [TestCase(42UL)]
        [TestCase(ulong.MaxValue)]
        public void SequenceAndUnderlyingGraphsReproduceExactly(ulong seed)
        {
            var first = new RunSequence(seed, Catalog()); var second = new RunSequence(seed, Catalog());
            for (var i = 0; i < 3; i++)
            {
                var graph = first.Levels[i];
                Assert.That(GraphFingerprint.Compute(graph), Is.EqualTo(GraphFingerprint.Compute(second.Levels[i])));
                Assert.That(GraphFingerprint.Compute(graph), Is.EqualTo(GraphFingerprint.Compute(new RunGenerator().Generate(graph.Seed, graph.Configuration))));
            }
        }

        [Test]
        public void OnlyCompletedCurrentLevelsAdvanceAndFinalLevelCannotAdvance()
        {
            var run = new RunSequence(42, Catalog());
            for (var level = 1; level <= 3; level++)
            {
                var progress = new RunProgression(run.CurrentGraph, new PlayerIdentity(1));
                progress.SetGameplayActive(true);
                Assert.That(run.TryAdvance(progress), Is.False);
                progress.TryCompleteEntry(run.CurrentGraph.StartRoomId);
                while (!progress.IsTerminal)
                {
                    var destination = progress.Choices[0].To;
                    Assert.That(progress.TryRequestExit(new ExitIntent(progress.Player, progress.CurrentRoomId, destination)), Is.True);
                    Assert.That(progress.TryBeginAcceptedEntry() && progress.TryCompleteEntry(destination), Is.True);
                }
                progress.SetGameplayActive(false);
                Assert.That(run.TryAdvance(progress), Is.False);
                progress.SetGameplayActive(true);
                Assert.That(run.TryAdvance(progress), Is.EqualTo(level < 3));
                Assert.That(run.TryAdvance(progress), Is.False);
                Assert.That(run.LevelNumber, Is.EqualTo(Math.Min(level + 1, 3)));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunSequence(42, Catalog(), 99));
        }
    }
}
