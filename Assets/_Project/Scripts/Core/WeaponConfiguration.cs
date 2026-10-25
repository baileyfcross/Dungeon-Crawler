using System;

namespace SpaceCrawler.Core
{
    public enum AttackKind { Melee, Projectile }

    /// <summary>Immutable configuration copied from a definition, never a home for runtime cooldowns.</summary>
    public sealed class WeaponConfiguration
    {
        public string Id { get; }
        public AttackKind Kind { get; }
        public int Damage { get; }
        public float Cooldown { get; }
        public float MeleeRange { get; }
        public float MeleeArcDegrees { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetime { get; }

        private WeaponConfiguration(string id, AttackKind kind, int damage, float cooldown, float range, float arc, float speed, float lifetime)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A stable weapon ID is required.", nameof(id));
            if (damage <= 0) throw new ArgumentOutOfRangeException(nameof(damage));
            RequirePositive(cooldown, nameof(cooldown));
            if (kind == AttackKind.Melee)
            {
                RequirePositive(range, nameof(range));
                RequirePositive(arc, nameof(arc));
                if (arc > 360) throw new ArgumentOutOfRangeException(nameof(arc));
            }
            else { RequirePositive(speed, nameof(speed)); RequirePositive(lifetime, nameof(lifetime)); }
            Id = id; Kind = kind; Damage = damage; Cooldown = cooldown;
            MeleeRange = range; MeleeArcDegrees = arc; ProjectileSpeed = speed; ProjectileLifetime = lifetime;
        }

        public static WeaponConfiguration Melee(string id, int damage, float cooldown, float range, float arcDegrees)
            => new WeaponConfiguration(id, AttackKind.Melee, damage, cooldown, range, arcDegrees, 0, 0);

        public WeaponConfiguration WithDamageBonus(int percent)
        {
            if(percent<0 || percent>40)throw new ArgumentOutOfRangeException(nameof(percent));
            int damage=checked((int)(((long)Damage*(100+percent)+50)/100));
            return new WeaponConfiguration(Id,Kind,damage,Cooldown,MeleeRange,MeleeArcDegrees,ProjectileSpeed,ProjectileLifetime);
        }
        public static WeaponConfiguration Projectile(string id, int damage, float cooldown, float speed, float lifetime)
            => new WeaponConfiguration(id, AttackKind.Projectile, damage, cooldown, 0, 0, speed, lifetime);

        private static void RequirePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
        }
    }
}
