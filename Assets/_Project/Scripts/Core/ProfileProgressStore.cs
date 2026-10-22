using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpaceCrawler.Core
{
    public sealed class RunEndReceipt
    {
        public const int ProfilePercent = 6;
        public Guid RunId { get; }
        public long RunXp { get; }
        public RunEndReason Reason { get; }
        public int Percent { get; }
        public IReadOnlyList<Guid> Profiles { get; }
        public RunEndReceipt(Guid runId, long xp, RunEndReason reason, IReadOnlyList<Guid> profiles, int percent = ProfilePercent)
        {
            if (runId == Guid.Empty || xp < 0 || xp > long.MaxValue / 100 || (reason != RunEndReason.Loss && reason != RunEndReason.Completed)
                || profiles == null || profiles.Count < 1 || profiles.Count > 2 || profiles.Any(p => p == Guid.Empty)
                || profiles.Distinct().Count() != profiles.Count || percent < 0 || percent > 100) throw new ArgumentException("Invalid run receipt.");
            RunId = runId; RunXp = xp; Reason = reason; Profiles = Array.AsReadOnly(profiles.ToArray()); Percent = percent;
        }
    }

    /// <summary>Atomic checksummed records. Readers never fall back to stale backups silently.</summary>
    internal static class ProgressRecord
    {
        public static void Write(string path, Action<BinaryWriter> encode)
        {
            byte[] payload;
            using (var data = new MemoryStream())
            { using (var writer = new BinaryWriter(data, Encoding.UTF8, true)) encode(writer); payload = data.ToArray(); }
            if (payload.Length > 8388608) throw new InvalidDataException("Progress record is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            using (var sha = SHA256.Create())
            { writer.Write(0x53435052); writer.Write(1); writer.Write(payload.Length); writer.Write(payload); writer.Write(sha.ComputeHash(payload)); writer.Flush(); file.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".pending", path, path + ".backup", true);
            else File.Move(path + ".pending", path);
        }
        public static T Read<T>(string path, Func<BinaryReader, T> decode)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = new BinaryReader(file))
            using (var sha = SHA256.Create())
            {
                if (file.Length < 44 || file.Length > 8388652 || reader.ReadInt32() != 0x53435052 || reader.ReadInt32() != 1)
                    throw new InvalidDataException("Progress format is invalid or unsupported.");
                var size = reader.ReadInt32(); if (size != file.Length - 44) throw new InvalidDataException("Progress is incomplete.");
                var bytes = reader.ReadBytes(size); var hash = reader.ReadBytes(32);
                if (!sha.ComputeHash(bytes).SequenceEqual(hash)) throw new InvalidDataException("Progress integrity check failed.");
                using (var data = new MemoryStream(bytes, false))
                using (var payload = new BinaryReader(data))
                {
                    var result = decode(payload);
                    if (data.Position != data.Length) throw new InvalidDataException("Unexpected progress data.");
                    return result;
                }
            }
        }
    }

    public sealed class ProfileProgress
    {
        public Guid Id { get; internal set; }
        public long Experience { get; internal set; }
        public int FractionHundredths { get; internal set; }
        internal HashSet<Guid> CreditedRuns { get; } = new HashSet<Guid>();
        public bool HasEndedRun(Guid run) => CreditedRuns.Contains(run);
        // Reserved per-class progression records; no class or ability is supplied by this package.
        public IReadOnlyDictionary<string, long> Classes => classes;
        internal readonly Dictionary<string, long> classes = new Dictionary<string, long>(StringComparer.Ordinal);
    }

    /// <summary>Durable run-end journal precedes profile credit. Receipt IDs make crash recovery/re-delivery idempotent.</summary>
    public sealed class ProfileProgressStore
    {
        private readonly string directory;
        public ProfileProgress Profile { get; private set; }
        public string FilePath => Path.Combine(directory, "profile.save");
        public ProfileProgressStore(string directory)
        {
            this.directory = Path.GetFullPath(directory);
            using (Own())
            {
                if (!File.Exists(FilePath) && File.Exists(FilePath + ".backup"))
                    throw new InvalidDataException("Profile primary is missing. Its backup is retained for recovery.");
                Profile = File.Exists(FilePath) ? ReadProfile() : new ProfileProgress { Id = Guid.NewGuid() };
                if (!File.Exists(FilePath)) WriteProfile();
                Recover();
            }
        }
        private FileStream Own()
        {
            Directory.CreateDirectory(directory);
            return new FileStream(Path.Combine(directory, "progress.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        public static string ReceiptPath(string directory, Guid run) => Path.Combine(directory, "RunEnds", run.ToString("N") + ".end");
        private string ReceiptPath(Guid run) => ReceiptPath(directory, run);
        public bool HasEnded(Guid run) => File.Exists(ReceiptPath(run));

        public void EndRun(RunEndReceipt receipt) => EndRunWithCredit(receipt);
        public long EndRunWithCredit(RunEndReceipt receipt)
        {
            using (Own())
            {
                Profile = ReadProfile();
                if (!receipt.Profiles.Contains(Profile.Id)) throw new InvalidOperationException("Receipt does not belong to this profile.");
                if (File.Exists(ReceiptPath(receipt.RunId)))
                {
                    var previous = ReadReceipt(ReceiptPath(receipt.RunId));
                    if (previous.RunXp != receipt.RunXp || previous.Reason != receipt.Reason || previous.Percent != receipt.Percent || !previous.Profiles.SequenceEqual(receipt.Profiles))
                        throw new InvalidDataException("Conflicting run receipt.");
                }
                else WriteReceipt(receipt);
                var before = Profile.Experience; Credit(receipt); return Profile.Experience-before;
            }
        }
        private void Recover()
        {
            var folder = Path.Combine(directory, "RunEnds"); if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.end"))
            {
                var receipt = ReadReceipt(file);
                if (Path.GetFileNameWithoutExtension(file) != receipt.RunId.ToString("N")) throw new InvalidDataException("Receipt identity mismatch.");
                if (receipt.Profiles.Contains(Profile.Id)) Credit(receipt);
            }
        }
        private void Credit(RunEndReceipt receipt)
        {
            if (Profile.CreditedRuns.Contains(receipt.RunId)) return;
            var whole = receipt.RunXp / 100 * receipt.Percent;
            var fractional = receipt.RunXp % 100 * receipt.Percent + Profile.FractionHundredths;
            Profile.Experience = checked(Profile.Experience + whole + fractional / 100);
            Profile.FractionHundredths = (int)(fractional % 100); Profile.CreditedRuns.Add(receipt.RunId);
            WriteProfile();
        }
        public IReadOnlyList<RunEndReceipt> PendingFor(Guid peer)
        {
            using (Own())
            {
                var result = new List<RunEndReceipt>(); var folder = Path.Combine(directory, "RunEnds");
                if (!Directory.Exists(folder)) return result.AsReadOnly();
                foreach (var file in Directory.GetFiles(folder, "*.end"))
                {
                    var receipt = ReadReceipt(file);
                    if (receipt.Profiles.Contains(peer) && peer != Profile.Id && !File.Exists(file + ".ack." + peer.ToString("N"))) result.Add(receipt);
                }
                return result.AsReadOnly();
            }
        }
        public void Acknowledge(Guid run, Guid peer)
        {
            using (Own())
            {
                var receipt = ReadReceipt(ReceiptPath(run));
                if (!receipt.Profiles.Contains(peer)) throw new InvalidOperationException("Unknown receipt recipient.");
                ProgressRecord.Write(ReceiptPath(run) + ".ack." + peer.ToString("N"), w => w.Write(run.ToByteArray()));
            }
        }
        private ProfileProgress ReadProfile() => ProgressRecord.Read(FilePath, r =>
        {
            var profile = new ProfileProgress { Id = new Guid(r.ReadBytes(16)), Experience = r.ReadInt64(), FractionHundredths = r.ReadInt32() };
            if (profile.Id == Guid.Empty || profile.Experience < 0 || profile.FractionHundredths < 0 || profile.FractionHundredths > 99) throw new InvalidDataException("Invalid profile.");
            var count = r.ReadInt32(); if (count < 0 || count > 400000) throw new InvalidDataException("Invalid reward history.");
            for (var i = 0; i < count; i++) { var run = new Guid(r.ReadBytes(16)); if (run == Guid.Empty || !profile.CreditedRuns.Add(run)) throw new InvalidDataException("Invalid reward identity."); }
            var classes = r.ReadInt32(); if (classes < 0 || classes > 1024) throw new InvalidDataException("Invalid class records.");
            for (var i = 0; i < classes; i++)
            { var id = r.ReadString(); var xp = r.ReadInt64(); if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || xp < 0 || profile.classes.ContainsKey(id)) throw new InvalidDataException("Invalid class progression."); profile.classes.Add(id, xp); }
            return profile;
        });
        private void WriteProfile() => ProgressRecord.Write(FilePath, w =>
        {
            w.Write(Profile.Id.ToByteArray()); w.Write(Profile.Experience); w.Write(Profile.FractionHundredths);
            w.Write(Profile.CreditedRuns.Count); foreach (var run in Profile.CreditedRuns.OrderBy(r => r)) w.Write(run.ToByteArray());
            w.Write(Profile.classes.Count); foreach (var item in Profile.classes.OrderBy(c => c.Key, StringComparer.Ordinal)) { w.Write(item.Key); w.Write(item.Value); }
        });
        private void WriteReceipt(RunEndReceipt receipt) => ProgressRecord.Write(ReceiptPath(receipt.RunId), w =>
        {
            w.Write(receipt.RunId.ToByteArray()); w.Write(receipt.RunXp); w.Write((int)receipt.Reason);
            w.Write(receipt.Profiles.Count); foreach (var profile in receipt.Profiles) w.Write(profile.ToByteArray()); w.Write(receipt.Percent);
        });
        private static RunEndReceipt ReadReceipt(string path) => ProgressRecord.Read(path, r =>
        {
            var id = new Guid(r.ReadBytes(16)); var xp = r.ReadInt64(); var reason = (RunEndReason)r.ReadInt32(); var count = r.ReadInt32();
            if (count < 1 || count > 2) throw new InvalidDataException("Invalid receipt recipients.");
            var profiles = new Guid[count]; for (var i = 0; i < count; i++) profiles[i] = new Guid(r.ReadBytes(16));
            return new RunEndReceipt(id, xp, reason, profiles, r.ReadInt32());
        });
    }
}
