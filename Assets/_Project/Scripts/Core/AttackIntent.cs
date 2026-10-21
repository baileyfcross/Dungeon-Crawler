using System.Numerics;

namespace SpaceCrawler.Core
{
    public readonly struct AttackIntent
    {
        public PlayerIdentity Player { get; }
        public Vector2 AimDirection { get; }
        public AttackIntent(PlayerIdentity player, Vector2 acceptedAimDirection)
        { Player = player; AimDirection = acceptedAimDirection; }
    }

    /// <summary>A validated execution captures equipment, origin and aim at acceptance time.</summary>
    public sealed class AttackExecution
    {
        public PlayerIdentity Attacker { get; }
        public long Sequence { get; }
        public WeaponConfiguration Weapon { get; }
        public Vector2 Origin { get; }
        public Vector2 AimDirection { get; }
        internal AttackExecution(PlayerIdentity attacker, long sequence, WeaponConfiguration weapon, Vector2 origin, Vector2 direction)
        { Attacker = attacker; Sequence = sequence; Weapon = weapon; Origin = origin; AimDirection = direction; }
    }
}
