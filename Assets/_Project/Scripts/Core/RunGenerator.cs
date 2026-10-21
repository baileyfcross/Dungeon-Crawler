using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>Version 1: layered routes with non-adjacent, two-arm diamonds that immediately rejoin.</summary>
    public sealed class RunGenerator
    {
        public const int CurrentVersion = 1;

        public DungeonGraph Generate(ulong seed, RunGenerationConfiguration configuration, int generationVersion = CurrentVersion)
        {
            if (generationVersion != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(generationVersion), "Unsupported generator version.");
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var random = new DeterministicRandom(seed);
            var settings = configuration.Settings;
            var routeRooms = settings.MinRouteRooms + random.NextInt(settings.MaxRouteRooms - settings.MinRouteRooms + 1);
            var capacity = Math.Min(settings.MaxSplits, (routeRooms - 1) / 2);
            var splitCount = capacity == 0 ? 0 : 1 + random.NextInt(capacity);
            var splitLayers = SelectSplitLayers(routeRooms, splitCount, random);
            var nodes = new List<RoomNode>(routeRooms + splitCount);
            var edges = new List<RoomConnection>(routeRooms - 1 + 2 * splitCount);
            var previous = Array.Empty<RunRoomId>();
            var splitIndex = 0;
            for (var layer = 0; layer < routeRooms; layer++)
            {
                var split = splitIndex < splitLayers.Length && splitLayers[splitIndex] == layer;
                if (split) splitIndex++;
                var current = new RunRoomId[split ? 2 : 1];
                for (var arm = 0; arm < current.Length; arm++)
                {
                    current[arm] = new RunRoomId(nodes.Count);
                    var definition = ChooseDefinition(configuration, layer == 0, random);
                    nodes.Add(new RoomNode(current[arm], definition, layer == routeRooms - 1));
                }
                foreach (var from in previous)
                    foreach (var to in current) edges.Add(new RoomConnection(from, to));
                previous = current;
            }
            return new DungeonGraph(seed, generationVersion, configuration, nodes.ToArray(), edges.ToArray());
        }

        private static int[] SelectSplitLayers(int routeRooms, int count, DeterministicRandom random)
        {
            if (count == 0) return Array.Empty<int>();
            // Select a subset in compressed space, then insert the required one-layer gaps.
            // This avoids retries, adjacent split layers, and permanent branching growth.
            var slots = new int[routeRooms - count - 1];
            for (var i = 0; i < slots.Length; i++) slots[i] = i;
            for (var i = 0; i < count; i++)
            {
                var selected = i + random.NextInt(slots.Length - i);
                var swap = slots[i]; slots[i] = slots[selected]; slots[selected] = swap;
            }
            var layers = new int[count];
            Array.Copy(slots, layers, count);
            Array.Sort(layers);
            for (var i = 0; i < count; i++) layers[i] += i + 1;
            return layers;
        }

        private static RoomConfiguration ChooseDefinition(RunGenerationConfiguration configuration, bool starting, DeterministicRandom random)
        {
            var choice = random.NextInt(starting ? configuration.StartingWeight : configuration.RegularWeight);
            foreach (var definition in configuration.Definitions)
            {
                if ((definition.Category == RoomCategory.Starting) != starting) continue;
                if (choice < definition.Weight) return definition;
                choice -= definition.Weight;
            }
            throw new InvalidOperationException("Validated definition weights must contain the selected value.");
        }
    }
}
