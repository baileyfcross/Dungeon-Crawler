using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpaceCrawler.Core
{
    public readonly struct SavedPlayerPose
    {
        public Vector2 Position { get; }
        public Vector2 Aim { get; }
        public float Cooldown { get; }
        public SavedPlayerPose(Vector2 position, Vector2 aim, float cooldown)
        {
            if (!PlayerSimulation.IsFinite(position) || !PlayerSimulation.IsFinite(aim) || Math.Abs(aim.LengthSquared()-1) > .001f
                || float.IsNaN(cooldown) || float.IsInfinity(cooldown) || cooldown < 0 || cooldown > 3600)
                throw new ArgumentException("Invalid saved player pose.");
            Position = position; Aim = aim; Cooldown = cooldown;
        }
    }
    public readonly struct SavedEnemy
    {
        public int Spawn { get; }
        public int Health { get; }
        public int Maximum { get; }
        public Vector2 Position { get; }
        public Vector2 Facing { get; }
        public SavedEnemy(int spawn, int health, int maximum, Vector2 position, Vector2 facing)
        {
            if (spawn < 1 || spawn > 32 || maximum < 1 || health < 0 || health > maximum
                || !PlayerSimulation.IsFinite(position) || !PlayerSimulation.IsFinite(facing) || Math.Abs(facing.LengthSquared()-1) > .001f)
                throw new ArgumentException("Invalid saved enemy state.");
            Spawn = spawn; Health = health; Maximum = maximum; Position = position; Facing = facing;
        }
    }
    public sealed class SavedRoomState
    {
        public int Level { get; }
        public RunRoomId Room { get; }
        public IReadOnlyList<SavedEnemy> Enemies { get; }
        public SavedRoomState(int level, RunRoomId room, IReadOnlyList<SavedEnemy> enemies)
        {
            if (level < 1 || level > 3 || enemies == null || enemies.Count > 32) throw new ArgumentException("Invalid saved room.");
            var ids = new HashSet<int>();
            foreach (var enemy in enemies) if (enemy.Spawn == 0 || !ids.Add(enemy.Spawn)) throw new ArgumentException("Duplicate or invalid saved spawn.");
            Level = level; Room = room; Enemies = Array.AsReadOnly(new List<SavedEnemy>(enemies).ToArray());
        }
    }
    /// <summary>Explicit semantic state only. No scene objects, paths, input, projectiles or revival holds.</summary>
    public sealed class RunWorldSnapshot
    {
        public IReadOnlyList<SavedPlayerPose> Players { get; }
        public IReadOnlyList<SavedRoomState> Rooms { get; }
        public RunWorldSnapshot(IReadOnlyList<SavedPlayerPose> players, IReadOnlyList<SavedRoomState> rooms)
        {
            if (players == null || players.Count < 1 || players.Count > 2 || rooms == null || rooms.Count > 75)
                throw new ArgumentException("Invalid saved world bounds.");
            foreach (var player in players) if (player.Aim.LengthSquared() < .9f) throw new ArgumentException("Invalid saved player.");
            var keys = new HashSet<(int,int)>();
            foreach (var room in rooms) if (room == null || !keys.Add((room.Level,room.Room.Value))) throw new ArgumentException("Duplicate saved room.");
            Players = Array.AsReadOnly(new List<SavedPlayerPose>(players).ToArray());
            Rooms = Array.AsReadOnly(new List<SavedRoomState>(rooms).ToArray());
        }
    }
}
