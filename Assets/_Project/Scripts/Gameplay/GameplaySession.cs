using System;
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
        public PlayerSimulation Simulation { get; private set; }
        public PlayerMotor Motor => motor;
        public PlayerInputAdapter Input => input;
        public PlayerCameraFollower Follower => follower;

        public void Bind(PlayerIdentity identity, ApplicationFlow flow)
        {
            if (Simulation != null) throw new InvalidOperationException("Session is already bound.");
            Simulation = new PlayerSimulation(identity, movementSpeed);
            motor.Bind(Simulation);
            follower.Bind(motor.transform);
            input.Bind(Simulation, flow);
        }

        private void Update() => input.SampleMovement();
        private void LateUpdate()
        {
            if (Simulation == null) return;
            motor.CaptureState();
            follower.Follow();
            // Recalculate even when the pointer is stationary, using the camera's current presentation.
            input.SampleAim(follower.GameplayCamera, motor.transform.position);
            presentation.Present(Simulation.State);
        }
    }
}
