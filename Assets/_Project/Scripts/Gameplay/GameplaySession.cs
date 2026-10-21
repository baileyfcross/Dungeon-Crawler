using System;
using System.Security.Cryptography;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Wires one local player explicitly and orders presentation after physics.</summary>
    public sealed class GameplaySession : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float movementSpeed = 5;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInputAdapter input;
        [SerializeField] private PlayerPresentation presentation;
        [SerializeField] private PlayerCameraFollower follower;
        [SerializeField] private WeaponDefinition startingWeapon;
        [SerializeField] private CombatResolver combat;
        [SerializeField] private RunGenerationDefinition generation;
        [SerializeField] private RoomLoader rooms;
        [UnityEngine.Serialization.FormerlySerializedAs("useDevelopmentSeed")]
        [SerializeField] private bool useFixedSeed;
        [UnityEngine.Serialization.FormerlySerializedAs("developmentSeed")]
        [SerializeField] private uint fixedSeed = 42;
        public PlayerSimulation Simulation { get; private set; }
        public DungeonGraph Run { get; private set; }
        public RunSequence Sequence { get; private set; }
        public int LevelNumber => Sequence?.LevelNumber ?? 1;
        public PlayerMotor Motor => motor;
        public PlayerInputAdapter Input => input;
        public PlayerCameraFollower Follower => follower;
        public CombatResolver Combat => combat;
        public RoomLoader Rooms => rooms;
        public string WeaponDisplayName => startingWeapon != null ? startingWeapon.DisplayName : "Unarmed";
        private Action<RunCheckpoint> commitCheckpoint;
        public bool PollLocalDevices { get; set; } = true;

        public void Bind(PlayerIdentity identity, ApplicationFlow flow, ulong? runSeed = null, bool useLevelSequence = false,
            RunCheckpoint checkpoint = null, Action<RunCheckpoint> save = null, WeaponDefinition restoredWeapon = null)
        {
            if (Simulation != null) throw new InvalidOperationException("Session is already bound.");
            if (generation == null) throw new InvalidOperationException("Missing run generation definition.");
            // The local session selects a seed once; a future authority can explicitly supply it.
            // Neither seed selection nor Unity state is consulted inside Core generation.
            var seed = runSeed ?? (Debug.isDebugBuild && useFixedSeed ? fixedSeed : CreateRunSeed());
            commitCheckpoint = save;
            Sequence = checkpoint != null ? RunSequence.Restore(checkpoint, generation.CreateConfiguration())
                : useLevelSequence ? new RunSequence(seed, generation.CreateConfiguration()) : null;
            if (checkpoint != null)
            {
                if (checkpoint.Mode != SavedSessionMode.Solo || restoredWeapon == null || restoredWeapon.CreateConfiguration().Id != checkpoint.EquipmentIds[0])
                    throw new InvalidOperationException("Saved solo equipment or session mode is incompatible.");
                startingWeapon = restoredWeapon;
            }
            Run = Sequence?.CurrentGraph ?? new RunGenerator().Generate(seed, generation.CreateConfiguration());
            Simulation = new PlayerSimulation(identity, movementSpeed);
            motor.Bind(Simulation);
            follower.Bind(motor.transform);
            combat.Bind(Simulation, startingWeapon != null ? startingWeapon.CreateConfiguration() : null);
            input.Bind(Simulation, flow, combat);
            if (Debug.isDebugBuild)
                Debug.Log($"[RunGeneration] seed={Run.Seed} version={Run.GenerationVersion} nodes={Run.Nodes.Count} connections={Run.Connections.Count} start={Run.StartRoomId} terminal={Run.TerminalRoomId}", this);
            // The original isolated control/combat test prefab intentionally has no loader.
            // Normal New Game requires the room-enabled composition in ApplicationRoot.
            if (rooms != null) rooms.Bind(this, flow, generation, checkpoint);
        }

        private static ulong CreateRunSeed()
        {
            var bytes = new byte[8];
            using (var source = RandomNumberGenerator.Create()) source.GetBytes(bytes);
            ulong seed = 0;
            for (var i = 0; i < bytes.Length; i++) seed |= (ulong)bytes[i] << (8 * i);
            return seed;
        }

        public bool TryAdvanceLevel(RunProgression progression)
        {
            if (Sequence == null || !Sequence.TryAdvance(progression)) return false;
            Run = Sequence.CurrentGraph;
            return true;
        }

        public void CommitCheckpoint(RunProgression progression)
        {
            if (commitCheckpoint != null && Sequence != null)
                commitCheckpoint(RunCheckpoint.Capture(Sequence, progression, SavedSessionMode.Solo, new[] { combat.Simulation.EquippedWeapon.Id }));
        }

        private void Update() { if (PollLocalDevices) input.SampleMovement(); }
        private void LateUpdate()
        {
            if (Simulation == null) return;
            motor.CaptureState();
            follower.Follow();
            // Recalculate even when the pointer is stationary, using the camera's current presentation.
            if (PollLocalDevices)
            {
                input.SampleAim(follower.GameplayCamera, motor.transform.position);
                input.SampleAttack();
            }
            presentation.Present(Simulation.State);
        }
    }
}
