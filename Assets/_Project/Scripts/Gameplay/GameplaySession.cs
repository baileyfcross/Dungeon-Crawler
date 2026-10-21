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
        public PlayerMotor Motor => motor;
        public PlayerInputAdapter Input => input;
        public PlayerCameraFollower Follower => follower;
        public CombatResolver Combat => combat;
        public RoomLoader Rooms => rooms;
        public string WeaponDisplayName => startingWeapon != null ? startingWeapon.DisplayName : "Unarmed";

        public void Bind(PlayerIdentity identity, ApplicationFlow flow, ulong? runSeed = null)
        {
            if (Simulation != null) throw new InvalidOperationException("Session is already bound.");
            if (generation == null) throw new InvalidOperationException("Missing run generation definition.");
            // The local session selects a seed once; a future authority can explicitly supply it.
            // Neither seed selection nor Unity state is consulted inside Core generation.
            var seed = runSeed ?? (Debug.isDebugBuild && useFixedSeed ? fixedSeed : CreateRunSeed());
            Run = new RunGenerator().Generate(seed, generation.CreateConfiguration());
            Simulation = new PlayerSimulation(identity, movementSpeed);
            motor.Bind(Simulation);
            follower.Bind(motor.transform);
            combat.Bind(Simulation, startingWeapon != null ? startingWeapon.CreateConfiguration() : null);
            input.Bind(Simulation, flow, combat);
            if (Debug.isDebugBuild)
                Debug.Log($"[RunGeneration] seed={Run.Seed} version={Run.GenerationVersion} nodes={Run.Nodes.Count} connections={Run.Connections.Count} start={Run.StartRoomId} terminal={Run.TerminalRoomId}", this);
            // The original isolated control/combat test prefab intentionally has no loader.
            // Normal New Game requires the room-enabled composition in ApplicationRoot.
            if (rooms != null) rooms.Bind(this, flow, generation);
        }

        private static ulong CreateRunSeed()
        {
            var bytes = new byte[8];
            using (var source = RandomNumberGenerator.Create()) source.GetBytes(bytes);
            ulong seed = 0;
            for (var i = 0; i < bytes.Length; i++) seed |= (ulong)bytes[i] << (8 * i);
            return seed;
        }

        private void Update() => input.SampleMovement();
        private void LateUpdate()
        {
            if (Simulation == null) return;
            motor.CaptureState();
            follower.Follow();
            // Recalculate even when the pointer is stationary, using the camera's current presentation.
            input.SampleAim(follower.GameplayCamera, motor.transform.position);
            input.SampleAttack();
            presentation.Present(Simulation.State);
        }
    }
}
