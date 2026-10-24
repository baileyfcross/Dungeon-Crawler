using System;

namespace SpaceCrawler.Core
{
    // Legacy keeps already saved encounters unchanged; new runs choose a named difficulty.
    public enum RunDifficulty { Legacy = 0, Easy = 1, Normal = 2, Hard = 3 }

    public static class DifficultyRules
    {
        public static string DisplayName(RunDifficulty difficulty)
        { Factor(difficulty);return difficulty==RunDifficulty.Legacy?"Standard":difficulty.ToString(); }
        public static float Factor(RunDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RunDifficulty.Legacy: case RunDifficulty.Normal: return 1;
                case RunDifficulty.Easy: return .75f;
                case RunDifficulty.Hard: return 1.5f;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        public static int Scale(int baseline, RunDifficulty difficulty)
        {
            if (baseline < 1 || baseline > 1000000) throw new ArgumentOutOfRangeException(nameof(baseline));
            return Math.Max(1, (int)Math.Round(baseline * (double)Factor(difficulty), MidpointRounding.AwayFromZero));
        }

        public static int EnemyCount(int baseline, int capacity, RunDifficulty difficulty, bool startingRoom)
        {
            if (capacity < 0 || capacity > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (startingRoom || capacity == 0) return 0;
            return Math.Min(capacity, Scale(baseline, difficulty));
        }
    }
}
