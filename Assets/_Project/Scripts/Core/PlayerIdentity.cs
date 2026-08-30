using System;

namespace SpaceCrawler.Core
{
    public readonly struct PlayerIdentity : IEquatable<PlayerIdentity>
    {
        public int Value { get; }
        public PlayerIdentity(int value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }
        public bool Equals(PlayerIdentity other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerIdentity other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }
}
