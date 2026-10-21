using System;

namespace SpaceCrawler.Core
{
    /// <summary>Route lengths count rooms, including Starting and Terminal. MaxSplits counts diamonds.</summary>
    public sealed class RunGenerationSettings
    {
        // An allocation guard, not a game balance target.
        public const int MaximumRouteRooms = 1024;
        public int MinRouteRooms { get; }
        public int MaxRouteRooms { get; }
        public int MaxSplits { get; }

        public RunGenerationSettings(int minRouteRooms, int maxRouteRooms, int maxSplits)
        {
            if (minRouteRooms < 2 || minRouteRooms > MaximumRouteRooms)
                throw new ArgumentOutOfRangeException(nameof(minRouteRooms));
            if (maxRouteRooms < minRouteRooms || maxRouteRooms > MaximumRouteRooms)
                throw new ArgumentOutOfRangeException(nameof(maxRouteRooms));
            if (maxSplits < 0 || maxSplits > (maxRouteRooms - 1) / 2)
                throw new ArgumentOutOfRangeException(nameof(maxSplits), "Splits must fit non-adjacent interior layers of the longest route.");
            MinRouteRooms = minRouteRooms; MaxRouteRooms = maxRouteRooms; MaxSplits = maxSplits;
        }
    }
}
