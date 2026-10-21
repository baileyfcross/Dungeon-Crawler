using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpaceCrawler.Core
{
    public readonly struct RunRoomId : IEquatable<RunRoomId>
    {
        public int Value { get; }
        public RunRoomId(int value)
        { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
        public bool Equals(RunRoomId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RunRoomId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "room-" + Value.ToString("D3", CultureInfo.InvariantCulture);
    }

    public sealed class RoomNode
    {
        public RunRoomId Id { get; }
        public string DefinitionId { get; }
        public RoomCategory Category { get; }
        public bool IsTerminal { get; }
        public int LayoutX { get; }
        public int LayoutY { get; }
        internal RoomNode(RunRoomId id, RoomConfiguration definition, bool isTerminal, int layoutX = 0, int layoutY = 0)
        { Id = id; DefinitionId = definition.Id; Category = definition.Category; IsTerminal = isTerminal; LayoutX = layoutX; LayoutY = layoutY; }
    }

    public readonly struct RoomConnection
    {
        public RunRoomId From { get; }
        public RunRoomId To { get; }
        // Cardinal ports: north, east, south, west. Negative ports retain the legacy authored exits.
        public int FromPort { get; }
        public int ToPort { get; }
        internal RoomConnection(RunRoomId from, RunRoomId to, int fromPort = -1, int toPort = -1)
        { From = from; To = to; FromPort = fromPort; ToPort = toPort; }
    }

    /// <summary>One complete immutable abstract run. No physical rooms, current-room mutation, or saves.</summary>
    public sealed class DungeonGraph
    {
        public ulong Seed { get; }
        public int GenerationVersion { get; }
        // Retain the complete snapshot: seed alone is not a cross-version reconstruction contract.
        public RunGenerationConfiguration Configuration { get; }
        public IReadOnlyList<RoomNode> Nodes { get; }
        public IReadOnlyList<RoomConnection> Connections { get; }
        public RunRoomId StartRoomId => Nodes[0].Id;
        public RunRoomId TerminalRoomId => Nodes[Nodes.Count - 1].Id;

        internal DungeonGraph(ulong seed, int version, RunGenerationConfiguration configuration,
            RoomNode[] nodes, RoomConnection[] connections)
        {
            Seed = seed; GenerationVersion = version; Configuration = configuration;
            Nodes = Array.AsReadOnly(nodes); Connections = Array.AsReadOnly(connections);
        }

        public IReadOnlyList<RoomConnection> Route(RunRoomId from, RunRoomId destination)
        {
            GetRoom(from); GetRoom(destination);
            var visited = new HashSet<RunRoomId> { from }; var queue = new Queue<RunRoomId>();
            var previous = new Dictionary<RunRoomId,RoomConnection>(); queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (current.Equals(destination)) break;
                foreach (var edge in Connections) if (edge.From.Equals(current) && visited.Add(edge.To))
                { previous[edge.To] = edge; queue.Enqueue(edge.To); }
            }
            if (!visited.Contains(destination)) throw new ArgumentException("Destination is not reachable.");
            var result = new List<RoomConnection>();
            while (!destination.Equals(from)) { var edge = previous[destination]; result.Add(edge); destination = edge.From; }
            result.Reverse(); return result.AsReadOnly();
        }

        public RoomNode GetRoom(RunRoomId id)
        {
            if (id.Value >= Nodes.Count) throw new ArgumentOutOfRangeException(nameof(id));
            return Nodes[id.Value];
        }
    }
}
