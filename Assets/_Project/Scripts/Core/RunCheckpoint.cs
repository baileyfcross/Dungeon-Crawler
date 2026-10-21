using System;
using System.Collections.Generic;

namespace SpaceCrawler.Core
{
    public enum SavedSessionMode { Solo = 1, Cooperative = 2 }
    public enum SavedRunStatus { Active = 1 }

    /// <summary>Immutable room-entry data. Player slots are stable gameplay identities, not network IDs.</summary>
    public sealed class RunCheckpoint
    {
        public const int SchemaVersion = 3;
        public RunLifeSnapshot Life { get; }
        public int SourceSchemaVersion { get; }
        public ulong RunSeed { get; }
        public int SequenceVersion { get; }
        public int LevelNumber { get; }
        public IReadOnlyList<ulong> LevelSeeds { get; }
        public IReadOnlyList<string> LevelFingerprints { get; }
        public IReadOnlyList<IReadOnlyList<RunRoomId>> CompletedRoutes { get; }
        public ulong Seed { get; }
        public int GenerationVersion { get; }
        public string Fingerprint { get; }
        public SavedSessionMode Mode { get; }
        public SavedRunStatus Status => SavedRunStatus.Active;
        public RunRoomId CurrentRoom { get; }
        public IReadOnlyList<RunRoomId> RoomVisits { get; }
        // Index zero is PlayerIdentity 1; index one is PlayerIdentity 2 in cooperative sessions.
        public IReadOnlyList<string> EquipmentIds { get; }

        public RunCheckpoint(ulong seed, int generationVersion, string fingerprint, SavedSessionMode mode,
            RunRoomId currentRoom, IReadOnlyList<RunRoomId> roomVisits, IReadOnlyList<string> equipmentIds,
            ulong? runSeed = null, int sequenceVersion = 0, int levelNumber = 1, IReadOnlyList<ulong> levelSeeds = null,
            IReadOnlyList<string> levelFingerprints = null, IReadOnlyList<IReadOnlyList<RunRoomId>> completedRoutes = null,
            RunLifeSnapshot life = null, int sourceSchemaVersion = SchemaVersion)
        {
            if (sequenceVersion != 0 && sequenceVersion != RunSequence.CurrentVersion) throw new NotSupportedException("Saved level policy is not supported.");
            var levelCount = sequenceVersion == 0 ? 1 : RunSequence.LevelCount;
            if (levelNumber < 1 || levelNumber > levelCount) throw new ArgumentOutOfRangeException(nameof(levelNumber));
            levelSeeds = levelSeeds ?? new[] { seed }; levelFingerprints = levelFingerprints ?? new[] { fingerprint };
            completedRoutes = completedRoutes ?? Array.Empty<IReadOnlyList<RunRoomId>>();
            if (levelSeeds.Count != levelCount || levelFingerprints.Count != levelCount || completedRoutes.Count != levelNumber - 1)
                throw new ArgumentException("Saved level metadata is inconsistent.");
            var seeds = new ulong[levelCount]; var fingerprints = new string[levelCount];
            for (var i = 0; i < levelCount; i++)
            { seeds[i] = levelSeeds[i]; ValidateFingerprint(levelFingerprints[i]); fingerprints[i] = levelFingerprints[i]; }
            var histories = new IReadOnlyList<RunRoomId>[completedRoutes.Count];
            for (var i = 0; i < histories.Length; i++)
            {
                if (completedRoutes[i] == null || completedRoutes[i].Count < 1 || completedRoutes[i].Count > 4096)
                    throw new ArgumentException("Saved completed room history is invalid.");
                histories[i] = Array.AsReadOnly(new List<RunRoomId>(completedRoutes[i]).ToArray());
            }
            RunSeed = runSeed ?? seed; SequenceVersion = sequenceVersion; LevelNumber = levelNumber;
            LevelSeeds = Array.AsReadOnly(seeds); LevelFingerprints = Array.AsReadOnly(fingerprints); CompletedRoutes = Array.AsReadOnly(histories);
            if (generationVersion <= 0) throw new ArgumentOutOfRangeException(nameof(generationVersion));
            if (fingerprint == null || fingerprint.Length != 64) throw new ArgumentException("Invalid graph fingerprint.", nameof(fingerprint));
            foreach (var character in fingerprint)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                    throw new ArgumentException("Invalid graph fingerprint.", nameof(fingerprint));
            if (mode != SavedSessionMode.Solo && mode != SavedSessionMode.Cooperative) throw new ArgumentOutOfRangeException(nameof(mode));
            if (roomVisits == null || roomVisits.Count == 0 || roomVisits.Count > 4096)
                throw new ArgumentException("A bounded room history is required.", nameof(roomVisits));
            if (!roomVisits[roomVisits.Count - 1].Equals(currentRoom))
                throw new ArgumentException("Checkpoint room does not match room history.", nameof(currentRoom));
            if (equipmentIds == null || equipmentIds.Count != (int)mode)
                throw new ArgumentException("Equipment must match the session's player slots.", nameof(equipmentIds));
            var visits = new RunRoomId[roomVisits.Count];
            for (var i = 0; i < visits.Length; i++) visits[i] = roomVisits[i];
            var equipment = new string[equipmentIds.Count];
            for (var i = 0; i < equipment.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(equipmentIds[i]) || equipmentIds[i].Length > 128)
                    throw new ArgumentException("Invalid equipment identity.", nameof(equipmentIds));
                equipment[i] = equipmentIds[i];
            }
            Seed = seed; GenerationVersion = generationVersion; Fingerprint = fingerprint; Mode = mode;
            CurrentRoom = currentRoom; RoomVisits = Array.AsReadOnly(visits); EquipmentIds = Array.AsReadOnly(equipment);
            Life = life ?? RunLifeSnapshot.Fresh((int)mode); SourceSchemaVersion = sourceSchemaVersion;
            if (Life.Players.Count != (int)mode || new RunLife(Life).Ended)
                throw new ArgumentException("Only an unfinished run can be checkpointed.");
        }

        private static void ValidateFingerprint(string value)
        {
            if (value == null || value.Length != 64) throw new ArgumentException("Invalid level fingerprint.");
            foreach (var c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new ArgumentException("Invalid level fingerprint.");
        }

        public static RunCheckpoint Capture(RunSequence sequence, RunProgression progression, SavedSessionMode mode, IReadOnlyList<string> equipment, RunLifeSnapshot life = null)
        {
            if (sequence == null || !ReferenceEquals(sequence.CurrentGraph, progression.Graph)
                || (progression.State != RoomFlowState.Playing && progression.State != RoomFlowState.Terminal))
                throw new InvalidOperationException("Only a successfully entered room can be saved.");
            var seeds = new ulong[RunSequence.LevelCount]; var fingerprints = new string[RunSequence.LevelCount];
            for (var i = 0; i < seeds.Length; i++) { seeds[i] = sequence.Levels[i].Seed; fingerprints[i] = GraphFingerprint.Compute(sequence.Levels[i]); }
            return new RunCheckpoint(progression.Graph.Seed, progression.Graph.GenerationVersion, GraphFingerprint.Compute(progression.Graph),
                mode, progression.CurrentRoomId, progression.VisitedRooms, equipment, sequence.Seed, RunSequence.CurrentVersion,
                sequence.LevelNumber, seeds, fingerprints, sequence.CompletedRoutes, life);
        }
    }
}
