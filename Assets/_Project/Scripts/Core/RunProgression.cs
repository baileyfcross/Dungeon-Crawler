using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    public enum RoomFlowState { EnteringRoom, Playing, LeavingRoom, Terminal, Failed }

    public readonly struct ExitIntent
    {
        public PlayerIdentity Player { get; }
        public RunRoomId CurrentRoom { get; }
        public RunRoomId Destination { get; }
        public ExitIntent(PlayerIdentity player, RunRoomId currentRoom, RunRoomId destination)
        { Player = player; CurrentRoom = currentRoom; Destination = destination; }
    }

    /// <summary>Local traversal authority. The versioned generated graph is never modified.</summary>
    public sealed class RunProgression
    {
        public DungeonGraph Graph { get; }
        public PlayerIdentity Player { get; }
        public RunRoomId CurrentRoomId { get; private set; }
        public RunRoomId? AcceptedDestination { get; private set; }
        public RoomFlowState State { get; private set; } = RoomFlowState.EnteringRoom;
        public bool GameplayActive { get; private set; }
        public bool IsTerminal => Graph.GetRoom(CurrentRoomId).IsTerminal;
        public int RoomsEntered { get; private set; }
        public string Failure { get; private set; }
        public IReadOnlyList<RoomConnection> Choices { get; private set; }

        public RunProgression(DungeonGraph graph, PlayerIdentity player)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            if (player.Value <= 0) throw new ArgumentException("An explicit player identity is required.", nameof(player));
            Player = player;
            CurrentRoomId = graph.StartRoomId;
            Choices = ConnectionsFrom(CurrentRoomId);
        }

        public void SetGameplayActive(bool active) => GameplayActive = active;

        // Preserve the graph's connection order: index 0 is left, index 1 is right.
        public IReadOnlyList<RoomConnection> ConnectionsFrom(RunRoomId room)
        {
            Graph.GetRoom(room);
            var connections = new List<RoomConnection>(2);
            foreach (var edge in Graph.Connections) if (edge.From.Equals(room)) connections.Add(edge);
            if (connections.Count > 2) throw new InvalidOperationException("Only zero, one, or two forward exits are supported.");
            return connections.AsReadOnly();
        }

        public bool TryRequestExit(ExitIntent intent)
        {
            if (!GameplayActive || State != RoomFlowState.Playing || IsTerminal
                || intent.Player.Value <= 0 || !Player.Equals(intent.Player) || !CurrentRoomId.Equals(intent.CurrentRoom)) return false;
            foreach (var choice in Choices)
            {
                if (!choice.To.Equals(intent.Destination)) continue;
                AcceptedDestination = choice.To;
                State = RoomFlowState.LeavingRoom;
                return true;
            }
            return false;
        }

        public bool TryBeginAcceptedEntry()
        {
            if (!GameplayActive || State != RoomFlowState.LeavingRoom || !AcceptedDestination.HasValue) return false;
            State = RoomFlowState.EnteringRoom;
            return true;
        }

        public bool TryCompleteEntry(RunRoomId room)
        {
            if (State != RoomFlowState.EnteringRoom) return false;
            if (AcceptedDestination.HasValue)
            {
                if (!GameplayActive || !AcceptedDestination.Value.Equals(room)) return false;
            }
            else if (RoomsEntered != 0 || !CurrentRoomId.Equals(room)) return false;
            CurrentRoomId = room;
            AcceptedDestination = null;
            Choices = ConnectionsFrom(room);
            RoomsEntered++;
            State = IsTerminal ? RoomFlowState.Terminal : RoomFlowState.Playing;
            return true;
        }

        public void Fail(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A failure reason is required.", nameof(reason));
            Failure = reason;
            State = RoomFlowState.Failed;
            GameplayActive = false;
        }
    }
}
