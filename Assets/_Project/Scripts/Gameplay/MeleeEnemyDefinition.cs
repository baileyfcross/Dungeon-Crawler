using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName = "Space Crawler/Melee Enemy")]
    public sealed class MeleeEnemyDefinition : ScriptableObject
    {
        [SerializeField, Min(.01f)] private float speed = 3, detection = 7, reach = 1.5f, windup = .6f, recovery = .8f, lostTargetSeconds = 3;
        [SerializeField, Min(1)] private int damage = 20;
        public MeleeEnemyConfiguration CreateConfiguration()
            => new MeleeEnemyConfiguration(speed, detection, reach, windup, recovery, lostTargetSeconds, damage);
    }
}
