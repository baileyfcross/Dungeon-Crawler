using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class RunCheckpointTests
    {
        private string directory;
        private RunCheckpointStore store;
        private static RunCheckpoint Checkpoint(ulong seed = 42, SavedSessionMode mode = SavedSessionMode.Solo)
            => new RunCheckpoint(seed, 1, new string('a', 64), mode, new RunRoomId(3),
                new[] { new RunRoomId(0), new RunRoomId(1), new RunRoomId(3) },
                mode == SavedSessionMode.Solo ? new[] { "weapon.knife" } : new[] { "weapon.knife", "weapon.blaster" });

        [SetUp]
        public void SetUp()
        {
            directory = Path.GetFullPath(Path.Combine("Logs", "Validation", "Saves", "TestData", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            store = new RunCheckpointStore(Path.Combine(directory, "run.save"));
        }

        [TestCase(SavedSessionMode.Solo)]
        [TestCase(SavedSessionMode.Cooperative)]
        public void RoundTripRetainsExactSeedAndStableGameplayIdentities(SavedSessionMode mode)
        {
            var original = Checkpoint(ulong.MaxValue, mode);
            var loaded = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(original));
            Assert.That(loaded.Seed, Is.EqualTo(ulong.MaxValue));
            Assert.That(loaded.GenerationVersion, Is.EqualTo(1));
            Assert.That(loaded.Fingerprint, Is.EqualTo(original.Fingerprint));
            Assert.That(loaded.CurrentRoom, Is.EqualTo(original.CurrentRoom));
            Assert.That(loaded.Mode, Is.EqualTo(mode));
            Assert.That(loaded.Status, Is.EqualTo(SavedRunStatus.Active));
            Assert.That(loaded.RoomVisits, Is.EqualTo(original.RoomVisits));
            Assert.That(loaded.EquipmentIds, Is.EqualTo(original.EquipmentIds));
        }

        [Test]
        public void SnapshotOwnsItsDataAndRejectsInconsistentPlayerOrRoomData()
        {
            var visits = new[] { new RunRoomId(0) };
            var equipment = new[] { "weapon.knife" };
            var snapshot = new RunCheckpoint(1, 1, new string('b', 64), SavedSessionMode.Solo, visits[0], visits, equipment);
            visits[0] = new RunRoomId(99); equipment[0] = "weapon.blaster";
            Assert.That(snapshot.RoomVisits[0].Value, Is.Zero);
            Assert.That(snapshot.EquipmentIds[0], Is.EqualTo("weapon.knife"));
            Assert.Throws<ArgumentException>(() => new RunCheckpoint(1, 1, new string('b', 64), SavedSessionMode.Solo,
                new RunRoomId(1), new[] { new RunRoomId(0) }, new[] { "weapon.knife" }));
            Assert.Throws<ArgumentException>(() => new RunCheckpoint(1, 1, new string('b', 64), SavedSessionMode.Cooperative,
                new RunRoomId(0), new[] { new RunRoomId(0) }, new[] { "weapon.knife" }));
        }

        [Test]
        public void DamageAndUnsupportedVersionsAreReportedWithoutChangingEvidence()
        {
            var bytes = RunCheckpointCodec.Encode(Checkpoint());
            bytes[20] ^= 1;
            File.WriteAllBytes(store.FilePath, bytes);
            Assert.That(store.Load(out var checkpoint, out var error), Is.EqualTo(CheckpointLoadStatus.Invalid));
            Assert.That(checkpoint, Is.Null); Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(store.FilePath), Is.EqualTo(bytes));
            bytes = RunCheckpointCodec.Encode(Checkpoint());
            bytes[4] = 99;
            File.WriteAllBytes(store.FilePath, bytes);
            Assert.That(store.Load(out _, out error), Is.EqualTo(CheckpointLoadStatus.Incompatible));
            Assert.That(error, Does.Contain("version"));
            Assert.That(File.ReadAllBytes(store.FilePath), Is.EqualTo(bytes));
        }

        [TestCase(0)]
        [TestCase(20)]
        [TestCase(44)]
        public void TruncatedFilesCannotBecomeCheckpoints(int length)
        {
            var bytes = RunCheckpointCodec.Encode(Checkpoint()).Take(length).ToArray();
            Assert.Throws<InvalidDataException>(() => RunCheckpointCodec.Decode(bytes));
        }

        [Test]
        public void OversizedAndTrailingDataAreRejected()
        {
            File.WriteAllBytes(store.FilePath, new byte[RunCheckpointCodec.MaximumFileBytes + 1]);
            Assert.That(store.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Invalid));
            var bytes = RunCheckpointCodec.Encode(Checkpoint()).Concat(new byte[] { 0 }).ToArray();
            Assert.Throws<InvalidDataException>(() => RunCheckpointCodec.Decode(bytes));
        }

        [Test]
        public void CommittedSaveSurvivesInterruptedPendingWriteAndReplacementKeepsBackup()
        {
            Assert.That(store.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Missing));
            store.Save(Checkpoint(42));
            File.WriteAllBytes(store.PendingPath, new byte[] { 1, 2, 3 });
            Assert.That(store.Load(out var restored, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(restored.Seed, Is.EqualTo(42));
            store.Save(Checkpoint(84));
            Assert.That(store.Load(out restored, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(restored.Seed, Is.EqualTo(84));
            Assert.That(RunCheckpointCodec.Decode(File.ReadAllBytes(store.BackupPath)).Seed, Is.EqualTo(42));
            Assert.That(File.Exists(store.PendingPath), Is.False);
        }

        [Test]
        public void FirstInterruptedWriteDoesNotEnableContinue()
        {
            File.WriteAllBytes(store.PendingPath, RunCheckpointCodec.Encode(Checkpoint()));
            Assert.That(store.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Missing));
            store.Save(Checkpoint(84));
            Assert.That(store.Load(out var loaded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(loaded.Seed, Is.EqualTo(84));
        }

        [Test]
        public void ExplicitNewSavePreservesCorruptEvidenceAndPreviousValidBackup()
        {
            store.Save(Checkpoint(42)); store.Save(Checkpoint(84));
            var broken = new byte[] { 1, 2, 3 };
            File.WriteAllBytes(store.FilePath, broken);
            store.Save(Checkpoint(126));
            Assert.That(File.ReadAllBytes(Directory.GetFiles(directory, "*.invalid.*").Single()), Is.EqualTo(broken));
            Assert.That(RunCheckpointCodec.Decode(File.ReadAllBytes(store.BackupPath)).Seed, Is.EqualTo(42));
            Assert.That(store.Load(out var loaded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(loaded.Seed, Is.EqualTo(126));
        }

        [Test]
        public void ConcurrentRequestsCommitWholeSnapshots()
        {
            Task.WaitAll(Task.Run(() => store.Save(Checkpoint(42))), Task.Run(() => store.Save(Checkpoint(84))));
            Assert.That(store.Load(out var loaded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            var backup = RunCheckpointCodec.Decode(File.ReadAllBytes(store.BackupPath));
            Assert.That(new[] { loaded.Seed, backup.Seed }, Is.EquivalentTo(new ulong[] { 42, 84 }));
        }

        [Test]
        public void AnotherWriterCannotReplaceTheSlotOrItsPendingData()
        {
            store.Save(Checkpoint(42));
            using (var ownership = new FileStream(store.FilePath + ".lock", FileMode.Open, FileAccess.Write, FileShare.None))
                Assert.Throws<IOException>(() => new RunCheckpointStore(store.FilePath).Save(Checkpoint(84)));
            Assert.That(store.Load(out var loaded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(loaded.Seed, Is.EqualTo(42));
            Assert.That(File.Exists(store.PendingPath), Is.False);
        }

        [Test]
        public void UnavailableSlotIsNotTreatedAsMissingOrOverwritten()
        {
            Directory.CreateDirectory(store.FilePath);
            Assert.That(store.Load(out _, out var error), Is.EqualTo(CheckpointLoadStatus.Unavailable));
            Assert.That(error, Is.Not.Empty);
            Assert.Throws<IOException>(() => store.Save(Checkpoint()));
            Assert.That(Directory.Exists(store.FilePath), Is.True);
        }
    }
}
