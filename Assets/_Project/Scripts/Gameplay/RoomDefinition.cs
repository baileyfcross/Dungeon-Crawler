using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName = "Space Crawler/Room Definition")]
    public sealed class RoomDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private RoomCategory category;
        [SerializeField, Min(1)] private int generationWeight = 1;
        [SerializeField] private AuthoredRoom roomPrefab;
        public AuthoredRoom RoomPrefab => roomPrefab;

        public RoomConfiguration CreateConfiguration() => new RoomConfiguration(stableId, category, generationWeight);
    }
}
