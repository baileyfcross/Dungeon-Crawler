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
        internal RoomNode(RunRoomId id, RoomConfiguration definition, bool isTerminal)
        { Id = id; DefinitionId = definition.Id; Category = definition.Category; IsTerminal = isTerminal; }
    }

    public readonly struct RoomConnection
    {
        public RunRoomId From { get; }
        public RunRoomId To { get; }
        internal RoomConnection(RunRoomId from, RunRoomId to) { From = from; To = to; }
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

        public RoomNode GetRoom(RunRoomId id)
        {
            if (id.Value >= Nodes.Count) throw new ArgumentOutOfRangeException(nameof(id));
            return Nodes[id.Value];
        }
    }
}
