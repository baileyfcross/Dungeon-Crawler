using SpaceCrawler.Core;
using UnityEngine;
namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName="Space Crawler/Run Rewards")]
    public sealed class RunRewardDefinition : ScriptableObject
    {
        [SerializeField,Min(1)] private int baseXp=RunLife.BaseRunXpGain;
        [SerializeField,Min(.01f)] private float melee=1,ranged=1.25f,levelOne=1,levelTwo=1.25f,levelThree=1.5f,easy=.75f,normal=1,hard=1.5f;
        public RunRewardPolicy CreatePolicy()=>new RunRewardPolicy(baseXp,new[]{melee,ranged,levelOne,levelTwo,levelThree,easy,normal,hard});
    }
}
