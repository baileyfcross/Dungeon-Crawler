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
        private readonly List<RunRoomId> visits = new List<RunRoomId>();
        private bool restoring;
        public IReadOnlyList<RunRoomId> VisitedRooms => visits.AsReadOnly();

        public static void ValidateRoute(DungeonGraph graph, IReadOnlyList<RunRoomId> history)
        {
            if (history == null || history.Count == 0 || history.Count > (graph.GenerationVersion == 1 ? graph.Nodes.Count : 4096) || !history[0].Equals(graph.StartRoomId))
                throw new ArgumentException("Saved room history has an invalid start or length.");
            for (var i = 0; i < history.Count; i++)
            {
                graph.GetRoom(history[i]);
                if (i == 0) continue;
                var connected = false;
                foreach (var edge in graph.Connections)
                    if (edge.From.Equals(history[i - 1]) && edge.To.Equals(history[i])) { connected = true; break; }
                if (!connected) throw new ArgumentException("Saved room history contains an invalid exit.");
            }
        }

        public static RunProgression Restore(DungeonGraph graph, PlayerIdentity player, IReadOnlyList<RunRoomId> history)
        {
            ValidateRoute(graph, history);
            var result = new RunProgression(graph, player) { CurrentRoomId = history[history.Count - 1], restoring = true };
            for (var i = 0; i < history.Count - 1; i++) result.visits.Add(history[i]);
            result.RoomsEntered = result.visits.Count;
            result.Choices = result.ConnectionsFrom(result.CurrentRoomId);
            return result;
        }

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
            if (connections.Count > (Graph.GenerationVersion == 1 ? 2 : 4)) throw new InvalidOperationException("Only zero, one, or two forward exits are supported.");
            return connections.AsReadOnly();
        }

        public bool TryRequestExit(ExitIntent intent)
        {
            if (!GameplayActive || (State != RoomFlowState.Playing && !(Graph.GenerationVersion >= 2 && State == RoomFlowState.Terminal))
                || (IsTerminal && Graph.GenerationVersion == 1) || visits.Count >= 4096
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
            else if ((!restoring && RoomsEntered != 0) || !CurrentRoomId.Equals(room)) return false;
            CurrentRoomId = room;
            AcceptedDestination = null;
            Choices = ConnectionsFrom(room);
            RoomsEntered++;
            visits.Add(room); restoring = false;
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
