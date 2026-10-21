using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>Two-player coordination before RunProgression acceptance; never changes current room.</summary>
    public sealed class ExitReadiness
    {
        private readonly Dictionary<int, RunRoomId> ready = new Dictionary<int, RunRoomId>();
        private readonly HashSet<RunRoomId> destinations = new HashSet<RunRoomId>();
        public RunRoomId CurrentRoom { get; private set; }
        public bool BothConnected { get; private set; }

        public void Reset(RunRoomId room, IReadOnlyList<RoomConnection> choices, bool bothConnected)
        {
            Clear(); destinations.Clear(); CurrentRoom = room; BothConnected = bothConnected;
            foreach (var edge in choices)
            {
                if (!edge.From.Equals(room)) throw new ArgumentException("Readiness choices must originate in the current room.");
                destinations.Add(edge.To);
            }
        }
        public bool SetReady(ExitIntent intent, bool present)
        {
            if (!BothConnected || (intent.Player.Value != 1 && intent.Player.Value != 2)
                || !intent.CurrentRoom.Equals(CurrentRoom) || !destinations.Contains(intent.Destination)) return false;
            if (present) ready[intent.Player.Value] = intent.Destination;
            else if (ready.TryGetValue(intent.Player.Value, out var previous) && previous.Equals(intent.Destination)) ready.Remove(intent.Player.Value);
            return true;
        }
        public int Count(RunRoomId destination)
        {
            var count = 0;
            foreach (var value in ready.Values) if (value.Equals(destination)) count++;
            return count;
        }
        public bool TryGetSharedDestination(out RunRoomId destination)
        {
            destination = default;
            return BothConnected && ready.TryGetValue(1, out destination) && ready.TryGetValue(2, out var other) && destination.Equals(other);
        }
        public void Clear() => ready.Clear();
        public void Disconnect() { BothConnected = false; Clear(); }
    }
}
