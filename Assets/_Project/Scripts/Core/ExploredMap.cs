using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>A presentation snapshot containing entered rooms and their known connections only.</summary>
    public sealed class ExploredMap
    {
        public IReadOnlyList<RoomNode> Rooms { get; }
        public IReadOnlyList<RoomConnection> Connections { get; }
        public IReadOnlyList<RunRoomId> PlayerRooms { get; }
        public ExploredMap(DungeonGraph graph, IEnumerable<RunRoomId> entered, IReadOnlyList<RunRoomId> playerRooms)
        {
            if (graph == null || entered == null || playerRooms == null || playerRooms.Count < 1 || playerRooms.Count > 2)
                throw new ArgumentException("An explicit explored level and player locations are required.");
            var ids = new HashSet<RunRoomId>(entered);
            var nodes = new List<RoomNode>();
            foreach (var id in ids) nodes.Add(graph.GetRoom(id));
            nodes.Sort((a,b) => a.Id.Value.CompareTo(b.Id.Value));
            var links = new List<RoomConnection>();
            foreach (var edge in graph.Connections)
                if (ids.Contains(edge.From) && ids.Contains(edge.To) && edge.From.Value < edge.To.Value) links.Add(edge);
            var locations = new List<RunRoomId>();
            foreach (var id in playerRooms)
            {
                if (!ids.Contains(id)) throw new ArgumentException("A player cannot occupy an undiscovered room.");
                locations.Add(id);
            }
            Rooms = nodes.AsReadOnly(); Connections = links.AsReadOnly(); PlayerRooms = locations.AsReadOnly();
        }
    }
}
