using System;

namespace SpaceCrawler.Core
{
    public readonly struct DamageableIdentity : IEquatable<DamageableIdentity>
    {
        public int Value { get; }
        public DamageableIdentity(int value)
        { if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
        public bool Equals(DamageableIdentity other) => Value == other.Value;
        public override bool Equals(object obj) => obj is DamageableIdentity other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    public readonly struct DamageRequest
    {
        public PlayerIdentity Attacker { get; }
        public long AttackSequence { get; }
        public DamageableIdentity Target { get; }
        public int Amount { get; }
        public DamageRequest(PlayerIdentity attacker, long attackSequence, DamageableIdentity target, int amount)
        { Attacker = attacker; AttackSequence = attackSequence; Target = target; Amount = amount; }
    }

    public readonly struct DamageResult
    {
        public DamageRequest Request { get; }
        public int AppliedDamage { get; }
        public int RemainingHealth { get; }
        public DamageResult(DamageRequest request, int appliedDamage, int remainingHealth)
        { Request = request; AppliedDamage = appliedDamage; RemainingHealth = remainingHealth; }
    }

    /// <summary>Runtime health only. World resolution is responsible for each execution's hit set.</summary>
    public sealed class HealthSimulation
    {
        public DamageableIdentity Identity { get; }
        public int Maximum { get; }
        public int Current { get; private set; }
        public bool IsAlive => Current > 0;

        public HealthSimulation(DamageableIdentity identity, int maximum)
        {
            if (identity.Value <= 0) throw new ArgumentException("A target identity is required.", nameof(identity));
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            Identity = identity; Maximum = maximum; Current = maximum;
        }

        public bool TryApplyDamage(DamageRequest request, out DamageResult result)
        {
            result = default;
            if (!IsAlive || !Identity.Equals(request.Target) || request.Attacker.Value <= 0
                || request.AttackSequence <= 0 || request.Amount <= 0) return false;
            var applied = Math.Min(Current, request.Amount);
            Current -= applied;
            result = new DamageResult(request, applied, Current);
            return true;
        }
    }
}
