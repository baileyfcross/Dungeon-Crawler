using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class ProfileProgressTests
    {
        private string directory;
        private ProfileProgressStore store;
        [SetUp] public void SetUp()
        { directory = Path.GetFullPath(Path.Combine("Logs/Validation/Health/Core", Guid.NewGuid().ToString("N"))); store = new ProfileProgressStore(directory); }
        private RunEndReceipt Receipt(long xp = 100) => new RunEndReceipt(Guid.NewGuid(), xp, RunEndReason.Loss, new[] { store.Profile.Id });
        private RunCheckpoint Checkpoint(Guid run) => new RunCheckpoint(42, 1, new string('a', 64), SavedSessionMode.Solo, new RunRoomId(0),
            new[] { new RunRoomId(0) }, new[] { "weapon.knife" }, life: new RunLifeSnapshot(run, 100, new[] { PlayerVitals.Starting }, new[] { store.Profile.Id }));

        [Test]
        public void RewardsApplyOnceAndFractionalExperienceCarriesBetweenRuns()
        {
            var receipt = Receipt(10); store.EndRun(receipt); store.EndRun(receipt);
            Assert.That(store.Profile.Experience, Is.Zero); Assert.That(store.Profile.FractionHundredths, Is.EqualTo(60));
            store.EndRun(Receipt(10));
            var reopened = new ProfileProgressStore(directory);
            Assert.That(reopened.Profile.Id, Is.EqualTo(store.Profile.Id)); Assert.That(reopened.Profile.Experience, Is.EqualTo(1));
            Assert.That(reopened.Profile.FractionHundredths, Is.EqualTo(20));
            reopened.EndRun(receipt); Assert.That(reopened.Profile.Experience, Is.EqualTo(1));
        }
        [Test]
        public void BothProfilesReceiveFullSixPercentOfSharedRunXpAndDeliveryIsAcknowledged()
        {
            var peer = new ProfileProgressStore(Path.Combine(directory, "Peer"));
            var receipt = new RunEndReceipt(Guid.NewGuid(), 100, RunEndReason.Loss, new[] { store.Profile.Id, peer.Profile.Id });
            store.EndRun(receipt); Assert.That(store.PendingFor(peer.Profile.Id).Count, Is.EqualTo(1));
            peer.EndRun(receipt); peer.EndRun(receipt);
            Assert.That(peer.Profile.Experience, Is.EqualTo(6)); Assert.That(store.Profile.Experience, Is.EqualTo(6));
            store.Acknowledge(receipt.RunId, peer.Profile.Id);
            Assert.That(new ProfileProgressStore(directory).PendingFor(peer.Profile.Id), Is.Empty);
        }
        [Test]
        public void JournalBeforeProfileWritePreventsContinueAndRecoveryCreditsExactlyOnce()
        {
            var receipt = Receipt(); var checkpoints = new RunCheckpointStore(Path.Combine(directory, "run.save")); checkpoints.Save(Checkpoint(receipt.RunId));
            using (new FileStream(store.FilePath + ".pending", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => store.EndRun(receipt));
            Assert.That(checkpoints.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Missing));
            var recovered = new ProfileProgressStore(directory); Assert.That(recovered.Profile.Experience, Is.EqualTo(6));
            recovered.EndRun(receipt); Assert.That(recovered.Profile.Experience, Is.EqualTo(6));
            Assert.Throws<InvalidOperationException>(() => checkpoints.Save(Checkpoint(receipt.RunId)));
        }
        [Test]
        public void FailureBeforeJournalCommitDoesNotCreditOrInvalidateRun()
        {
            var receipt = Receipt(); var checkpoints = new RunCheckpointStore(Path.Combine(directory, "run.save")); checkpoints.Save(Checkpoint(receipt.RunId));
            var receiptPath = ProfileProgressStore.ReceiptPath(directory, receipt.RunId); Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
            using (new FileStream(receiptPath + ".pending", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => store.EndRun(receipt));
            Assert.That(checkpoints.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(new ProfileProgressStore(directory).Profile.Experience, Is.Zero);
        }
        [Test]
        public void MissingPrimaryWithExistingBackupDoesNotCreateANewProfile()
        {
            store.EndRun(Receipt());
            File.Move(store.FilePath, store.FilePath + ".held");
            try
            {
                Assert.Throws<InvalidDataException>(() => new ProfileProgressStore(directory));
                Assert.That(File.Exists(store.FilePath), Is.False); Assert.That(File.Exists(store.FilePath + ".backup"), Is.True);
            }
            finally { File.Move(store.FilePath + ".held", store.FilePath); }
        }
        [Test]
        public void ConflictingReceiptAndCorruptProfileArePreservedAndNeverOverwriteProgress()
        {
            var receipt = Receipt(); store.EndRun(receipt);
            Assert.Throws<InvalidDataException>(() => store.EndRun(new RunEndReceipt(receipt.RunId, 1000, receipt.Reason, receipt.Profiles)));
            var bytes = File.ReadAllBytes(store.FilePath); bytes[18] ^= 1; File.WriteAllBytes(store.FilePath, bytes);
            Assert.Throws<InvalidDataException>(() => new ProfileProgressStore(directory));
            Assert.That(File.ReadAllBytes(store.FilePath), Is.EqualTo(bytes));
        }
        [Test]
        public void SaveRoundTripRetainsHealthXpAndDownedStateWithoutAwardingProfileXp()
        {
            var life = new RunLifeSnapshot(Guid.NewGuid(), 250, new[] { new PlayerVitals(70, 150, PlayerLifeState.Alive), new PlayerVitals(0, 100, PlayerLifeState.Downed) },
                new[] { store.Profile.Id, Guid.NewGuid() }, new[] { "", "future.class" });
            var original = new RunCheckpoint(42, 1, new string('a', 64), SavedSessionMode.Cooperative, new RunRoomId(0), new[] { new RunRoomId(0) }, new[] { "weapon.knife", "weapon.knife" }, life: life);
            var loaded = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(original));
            Assert.That(loaded.Life.RunId, Is.EqualTo(life.RunId)); Assert.That(loaded.Life.RunXp, Is.EqualTo(250));
            Assert.That(loaded.Life.Players[0].Current, Is.EqualTo(70)); Assert.That(loaded.Life.Players[1].State, Is.EqualTo(PlayerLifeState.Downed));
            Assert.That(loaded.Life.Classes[1], Is.EqualTo("future.class")); Assert.That(store.Profile.Experience, Is.Zero);
        }
        [Test]
        public void LegacyCheckpointMigrationUsesStableIdentityAndPreservesOriginalBytes()
        {
            byte[] payload;
            using (var data = new MemoryStream())
            {
                using (var w = new BinaryWriter(data, System.Text.Encoding.UTF8, true))
                {
                    w.Write((ulong)42); w.Write(1); w.Write(new string('a', 64)); w.Write(1); w.Write(1); w.Write(0);
                    w.Write(1); w.Write(0); w.Write(1); w.Write("weapon.knife");
                    w.Write((ulong)42); w.Write(0); w.Write(1); w.Write(1); w.Write((ulong)42); w.Write(new string('a', 64)); w.Write(0);
                }
                payload = data.ToArray();
            }
            byte[] bytes;
            using (var data = new MemoryStream())
            {
                using (var w = new BinaryWriter(data, System.Text.Encoding.UTF8, true))
                using (var hash = SHA256.Create()) { w.Write(0x53435253); w.Write(2); w.Write(payload.Length); w.Write(payload); w.Write(hash.ComputeHash(payload)); }
                bytes = data.ToArray();
            }
            var path = Path.Combine(directory, "run.save"); File.WriteAllBytes(path, bytes); var checkpoints = new RunCheckpointStore(path);
            Assert.That(checkpoints.Load(out var loaded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(loaded.SourceSchemaVersion, Is.EqualTo(2)); Assert.That(loaded.Life.Players[0].Current, Is.EqualTo(100));
            Assert.That(loaded.Life.RunXp, Is.Zero); Assert.That(RunCheckpointCodec.Decode(bytes).Life.RunId, Is.EqualTo(loaded.Life.RunId));
            checkpoints.Save(loaded);
            Assert.That(File.ReadAllBytes(path + ".schema2"), Is.EqualTo(bytes));
            Assert.That(checkpoints.Load(out var upgraded, out _), Is.EqualTo(CheckpointLoadStatus.Valid));
            Assert.That(upgraded.SourceSchemaVersion, Is.EqualTo(RunCheckpoint.SchemaVersion));
        }
    }
}
