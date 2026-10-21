using System;

namespace SpaceCrawler.Core
{
    public enum RoomCategory { Starting, Engine, Cockpit, Storage, Generator, Lab }

    /// <summary>Static authored room identity/category/weight, without a physical room or runtime state.</summary>
    public sealed class RoomConfiguration
    {
        public string Id { get; }
        public RoomCategory Category { get; }
        public int Weight { get; }

        public RoomConfiguration(string id, RoomCategory category, int weight = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
                throw new ArgumentException("A nonempty stable definition ID without surrounding whitespace is required.", nameof(id));
            if (!Enum.IsDefined(typeof(RoomCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
            if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
            Id = id; Category = category; Weight = weight;
        }
    }
}
