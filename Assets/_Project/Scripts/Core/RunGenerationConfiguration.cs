using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    /// <summary>An immutable, canonical snapshot of every setting and definition affecting generation.</summary>
    public sealed class RunGenerationConfiguration
    {
        public RunGenerationSettings Settings { get; }
        public IReadOnlyList<RoomConfiguration> Definitions { get; }
        internal int StartingWeight { get; }
        internal int RegularWeight { get; }

        public RunGenerationConfiguration(RunGenerationSettings settings, IEnumerable<RoomConfiguration> definitions)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var ordered = new List<RoomConfiguration>();
            foreach (var definition in definitions)
            {
                if (definition == null) throw new ArgumentException("Null room definition.", nameof(definitions));
                ordered.Add(definition);
            }
            ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            long starting = 0, regular = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var definition = ordered[i];
                if (i > 0 && StringComparer.Ordinal.Equals(ordered[i - 1].Id, definition.Id))
                    throw new ArgumentException("Duplicate room definition ID: " + definition.Id, nameof(definitions));
                if (definition.Category == RoomCategory.Starting) starting += definition.Weight;
                else regular += definition.Weight;
                if (starting > int.MaxValue || regular > int.MaxValue)
                    throw new ArgumentException("Total candidate weight exceeds the supported integer bound.", nameof(definitions));
            }
            if (starting == 0 || regular == 0)
                throw new ArgumentException("At least one Starting and one non-Starting definition are required.", nameof(definitions));
            Definitions = ordered.AsReadOnly();
            StartingWeight = (int)starting; RegularWeight = (int)regular;
        }
    }
}
