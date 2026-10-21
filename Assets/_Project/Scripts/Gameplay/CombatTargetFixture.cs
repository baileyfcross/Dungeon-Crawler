using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Explicit combat-test content, never spawned by ordinary gameplay.</summary>
    public sealed class CombatTargetFixture : MonoBehaviour
    {
        [SerializeField] private DamageableTarget[] targets;
        public DamageableTarget[] Targets => (DamageableTarget[])targets.Clone();
    }
}
