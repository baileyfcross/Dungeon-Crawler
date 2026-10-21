using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunGenerationTests
    {
        private static RoomConfiguration[] Definitions() => new[]
        {
            new RoomConfiguration("room.starting", RoomCategory.Starting),
            new RoomConfiguration("room.engine", RoomCategory.Engine),
            new RoomConfiguration("room.cockpit", RoomCategory.Cockpit),
            new RoomConfiguration("room.storage", RoomCategory.Storage),
            new RoomConfiguration("room.generator", RoomCategory.Generator),
            new RoomConfiguration("room.lab", RoomCategory.Lab)
        };
        private static RunGenerationConfiguration DefaultConfiguration() => new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3), Definitions());

        [TestCase(0UL)]
        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(ulong.MaxValue)]
        public void IdenticalInputsProduceIdenticalCompleteGraphs(ulong seed)
        {
            var first = new RunGenerator().Generate(seed, DefaultConfiguration());
            var second = new RunGenerator().Generate(seed, DefaultConfiguration());
            Assert.That(Signature(first), Is.EqualTo(Signature(second)));
            Assert.That(first.Seed, Is.EqualTo(seed));
            Assert.That(first.GenerationVersion, Is.EqualTo(1));
            Validate(first);
        }

        [Test]
        public void VersionOneSeed42CompatibilityVectorIsFrozen()
        {
            var graph = new RunGenerator().Generate(42, DefaultConfiguration(), 1);
            Assert.That(string.Join(",", graph.Nodes.Select(n => n.DefinitionId)), Is.EqualTo(
                "room.starting,room.generator,room.cockpit,room.lab,room.cockpit,room.storage,room.generator,room.engine,room.lab,room.cockpit,room.engine,room.cockpit"));
            Assert.That(string.Join(" ", graph.Connections.Select(e => e.From.Value + ">" + e.To.Value)), Is.EqualTo(
                "0>1 0>2 1>3 2>3 3>4 3>5 4>6 5>6 6>7 7>8 8>9 9>10 10>11"));
            Assert.That(graph.GetRoom(new RunRoomId(11)).IsTerminal, Is.True);
        }

        [Test]
        public void DefinitionInputOrderAndCultureCannotInfluenceGeneration()
        {
            var source = Definitions();
            var settings = new RunGenerationSettings(9, 11, 3);
            var expected = Signature(new RunGenerator().Generate(2026, new RunGenerationConfiguration(settings, source)));
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                for (var offset = 0; offset < source.Length; offset++)
                {
                    var reordered = source.Skip(offset).Concat(source.Take(offset)).Reverse();
                    var configuration = new RunGenerationConfiguration(settings, reordered);
                    Assert.That(configuration.Definitions.Select(d => d.Id), Is.EqualTo(source.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal)));
                    Assert.That(Signature(new RunGenerator().Generate(2026, configuration)), Is.EqualTo(expected));
                }
                var mixedCase = new[] { new RoomConfiguration("I", RoomCategory.Starting), new RoomConfiguration("i", RoomCategory.Engine), new RoomConfiguration("Z", RoomCategory.Lab) };
                Assert.That(new RunGenerationConfiguration(settings, mixedCase).Definitions.Select(d => d.Id), Is.EqualTo(new[] { "I", "Z", "i" }));
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        [Test]
        public void RunRoomIdentityIsSeparateFromReusableDefinitionAndCategory()
        {
            var configuration = new RunGenerationConfiguration(new RunGenerationSettings(10, 10, 3), new[]
            { new RoomConfiguration("start", RoomCategory.Starting), new RoomConfiguration("shared", RoomCategory.Lab) });
            var graph = new RunGenerator().Generate(7, configuration);
            Validate(graph);
            Assert.That(graph.Nodes.Skip(1).All(n => n.DefinitionId == "shared"), Is.True);
            Assert.That(graph.Nodes.Select(n => n.Id.Value), Is.EqualTo(Enumerable.Range(0, graph.Nodes.Count)));
            Assert.That(graph.StartRoomId.ToString(), Is.EqualTo("room-000"));
            Assert.That(graph.GetRoom(graph.TerminalRoomId).Category, Is.EqualTo(RoomCategory.Lab), "Terminal is structural, not a mandatory Cockpit.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunRoomId(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.GetRoom(new RunRoomId(graph.Nodes.Count)));
        }

        [Test]
        public void SnapshotsAndGraphCollectionsCannotBeMutatedThroughTheirInputsOrViews()
        {
            var source = Definitions();
            var configuration = new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3), source);
            var graph = new RunGenerator().Generate(42, configuration);
            var before = Signature(graph);
            Array.Reverse(source);
            source[0] = new RoomConfiguration("replacement", RoomCategory.Engine, 5);
            Assert.That(Signature(graph), Is.EqualTo(before));
            Assert.That(Signature(new RunGenerator().Generate(graph.Seed, graph.Configuration, graph.GenerationVersion)), Is.EqualTo(before));
            Assert.Throws<NotSupportedException>(() => ((IList<RoomConfiguration>)configuration.Definitions)[0] = source[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<RoomNode>)graph.Nodes).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<RoomConnection>)graph.Connections).Clear());
        }

        [TestCase(0, 10, 3)]
        [TestCase(1, 10, 3)]
        [TestCase(10, 9, 3)]
        [TestCase(2, 1025, 0)]
        [TestCase(1025, 1025, 0)]
        [TestCase(9, 11, -1)]
        [TestCase(9, 11, 6)]
        public void InvalidSettingsFailExplicitly(int minimum, int maximum, int splits)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new RunGenerationSettings(minimum, maximum, splits));

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(" room.engine ")]
        public void InvalidDefinitionIdentityIsRejected(string id)
            => Assert.Throws<ArgumentException>(() => new RoomConfiguration(id, RoomCategory.Engine));

        [TestCase(-1)]
        [TestCase(6)]
        public void UnapprovedCategoriesAreRejected(int category)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new RoomConfiguration("room.invalid", (RoomCategory)category));

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidDefinitionWeightsAreRejected(int weight)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new RoomConfiguration("room.engine", RoomCategory.Engine, weight));

        [Test]
        public void MissingNullDuplicateAndOverflowingDefinitionsFailExplicitly()
        {
            var settings = new RunGenerationSettings(9, 11, 3);
            var start = new RoomConfiguration("start", RoomCategory.Starting);
            var normal = new RoomConfiguration("normal", RoomCategory.Engine);
            Assert.Throws<ArgumentNullException>(() => new RunGenerationConfiguration(null, Definitions()));
            Assert.Throws<ArgumentNullException>(() => new RunGenerationConfiguration(settings, null));
            foreach (var invalid in new[]
            {
                Array.Empty<RoomConfiguration>(), new[] { start }, new[] { normal }, new[] { start, normal, null },
                new[] { start, normal, new RoomConfiguration("normal", RoomCategory.Lab) },
                new[] { start, new RoomConfiguration("a", RoomCategory.Engine, int.MaxValue), normal },
                new[] { new RoomConfiguration("a", RoomCategory.Starting, int.MaxValue), start, normal }
            }) Assert.Throws<ArgumentException>(() => new RunGenerationConfiguration(settings, invalid));
            Assert.Throws<ArgumentNullException>(() => new RunGenerator().Generate(0, null));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(2)]
        public void UnsupportedGenerationVersionsAreRejected(int version)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new RunGenerator().Generate(0, DefaultConfiguration(), version));

        [Test]
        public void RepresentativeSeedsVaryTopologyCategoriesAndRouteLengths()
        {
            var configuration = DefaultConfiguration();
            var graphs = Enumerable.Range(0, 64).Select(seed => new RunGenerator().Generate((ulong)seed, configuration)).ToArray();
            Assert.That(graphs.Select(g => string.Join(",", g.Connections.Select(e => e.From + ">" + e.To))).Distinct().Count(), Is.GreaterThan(8));
            Assert.That(graphs.Select(g => string.Join(",", g.Nodes.Select(n => n.DefinitionId))).Distinct().Count(), Is.GreaterThan(8));
            Assert.That(graphs.Select(g => g.GetRoom(g.TerminalRoomId).Category).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(graphs.Select(g => g.Nodes.Count - g.Connections.GroupBy(e => e.From).Count(edges => edges.Count() == 2)).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void MaximumSupportedRouteStillHasLinearBoundedGrowth()
        {
            var graph = new RunGenerator().Generate(ulong.MaxValue, new RunGenerationConfiguration(new RunGenerationSettings(1024, 1024, 511), Definitions()));
            Validate(graph);
            Assert.That(graph.Nodes.Count, Is.LessThanOrEqualTo(1535));
        }

        [Test]
        public void AuthoredWeightsAffectSelectionWithoutChangingTopology()
        {
            var settings = new RunGenerationSettings(10, 10, 2);
            var first = new RunGenerationConfiguration(settings, new[] { new RoomConfiguration("start", RoomCategory.Starting),
                new RoomConfiguration("a", RoomCategory.Engine, 9), new RoomConfiguration("b", RoomCategory.Lab, 1) });
            var second = new RunGenerationConfiguration(settings, new[] { new RoomConfiguration("start", RoomCategory.Starting),
                new RoomConfiguration("a", RoomCategory.Engine, 1), new RoomConfiguration("b", RoomCategory.Lab, 9) });
            var a = new RunGenerator().Generate(42, first); var b = new RunGenerator().Generate(42, second);
            Assert.That(a.Connections.Select(e => (e.From, e.To)), Is.EqualTo(b.Connections.Select(e => (e.From, e.To))));
            Assert.That(a.Nodes.Select(n => n.DefinitionId), Is.Not.EqualTo(b.Nodes.Select(n => n.DefinitionId)));
        }

        [Test]
        public void FiveThousandSeedsSatisfyAllGraphInvariantsAcrossFiveSettingsProfiles()
        {
            var settings = new[] { new RunGenerationSettings(9, 11, 3), new RunGenerationSettings(2, 2, 0),
                new RunGenerationSettings(3, 8, 3), new RunGenerationSettings(12, 16, 0), new RunGenerationSettings(40, 64, 31) };
            var configurations = settings.Select(s => new RunGenerationConfiguration(s, Definitions())).ToArray();
            var watch = Stopwatch.StartNew();
            for (ulong seed = 0; seed < 5000; seed++) Validate(new RunGenerator().Generate(seed, configurations[seed % (ulong)configurations.Length]));
            TestContext.Progress.WriteLine($"Generation invariants: 5000 seeds, 5 profiles, {watch.Elapsed.TotalSeconds:F3}s; all passed.");
        }

        [Test]
        public void CoreAssemblyHasNoUnityEngineOrEditorReference()
        {
            Assert.That(typeof(RunGenerator).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Unity", StringComparison.Ordinal)), Is.False);
        }

        private static string Signature(DungeonGraph graph)
        {
            var text = new StringBuilder();
            text.Append(graph.Seed.ToString(CultureInfo.InvariantCulture)).Append('|').Append(graph.GenerationVersion).Append('|');
            var settings = graph.Configuration.Settings;
            text.Append(settings.MinRouteRooms).Append(',').Append(settings.MaxRouteRooms).Append(',').Append(settings.MaxSplits).Append('|');
            foreach (var definition in graph.Configuration.Definitions) text.Append(definition.Id).Append(':').Append((int)definition.Category).Append(':').Append(definition.Weight).Append(';');
            foreach (var node in graph.Nodes) text.Append(node.Id).Append(':').Append(node.DefinitionId).Append(':').Append((int)node.Category).Append(':').Append(node.IsTerminal).Append(';');
            foreach (var edge in graph.Connections) text.Append(edge.From).Append('>').Append(edge.To).Append(';');
            return text.ToString();
        }

        private static void Validate(DungeonGraph graph)
        {
            var seed = graph.Seed;
            var nodes = graph.Nodes;
            var settings = graph.Configuration.Settings;
            Check(nodes.Select(n => n.Id).Distinct().Count() == nodes.Count, seed, "Unique run-local IDs");
            Check(nodes.Count(n => n.Category == RoomCategory.Starting) == 1 && graph.GetRoom(graph.StartRoomId).Category == RoomCategory.Starting, seed, "One Starting node");
            Check(nodes.Count(n => n.IsTerminal) == 1 && graph.GetRoom(graph.TerminalRoomId).IsTerminal, seed, "One structural Terminal");
            var outgoing = nodes.ToDictionary(n => n.Id, n => new List<RunRoomId>());
            var indegree = nodes.ToDictionary(n => n.Id, n => 0);
            var pairs = new HashSet<(RunRoomId, RunRoomId)>();
            foreach (var edge in graph.Connections)
            {
                Check(outgoing.ContainsKey(edge.From) && outgoing.ContainsKey(edge.To), seed, "Existing connection endpoints");
                Check(edge.From.Value < edge.To.Value, seed, "Forward-only edges in topological ID order");
                Check(pairs.Add((edge.From, edge.To)), seed, "No duplicate edges");
                outgoing[edge.From].Add(edge.To); indegree[edge.To]++;
            }
            var reached = new HashSet<RunRoomId> { graph.StartRoomId };
            var queue = new Queue<RunRoomId>(); queue.Enqueue(graph.StartRoomId);
            while (queue.Count > 0)
                foreach (var next in outgoing[queue.Dequeue()]) if (reached.Add(next)) queue.Enqueue(next);
            Check(reached.Count == nodes.Count && reached.Contains(graph.TerminalRoomId), seed, "Every node and Terminal reachable");
            var splitCount = 0;
            foreach (var node in nodes)
            {
                var exits = outgoing[node.Id];
                Check(node.IsTerminal ? exits.Count == 0 : exits.Count >= 1 && exits.Count <= 2, seed, "Valid exit count");
                Check(graph.Configuration.Definitions.Any(d => d.Id == node.DefinitionId && d.Category == node.Category), seed, "Authored definition identity/category retained");
                if (exits.Count != 2) continue;
                splitCount++;
                Check(outgoing[exits[0]].Count == 1 && outgoing[exits[1]].Count == 1, seed, "Both split arms continue");
                Check(outgoing[exits[0]][0].Equals(outgoing[exits[1]][0]), seed, "Both arms rejoin at the same next room");
                Check(indegree[exits[0]] == 1 && indegree[exits[1]] == 1, seed, "Bounded isolated split arms");
            }
            Check(splitCount <= settings.MaxSplits, seed, "Configured split limit");
            Check(settings.MaxSplits == 0 || settings.MaxRouteRooms < 3 || nodes.Count == 2 || splitCount > 0, seed, "Branch when the selected route supports it");
            // Independent Kahn traversal and dynamic min/max path lengths; no exponential path enumeration.
            var minimum = nodes.ToDictionary(n => n.Id, n => int.MaxValue);
            var maximum = nodes.ToDictionary(n => n.Id, n => int.MinValue);
            minimum[graph.StartRoomId] = maximum[graph.StartRoomId] = 1;
            foreach (var node in nodes) if (indegree[node.Id] == 0) queue.Enqueue(node.Id);
            var visited = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); visited++;
                foreach (var next in outgoing[current])
                {
                    minimum[next] = Math.Min(minimum[next], minimum[current] + 1);
                    maximum[next] = Math.Max(maximum[next], maximum[current] + 1);
                    if (--indegree[next] == 0) queue.Enqueue(next);
                }
            }
            Check(visited == nodes.Count, seed, "No cycles");
            var minRoute = minimum[graph.TerminalRoomId]; var maxRoute = maximum[graph.TerminalRoomId];
            Check(minRoute >= settings.MinRouteRooms && maxRoute <= settings.MaxRouteRooms, seed, "Every path respects route-length bounds");
            Check(minRoute == maxRoute, seed, "Version 1 diamonds have equal-length arms");
            Check(nodes.Count == maxRoute + splitCount && nodes.Count <= settings.MaxRouteRooms + settings.MaxSplits, seed, "Linear node bound");
            Check(graph.Connections.Count == maxRoute - 1 + 2 * splitCount, seed, "Linear connection bound");
        }

        private static void Check(bool condition, ulong seed, string message)
        { if (!condition) Assert.Fail($"Seed {seed}: {message}"); }
    }
}
