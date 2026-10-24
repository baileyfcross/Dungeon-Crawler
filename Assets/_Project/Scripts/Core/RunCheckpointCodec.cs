using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SpaceCrawler.Core
{
    /// <summary>Bounded, checksummed binary format, independent of Unity serialization and live objects.</summary>
    public static class RunCheckpointCodec
    {
        private const int Magic = 0x53435253;
        public const int MaximumFileBytes = 1048576;

        public static byte[] Encode(RunCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            byte[] payload;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(checkpoint.Seed); writer.Write(checkpoint.GenerationVersion); writer.Write(checkpoint.Fingerprint);
                    writer.Write((int)checkpoint.Mode); writer.Write((int)checkpoint.Status); writer.Write(checkpoint.CurrentRoom.Value);
                    writer.Write(checkpoint.RoomVisits.Count);
                    foreach (var room in checkpoint.RoomVisits) writer.Write(room.Value);
                    writer.Write(checkpoint.EquipmentIds.Count);
                    foreach (var equipment in checkpoint.EquipmentIds) writer.Write(equipment);
                    writer.Write(checkpoint.RunSeed); writer.Write(checkpoint.SequenceVersion); writer.Write(checkpoint.LevelNumber);
                    writer.Write(checkpoint.LevelSeeds.Count);
                    for (var i = 0; i < checkpoint.LevelSeeds.Count; i++) { writer.Write(checkpoint.LevelSeeds[i]); writer.Write(checkpoint.LevelFingerprints[i]); }
                    writer.Write(checkpoint.CompletedRoutes.Count);
                    foreach (var route in checkpoint.CompletedRoutes)
                    { writer.Write(route.Count); foreach (var room in route) writer.Write(room.Value); }
                    writer.Write(checkpoint.Life.RunId.ToByteArray()); writer.Write(checkpoint.Life.RunXp);
                    foreach (var player in checkpoint.Life.Players)
                    { writer.Write(player.Current); writer.Write(player.Maximum); writer.Write((int)player.State); }
                    for (var i = 0; i < checkpoint.Life.Players.Count; i++)
                    { writer.Write(checkpoint.Life.Profiles[i].ToByteArray()); writer.Write(checkpoint.Life.Classes[i]); }
                    writer.Write(checkpoint.World != null);
                    if (checkpoint.World != null)
                    {
                        foreach (var pose in checkpoint.World.Players)
                        { WriteVector(writer,pose.Position); WriteVector(writer,pose.Aim); writer.Write(pose.Cooldown); }
                        writer.Write(checkpoint.World.Rooms.Count);
                        foreach (var room in checkpoint.World.Rooms)
                        {
                            writer.Write(room.Level); writer.Write(room.Room.Value); writer.Write(room.Enemies.Count);
                            foreach (var enemy in room.Enemies)
                            { writer.Write(enemy.Spawn); writer.Write(enemy.Health); writer.Write(enemy.Maximum); WriteVector(writer,enemy.Position); WriteVector(writer,enemy.Facing); }
                        }
                    }
                    writer.Write((int)checkpoint.Difficulty);
                    writer.Write(checkpoint.PartnerRoomVisits.Count);
                    foreach (var room in checkpoint.PartnerRoomVisits) writer.Write(room.Value);
                }
                payload = stream.ToArray();
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var hash = SHA256.Create())
            {
                writer.Write(Magic); writer.Write(RunCheckpoint.SchemaVersion); writer.Write(payload.Length);
                writer.Write(payload); writer.Write(hash.ComputeHash(payload));
                if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Saved run exceeds the supported size.");
                return stream.ToArray();
            }
        }

        public static RunCheckpoint Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 44 || bytes.Length > MaximumFileBytes)
                throw new InvalidDataException("Saved run has an invalid size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Saved run format is not recognized.");
                var schema = reader.ReadInt32();
                if (schema < 2 || schema > RunCheckpoint.SchemaVersion) throw new NotSupportedException("Saved run version is not supported.");
                var length = reader.ReadInt32();
                if (length < 0 || length != bytes.Length - 44) throw new InvalidDataException("Saved run is incomplete.");
                var payload = reader.ReadBytes(length);
                var recordedHash = reader.ReadBytes(32);
                using (var hash = SHA256.Create())
                {
                    var actualHash = hash.ComputeHash(payload);
                    var difference = 0;
                    for (var i = 0; i < actualHash.Length; i++) difference |= actualHash[i] ^ recordedHash[i];
                    if (difference != 0) throw new InvalidDataException("Saved run integrity check failed.");
                }
                return ReadPayload(payload, schema);
            }
        }

        private static void WriteVector(BinaryWriter writer, System.Numerics.Vector2 vector) { writer.Write(vector.X); writer.Write(vector.Y); }
        private static System.Numerics.Vector2 ReadVector(BinaryReader reader) => new System.Numerics.Vector2(reader.ReadSingle(),reader.ReadSingle());

        private static RunCheckpoint ReadPayload(byte[] bytes, int schema)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var seed = reader.ReadUInt64(); var generation = reader.ReadInt32(); var fingerprint = reader.ReadString();
                var mode = (SavedSessionMode)reader.ReadInt32(); var status = (SavedRunStatus)reader.ReadInt32();
                if (status != SavedRunStatus.Active) throw new NotSupportedException("Saved run status is not supported.");
                var current = new RunRoomId(reader.ReadInt32());
                var count = reader.ReadInt32();
                if (count < 1 || count > 4096) throw new InvalidDataException("Saved room history is invalid.");
                var visits = new RunRoomId[count];
                for (var i = 0; i < visits.Length; i++) visits[i] = new RunRoomId(reader.ReadInt32());
                var players = reader.ReadInt32();
                if (players < 1 || players > 2) throw new InvalidDataException("Saved player slots are invalid.");
                var equipment = new string[players];
                for (var i = 0; i < equipment.Length; i++) equipment[i] = reader.ReadString();
                var runSeed = reader.ReadUInt64(); var sequenceVersion = reader.ReadInt32(); var level = reader.ReadInt32();
                var levels = reader.ReadInt32();
                if (levels < 1 || levels > 3) throw new InvalidDataException("Saved level count is invalid.");
                var seeds = new ulong[levels]; var fingerprints = new string[levels];
                for (var i = 0; i < levels; i++) { seeds[i] = reader.ReadUInt64(); fingerprints[i] = reader.ReadString(); }
                var completed = reader.ReadInt32();
                if (completed < 0 || completed > 2) throw new InvalidDataException("Saved completed levels are invalid.");
                var routes = new System.Collections.Generic.IReadOnlyList<RunRoomId>[completed];
                for (var i = 0; i < completed; i++)
                {
                    var rooms = reader.ReadInt32();
                    if (rooms < 1 || rooms > 4096) throw new InvalidDataException("Saved completed history is invalid.");
                    var route = new RunRoomId[rooms]; for (var j = 0; j < rooms; j++) route[j] = new RunRoomId(reader.ReadInt32());
                    routes[i] = route;
                }
                RunLifeSnapshot life;
                if (schema == 2)
                {
                    using (var hash = SHA256.Create())
                    {
                        var identity = new byte[16]; Array.Copy(hash.ComputeHash(bytes), identity, 16);
                        var defaults = new PlayerVitals[players]; for (var i = 0; i < players; i++) defaults[i] = PlayerVitals.Starting;
                        life = new RunLifeSnapshot(new Guid(identity), 0, defaults);
                    }
                }
                else
                {
                    var id = new Guid(reader.ReadBytes(16)); var xp = reader.ReadInt64();
                    var vitals = new PlayerVitals[players]; var profiles = new Guid[players]; var classes = new string[players];
                    for (var i = 0; i < players; i++) vitals[i] = new PlayerVitals(reader.ReadInt32(), reader.ReadInt32(), (PlayerLifeState)reader.ReadInt32());
                    for (var i = 0; i < players; i++) { profiles[i] = new Guid(reader.ReadBytes(16)); classes[i] = reader.ReadString(); }
                    life = new RunLifeSnapshot(id, xp, vitals, profiles, classes);
                }
                RunWorldSnapshot world = null;
                if (schema >= 4 && reader.ReadBoolean())
                {
                    var poses = new SavedPlayerPose[players];
                    for (int i = 0; i < players; i++) poses[i] = new SavedPlayerPose(ReadVector(reader),ReadVector(reader),reader.ReadSingle());
                    int roomCount = reader.ReadInt32();
                    if (roomCount < 0 || roomCount > 75) throw new InvalidDataException("Invalid saved room count.");
                    var rooms = new SavedRoomState[roomCount];
                    for (int i = 0; i < roomCount; i++)
                    {
                        int roomLevel = reader.ReadInt32(); var roomId = new RunRoomId(reader.ReadInt32()); int enemyCount = reader.ReadInt32();
                        if (enemyCount < 0 || enemyCount > 32) throw new InvalidDataException("Invalid saved enemy count.");
                        var enemies = new SavedEnemy[enemyCount];
                        for (int j = 0; j < enemyCount; j++) enemies[j] = new SavedEnemy(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),ReadVector(reader),ReadVector(reader));
                        rooms[i] = new SavedRoomState(roomLevel,roomId,enemies);
                    }
                    world = new RunWorldSnapshot(poses,rooms);
                }
                var difficulty = RunDifficulty.Legacy;
                RunRoomId[] partnerVisits = null;
                if (schema >= 5)
                {
                    difficulty = (RunDifficulty)reader.ReadInt32();
                    int partnerCount = reader.ReadInt32();
                    if (partnerCount < 1 || partnerCount > 4096) throw new InvalidDataException("Invalid partner route length.");
                    partnerVisits = new RunRoomId[partnerCount];
                    for (int i = 0; i < partnerCount; i++) partnerVisits[i] = new RunRoomId(reader.ReadInt32());
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Saved run contains unexpected data.");
                return new RunCheckpoint(seed, generation, fingerprint, mode, current, visits, equipment,
                    runSeed, sequenceVersion, level, seeds, fingerprints, routes, life, schema, world, difficulty, partnerVisits);
            }
        }
    }
}
