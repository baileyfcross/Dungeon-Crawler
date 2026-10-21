using System;

namespace SpaceCrawler.Core
{
    /// <summary>Project-owned SplitMix64 with explicit state and unbiased bounded integer selection.</summary>
    public sealed class DeterministicRandom
    {
        public const string Algorithm = "SplitMix64-v1";
        public ulong State { get; private set; }
        public DeterministicRandom(ulong state) => State = state;

        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            var bound = (ulong)exclusiveUpperBound;
            // Reject the incomplete residue interval; modulo alone would bias the result.
            var threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do { value = NextUInt64(); } while (value < threshold);
            return (int)(value % bound);
        }

        private ulong NextUInt64()
        {
            // Sebastiano Vigna's 2015 public-domain SplitMix64 algorithm:
            // https://xorshift.di.unimi.it/splitmix64.c
            // Unsigned wraparound and all constants/shift counts are part of version 1.
            unchecked
            {
                State += 0x9e3779b97f4a7c15UL;
                var value = State;
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }
    }
}
