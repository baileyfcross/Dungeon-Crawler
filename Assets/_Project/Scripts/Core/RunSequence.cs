using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>Versioned three-level policy around the frozen room-graph generator.</summary>
    public sealed class RunSequence
    {
        public const int CurrentVersion = 2;
        public const int LevelCount = 3;
        public const int MinimumRooms = 10;
        public const int MaximumRooms = 25;
        private readonly DungeonGraph[] levels = new DungeonGraph[LevelCount];
        private readonly List<IReadOnlyList<RunRoomId>> completedRoutes = new List<IReadOnlyList<RunRoomId>>();
        public IReadOnlyList<IReadOnlyList<RunRoomId>> CompletedRoutes => completedRoutes.AsReadOnly();
        public ulong Seed { get; }
        public int Version { get; }
        public int LevelNumber { get; private set; } = 1;
        public DungeonGraph CurrentGraph => levels[LevelNumber - 1];
        public IReadOnlyList<DungeonGraph> Levels => Array.AsReadOnly(levels);

        public RunSequence(ulong seed, RunGenerationConfiguration catalog, int version = CurrentVersion)
        {
            if (version != 1 && version != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            Seed = seed; Version = version;
            for (var i = 0; i < LevelCount; i++)
            {
                var levelSeed = unchecked(seed + 0x9e3779b97f4a7c15UL * (ulong)i);
                var total = MinimumRooms + new DeterministicRandom(levelSeed ^ 0xd1b54a32d192ed03UL).NextInt(version == 1 ? 8 : MaximumRooms - MinimumRooms + 1);
                if (version == 2)
                {
                    var spatial = new RunGenerationConfiguration(new RunGenerationSettings(total, total, 0), catalog.Definitions);
                    levels[i] = new RunGenerator().Generate(levelSeed, spatial, 2);
                    continue;
                }
                // Generator v1 consumes a route selection, then chooses 1..3 diamonds.
                // Each diamond adds exactly one extra node. Match its choices without retries.
                var topology = new DeterministicRandom(levelSeed);
                topology.NextInt(1);
                var splits = 1 + topology.NextInt(3);
                var route = total - splits; // At least seven: capacity always accommodates three diamonds.
                var configuration = new RunGenerationConfiguration(new RunGenerationSettings(route, route, 3), catalog.Definitions);
                levels[i] = new RunGenerator().Generate(levelSeed, configuration);
                if (levels[i].Nodes.Count != total) throw new InvalidOperationException("Level policy and graph generator disagree.");
            }
        }

        public bool TryAdvance(RunProgression progression)
        {
            if (progression == null || !ReferenceEquals(progression.Graph, CurrentGraph)
                || progression.State != RoomFlowState.Terminal || !progression.GameplayActive || LevelNumber == LevelCount) return false;
            completedRoutes.Add(Array.AsReadOnly(new List<RunRoomId>(progression.VisitedRooms).ToArray()));
            LevelNumber++;
            return true;
        }

        public static RunSequence Restore(RunCheckpoint checkpoint, RunGenerationConfiguration catalog)
        {
            if (checkpoint.SequenceVersion != 1 && checkpoint.SequenceVersion != CurrentVersion) throw new NotSupportedException("Saved level policy is not supported.");
            var sequence = new RunSequence(checkpoint.RunSeed, catalog, checkpoint.SequenceVersion);
            for (var i = 0; i < LevelCount; i++)
            {
                if (sequence.levels[i].Seed != checkpoint.LevelSeeds[i]
                    || GraphFingerprint.Compute(sequence.levels[i]) != checkpoint.LevelFingerprints[i])
                    throw new ArgumentException("Saved level fingerprint does not match installed room data.");
                if (i >= checkpoint.CompletedRoutes.Count) continue;
                RunProgression.ValidateRoute(sequence.levels[i], checkpoint.CompletedRoutes[i]);
                var route = checkpoint.CompletedRoutes[i];
                if (!route[route.Count - 1].Equals(sequence.levels[i].TerminalRoomId)) throw new ArgumentException("Saved completed level is incomplete.");
                sequence.completedRoutes.Add(route);
            }
            if (checkpoint.World != null)
                foreach (var room in checkpoint.World.Rooms)
                { if (room.Level > checkpoint.LevelNumber) throw new ArgumentException("Saved room belongs to an unvisited level."); sequence.levels[room.Level-1].GetRoom(room.Room); }
            sequence.LevelNumber = checkpoint.LevelNumber;
            var graph = sequence.CurrentGraph;
            if (graph.Seed != checkpoint.Seed || graph.GenerationVersion != checkpoint.GenerationVersion
                || GraphFingerprint.Compute(graph) != checkpoint.Fingerprint) throw new ArgumentException("Saved current graph does not match.");
            RunProgression.ValidateRoute(graph, checkpoint.RoomVisits);
            RunProgression.ValidateRoute(graph, checkpoint.PartnerRoomVisits);
            if (checkpoint.Mode == SavedSessionMode.Solo && sequence.LevelNumber < LevelCount && graph.GetRoom(checkpoint.CurrentRoom).IsTerminal && checkpoint.Upgrades?.Pending!=true)
                throw new ArgumentException("Saved checkpoint is inside an incomplete level transition.");
            if(checkpoint.Upgrades?.Pending==true && (!graph.GetRoom(checkpoint.CurrentRoom).IsTerminal
                || (checkpoint.Mode==SavedSessionMode.Cooperative && !graph.GetRoom(checkpoint.PartnerRoomVisits[checkpoint.PartnerRoomVisits.Count-1]).IsTerminal)))
                throw new ArgumentException("Saved upgrade stop requires the party at the level exit.");
            return sequence;
        }
    }
}
