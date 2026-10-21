using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    [CreateAssetMenu(menuName = "Space Crawler/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private AttackKind attackKind;
        [SerializeField, Min(1)] private int damage = 25;
        [SerializeField, Min(0.01f)] private float cooldown = 0.5f;
        [Header("Melee only")]
        [SerializeField, Min(0.01f)] private float meleeRange = 2;
        [SerializeField, Range(1, 360)] private float meleeArcDegrees = 90;
        [Header("Projectile only")]
        [SerializeField, Min(0.01f)] private float projectileSpeed = 9;
        [SerializeField, Min(0.01f)] private float projectileLifetime = 2;
        public string DisplayName => displayName;

        public WeaponConfiguration CreateConfiguration()
        {
            switch (attackKind)
            {
                case AttackKind.Melee: return WeaponConfiguration.Melee(stableId, damage, cooldown, meleeRange, meleeArcDegrees);
                case AttackKind.Projectile: return WeaponConfiguration.Projectile(stableId, damage, cooldown, projectileSpeed, projectileLifetime);
                default: throw new InvalidOperationException("Unsupported attack kind in " + name);
            }
        }
    }
}
