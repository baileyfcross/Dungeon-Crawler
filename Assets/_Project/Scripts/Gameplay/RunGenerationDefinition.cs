using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName = "Space Crawler/Run Generation Definition")]
    public sealed class RunGenerationDefinition : ScriptableObject
    {
        [SerializeField, Min(2)] private int minRouteRooms = 9;
        [SerializeField, Min(2)] private int maxRouteRooms = 11;
        [SerializeField, Min(0)] private int maxSplits = 3;
        [SerializeField] private RoomDefinition[] roomDefinitions;
        public IReadOnlyList<RoomDefinition> RoomDefinitions => Array.AsReadOnly(roomDefinitions);

        public RunGenerationConfiguration CreateConfiguration()
        {
            if (roomDefinitions == null) throw new InvalidOperationException("Missing room definitions.");
            var snapshots = new RoomConfiguration[roomDefinitions.Length];
            for (var i = 0; i < snapshots.Length; i++)
            {
                if (roomDefinitions[i] == null) throw new InvalidOperationException("Missing room definition reference.");
                snapshots[i] = roomDefinitions[i].CreateConfiguration();
            }
            return new RunGenerationConfiguration(new RunGenerationSettings(minRouteRooms, maxRouteRooms, maxSplits), snapshots);
        }
    }
}
