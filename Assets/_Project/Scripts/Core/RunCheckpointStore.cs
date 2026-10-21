using System;
using System.IO;

namespace SpaceCrawler.Core
{
    public enum CheckpointLoadStatus { Missing, Valid, Invalid, Incompatible, Unavailable }

    /// <summary>One durable run slot. Incomplete writes never replace the last committed checkpoint.</summary>
    public sealed class RunCheckpointStore
    {
        private readonly object sync = new object();
        public string FilePath { get; }
        public string BackupPath => FilePath + ".backup";
        public string PendingPath => FilePath + ".pending";

        public RunCheckpointStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save file path is required.", nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        public CheckpointLoadStatus Load(out RunCheckpoint checkpoint, out string error)
        {
            lock (sync)
            {
                var status = Read(FilePath, out checkpoint, out error);
                if (status == CheckpointLoadStatus.Valid && File.Exists(ProfileProgressStore.ReceiptPath(Path.GetDirectoryName(FilePath), checkpoint.Life.RunId)))
                { checkpoint = null; error = "This run has ended."; return CheckpointLoadStatus.Missing; }
                return status;
            }
        }

        private static CheckpointLoadStatus Read(string path, out RunCheckpoint checkpoint, out string error)
        {
            checkpoint = null; error = string.Empty;
            try
            {
                // Open directly: distinguish an unavailable path from a missing file.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (stream.Length > RunCheckpointCodec.MaximumFileBytes) throw new InvalidDataException("Saved run exceeds the supported size.");
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        checkpoint = RunCheckpointCodec.Decode(memory.ToArray());
                    }
                }
                return CheckpointLoadStatus.Valid;
            }
            catch (FileNotFoundException) { return CheckpointLoadStatus.Missing; }
            catch (DirectoryNotFoundException) { return CheckpointLoadStatus.Missing; }
            catch (NotSupportedException exception) { error = exception.Message; return CheckpointLoadStatus.Incompatible; }
            catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException
                || exception is ArgumentException || exception is FormatException)
            { error = "Saved run is damaged or invalid."; return CheckpointLoadStatus.Invalid; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { error = "Saved run could not be read. Check storage availability and permissions."; return CheckpointLoadStatus.Unavailable; }
        }

        public void Save(RunCheckpoint checkpoint)
        {
            var bytes = RunCheckpointCodec.Encode(checkpoint);
            lock (sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var ownership = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                {
                if (File.Exists(ProfileProgressStore.ReceiptPath(Path.GetDirectoryName(FilePath), checkpoint.Life.RunId)))
                    throw new InvalidOperationException("An ended run cannot be saved again.");
                var status = Read(FilePath, out var previous, out _);
                if (previous != null && previous.SourceSchemaVersion < RunCheckpoint.SchemaVersion)
                { var archive = FilePath + ".schema" + previous.SourceSchemaVersion; if (!File.Exists(archive)) File.Copy(FilePath,archive); }
                if (status == CheckpointLoadStatus.Unavailable) throw new IOException("Existing save cannot be accessed safely.");
                // Keep incompatible/corrupt evidence without replacing a previous valid backup.
                if (status == CheckpointLoadStatus.Invalid || status == CheckpointLoadStatus.Incompatible)
                    File.Copy(FilePath, FilePath + ".invalid." + Guid.NewGuid().ToString("N"));
                using (var stream = new FileStream(PendingPath, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (status == CheckpointLoadStatus.Missing) File.Move(PendingPath, FilePath);
                else File.Replace(PendingPath, FilePath, status == CheckpointLoadStatus.Valid ? BackupPath : null, true);
                }
            }
        }
    }
}
