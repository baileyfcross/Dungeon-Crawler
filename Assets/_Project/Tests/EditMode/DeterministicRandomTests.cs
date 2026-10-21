using System;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class DeterministicRandomTests
    {
        [Test]
        public void SplitMix64ReferenceSequenceAndExplicitStateAreStable()
        {
            var random = new DeterministicRandom(0);
            // Reference SplitMix64 seed-0 outputs, reduced modulo int.MaxValue:
            // e220a8397b1dcdaf, 6e789e6aa1b965f4, 06c45d188009454f, f88bb8a8724c81ec, 1b39896a51a8749b.
            var expected = new[] { 1063198245, 2125112010, 227671936, 1667494720, 136021872 };
            foreach (var value in expected) Assert.That(random.NextInt(int.MaxValue), Is.EqualTo(value));
            Assert.That(random.State, Is.EqualTo(unchecked(5UL * 0x9e3779b97f4a7c15UL)));
            var resumed = new DeterministicRandom(random.State);
            for (var i = 0; i < 50; i++) Assert.That(resumed.NextInt(37), Is.EqualTo(random.NextInt(37)));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(17)]
        [TestCase(int.MaxValue)]
        public void BoundedSelectionNeverLeavesItsValidatedInterval(int bound)
        {
            var random = new DeterministicRandom(ulong.MaxValue);
            for (var i = 0; i < 4096; i++) Assert.That(random.NextInt(bound), Is.InRange(0, bound - 1));
        }

        [Test]
        public void IncompleteModuloIntervalIsRejectedInsteadOfBiasingSelection()
        {
            // The first state becomes zero, so its mixed output is zero. For bound 3, threshold is 1.
            var random = new DeterministicRandom(unchecked(0UL - 0x9e3779b97f4a7c15UL));
            Assert.That(random.NextInt(3), Is.EqualTo(1));
            Assert.That(random.State, Is.EqualTo(0x9e3779b97f4a7c15UL), "The rejected zero must consume a draw.");
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidBoundsFailWithoutAdvancingState(int bound)
        {
            var random = new DeterministicRandom(42);
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(bound));
            Assert.That(random.State, Is.EqualTo(42UL));
        }
    }
}
