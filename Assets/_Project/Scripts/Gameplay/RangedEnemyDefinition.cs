using SpaceCrawler.Core;
using UnityEngine;
namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName="Space Crawler/Ranged Enemy")]
    public sealed class RangedEnemyDefinition : ScriptableObject
    {
        [SerializeField,Min(1)] private int damage=10;
        [SerializeField,Min(.01f)] private float speed=3,detection=9,minimumRange=4,maximumRange=6,windup=.75f,recovery=1.5f,projectileSpeed=8;
        public RangedEnemyConfiguration CreateConfiguration(RunDifficulty difficulty=RunDifficulty.Normal)
            =>new RangedEnemyConfiguration(DifficultyRules.Scale(damage,difficulty),speed,detection,minimumRange,maximumRange,windup,recovery,projectileSpeed);
    }
}
