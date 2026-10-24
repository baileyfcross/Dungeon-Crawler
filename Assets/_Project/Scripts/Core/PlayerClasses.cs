using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>Stable class identities only. Abilities, equipment and progression are separate policies.</summary>
    public static class PlayerClasses
    {
        public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[]
            { "pilot", "mechanic", "doctor", "first-mate", "logistics-manager", "captain" });
        public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
            { "Pilot", "Mechanic", "Doctor", "First-Mate", "Logistics Manager", "Captain" });
        public static bool Contains(string id)
        { for (int i=0;i<Ids.Count;i++) if (Ids[i]==id) return true; return false; }
    }
}
