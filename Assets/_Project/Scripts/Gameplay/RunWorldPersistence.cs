using System;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Host/solo adapter for the explicit save contracts. Retired rooms never tick.</summary>
    public sealed class RunWorldPersistence
    {
        private readonly Dictionary<(int,int),SavedRoomState> rooms = new Dictionary<(int,int),SavedRoomState>();
        private IReadOnlyList<SavedPlayerPose> restoredPlayers;
        private byte[] lastWritten;
        private float nextAutosave;
        public string RecoveryMessage { get; private set; }
        public RunWorldPersistence(RunWorldSnapshot world)
        { if (world == null) return; restoredPlayers = world.Players; foreach (var room in world.Rooms) rooms[(room.Level,room.Room.Value)] = room; }
        public void RestoreRoom(int level, RunRoomId id, AuthoredRoom room, Action changed)
        {
            if (rooms.TryGetValue((level,id.Value),out var saved))
            {
                var actors = room.Encounter.Enemies;
                if (saved.Enemies.Count != actors.Count) throw new InvalidOperationException("Saved enemy roster no longer matches the authored room.");
                foreach (var state in saved.Enemies)
                {
                    var actor = actors.SingleOrDefault(a => a.Identity.Spawn == state.Spawn);
                    if (actor == null || actor.Target.InitialHealth != state.Maximum) throw new InvalidOperationException("Saved enemy slot no longer matches authoring.");
                    var position = new Vector2(state.Position.X,state.Position.Y)+(Vector2)room.transform.position;
                    var value = new SavedEnemy(state.Spawn,state.Health,state.Maximum,ToCore(position),state.Facing);
                    if (!room.IsSafePosition(position))
                    { value = new SavedEnemy(state.Spawn,state.Health,state.Maximum,ToCore(actor.transform.position),state.Facing); Recovered(); }
                    actor.Restore(value);
                }
            }
            foreach (var actor in room.Encounter.Enemies)
            {
                var observed = actor.Target.Health.Current;
                actor.Simulation.Changed += () =>
                {
                    if (actor.Simulation.Lifecycle == EnemyLifecycle.Despawned || actor.Target.Health.Current == observed) return;
                    observed = actor.Target.Health.Current; changed();
                };
            }
        }
        private void Recovered()
        { RecoveryMessage = "A saved position was obstructed. A safe position in the same room was used."; Debug.LogWarning("[RunRestore] " + RecoveryMessage); }
        public void PlacePlayers(AuthoredRoom room, int arrivalPort, PlayerMotor[] motors, PlayerSimulation[] simulations, CombatResolver[] combat)
        {
            for (int i = 0; i < motors.Length; i++)
                PlacePlayer(room,arrivalPort,i,motors.Length,motors[i],simulations[i],combat[i]);
            if (restoredPlayers != null) room.SuppressOccupiedExits(motors);
            FinishPlacingPlayers();
        }
        internal void PlacePlayer(AuthoredRoom room,int arrivalPort,int slot,int count,PlayerMotor motor,PlayerSimulation simulation,CombatResolver combat)
        {
                var position = room.Arrival(arrivalPort,slot,count);
                if (restoredPlayers != null)
                {
                    var pose = restoredPlayers[slot]; var saved = new Vector2(pose.Position.X,pose.Position.Y)+(Vector2)room.transform.position;
                    if (room.IsSafePosition(saved)) position = saved; else Recovered();
                    simulation.RestoreAim(pose.Aim); combat.Simulation.RestoreCooldown(pose.Cooldown);
                }
                motor.Stop(); motor.PlaceAt(position);
        }
        internal void FinishPlacingPlayers() => restoredPlayers=null;
        internal void CaptureRoom(int level,RunRoomId id,AuthoredRoom room)
        {
            var offset=(Vector2)room.transform.position;
            var savedEnemies = room.Encounter.Enemies.Select(a => new SavedEnemy(a.Identity.Spawn,a.Target.Health.Current,a.Target.Health.Maximum,
                ToCore((Vector2)a.transform.position-offset),new CoreVector(a.Facing.x,a.Facing.y))).ToArray();
            rooms[(level,id.Value)] = new SavedRoomState(level,id,savedEnemies);
        }
        internal RunWorldSnapshot CapturePlayers(PlayerMotor[] motors,PlayerSimulation[] simulations,CombatResolver[] combat,AuthoredRoom[] playerRooms)
        {
            var poses = new SavedPlayerPose[motors.Length];
            for (int i = 0; i < poses.Length; i++) poses[i] = new SavedPlayerPose(ToCore(motors[i].Body.position-(Vector2)playerRooms[i].transform.position),simulations[i].State.AimDirection,combat[i].Simulation.CooldownRemaining);
            return new RunWorldSnapshot(poses,rooms.Values.OrderBy(r => r.Level).ThenBy(r => r.Room.Value).ToArray());
        }
        public RunWorldSnapshot Capture(int level, RunRoomId id, AuthoredRoom room, PlayerMotor[] motors, PlayerSimulation[] simulations, CombatResolver[] combat)
        {
            if (room?.Encounter == null) throw new InvalidOperationException("An entered room is required for world state.");
            CaptureRoom(level,id,room);
            return CapturePlayers(motors,simulations,combat,Enumerable.Repeat(room,motors.Length).ToArray());
        }
        public void Commit(RunCheckpoint checkpoint, Action<RunCheckpoint> save)
        {
            var bytes = RunCheckpointCodec.Encode(checkpoint);
            if (lastWritten != null && bytes.SequenceEqual(lastWritten)) return;
            save(checkpoint); lastWritten = bytes;
        }
        public bool AutosaveDue(float now)
        { if (now < nextAutosave) return false; nextAutosave = now+5; return true; }
        private static CoreVector ToCore(Vector2 value) => new CoreVector(value.x,value.y);
    }
}
