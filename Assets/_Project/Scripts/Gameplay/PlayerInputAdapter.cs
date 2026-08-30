using System;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Translates local devices to identified intentions. Never writes player transforms or physics.</summary>
    public sealed class PlayerInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        private InputActionMap playerMap;
        private InputAction move, aim, pause;
        private Vector2Control pointer, stick;
        private bool useStick, movementNeedsNeutral, stickNeedsNeutral;
        private bool focused = true;
        private int activationFrame;
        private PlayerSimulation simulation;
        private ApplicationFlow flow;
        public bool GameplayInputEnabled => simulation != null && simulation.IsActive;
        public bool MovementNeedsNeutral => movementNeedsNeutral;
        public string AimSource => useStick ? "Right stick" : "Absolute pointer";

        public void Bind(PlayerSimulation playerSimulation, ApplicationFlow applicationFlow)
        {
            if (simulation != null) throw new InvalidOperationException("Input is already bound.");
            simulation = playerSimulation;
            flow = applicationFlow;
            // One explicitly owned local map from the canonical asset; UI keeps its project-wide map.
            playerMap = actions.FindActionMap("Player", true).Clone();
            move = playerMap.FindAction("Move", true);
            aim = playerMap.FindAction("Aim", true);
            pause = playerMap.FindAction("Pause", true);
            aim.performed += ObserveAimDevice;
            flow.StateChanged += ApplyGate;
            ApplyGate(flow.State);
        }

        private void ApplyGate(ApplicationState state)
        {
            var active = focused && state == ApplicationState.Gameplay;
            simulation.SetActive(active);
            if (active)
            {
                activationFrame = Time.frameCount;
                movementNeedsNeutral = true;
                stickNeedsNeutral = true;
                move.Enable();
                aim.Enable();
                foreach (var control in aim.controls)
                {
                    if (control.device is Pointer && control is Vector2Control position) pointer = position;
                    if (control.device is Gamepad && control is Vector2Control rightStick) stick = rightStick;
                }
            }
            else { move.Disable(); aim.Disable(); }
            if (focused && (state == ApplicationState.Gameplay || state == ApplicationState.Paused)) pause.Enable();
            else pause.Disable();
        }

        public void SampleMovement()
        {
            if (simulation == null) return;
            if (pause.enabled && pause.WasPressedThisFrame())
            {
                flow.TryRequest(flow.State == ApplicationState.Paused ? ApplicationCommand.Resume : ApplicationCommand.Pause);
                return;
            }
            if (!GameplayInputEnabled || Time.frameCount <= activationFrame) return;
            var direction = move.ReadValue<Vector2>();
            if (movementNeedsNeutral)
            {
                if (direction.sqrMagnitude < 0.000001f) movementNeedsNeutral = false;
                return;
            }
            simulation.RequestMove(new MoveIntent(simulation.State.Player, new CoreVector(direction.x, direction.y)));
        }

        private void ObserveAimDevice(InputAction.CallbackContext context)
        {
            if (!GameplayInputEnabled) return;
            if (context.control.device is Pointer && context.control is Vector2Control position)
            {
                pointer = position;
                useStick = false;
            }
            else if (context.control.device is Gamepad && context.control is Vector2Control rightStick)
            {
                stick = rightStick;
                if (!stickNeedsNeutral && context.ReadValue<Vector2>().sqrMagnitude > 0.000001f) useStick = true;
            }
        }

        public void SampleAim(Camera camera, Vector2 presentedPlayerPosition)
        {
            if (!GameplayInputEnabled || Time.frameCount <= activationFrame) return;
            // StickControl applies the Input System's stickDeadzone processor before this read.
            var stickDirection = stick != null ? stick.ReadValue() : Vector2.zero;
            if (stickNeedsNeutral && stickDirection.sqrMagnitude < 0.000001f) stickNeedsNeutral = false;
            Vector2 direction;
            if (useStick)
                direction = stickNeedsNeutral ? Vector2.zero : stickDirection;
            else if (pointer != null)
                direction = PointerWorldDirection(camera, pointer.ReadValue(), presentedPlayerPosition);
            else return;
            simulation.RequestAim(new AimIntent(simulation.State.Player, new CoreVector(direction.x, direction.y)));
        }

        public static Vector2 PointerWorldDirection(Camera camera, Vector2 screenPosition, Vector2 playerPosition)
        {
            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            return plane.Raycast(ray, out var distance) ? (Vector2)ray.GetPoint(distance) - playerPosition : Vector2.zero;
        }

        private void OnApplicationFocus(bool hasFocus) => HandleFocus(hasFocus);

        public void HandleFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (flow == null) return;
            if (!hasFocus) flow.TryRequest(ApplicationCommand.Pause);
            ApplyGate(flow.State);
        }

        private void OnDestroy()
        {
            if (flow != null) flow.StateChanged -= ApplyGate;
            if (aim != null) aim.performed -= ObserveAimDevice;
            playerMap?.Dispose();
        }
    }
}
