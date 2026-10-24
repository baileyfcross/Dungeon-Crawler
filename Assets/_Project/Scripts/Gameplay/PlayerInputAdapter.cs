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
        private InputAction move, aim, pause, attack, interact, sprint;
        private bool sprintNeedsRelease;
        private bool interactNeedsRelease;
        private Vector2Control pointer, stick;
        private bool useStick, movementNeedsNeutral, stickNeedsNeutral;
        private bool focused = true;
        private int activationFrame;
        private IPlayerCommands commands;
        private ApplicationFlow flow;
        private Func<Vector2, bool> pointerOverUi;
        private Func<bool> pauseInterceptor;
        public void SetPauseInterceptor(Func<bool> interceptor)=>pauseInterceptor=interceptor;
        private bool attackNeedsRelease;
        private bool roomAvailable = true;
        private bool modalAvailable = true;
        public void SetModalAvailable(bool available)
        { if(modalAvailable==available)return;modalAvailable=available;if(flow!=null)ApplyGate(flow.State); }
        private bool continueRoomMovement, continueRoomSprint;
        public bool GameplayInputEnabled => commands != null && commands.InputActive;
        public bool MovementNeedsNeutral => movementNeedsNeutral;
        public string AimSource => useStick ? "Right stick" : "Absolute pointer";

        public void Bind(PlayerSimulation playerSimulation, ApplicationFlow applicationFlow, CombatResolver combatResolver = null)
            => Bind(new LocalPlayerCommands(playerSimulation, combatResolver), applicationFlow);

        public void Bind(IPlayerCommands commandTarget, ApplicationFlow applicationFlow)
        {
            if (commands != null) throw new InvalidOperationException("Input is already bound.");
            commands = commandTarget ?? throw new ArgumentNullException(nameof(commandTarget));
            flow = applicationFlow;
            // One explicitly owned local map from the canonical asset; UI keeps its project-wide map.
            playerMap = actions.FindActionMap("Player", true).Clone();
            move = playerMap.FindAction("Move", true);
            aim = playerMap.FindAction("Aim", true);
            pause = playerMap.FindAction("Pause", true);
            attack = playerMap.FindAction("Attack", true);
            interact = playerMap.FindAction("Interact", true);
            sprint = playerMap.FindAction("Sprint", true);
            aim.performed += ObserveAimDevice;
            flow.StateChanged += ApplyGate;
            ApplyGate(flow.State);
        }

        private void ApplyGate(ApplicationState state)
        {
            if (!accountAvailable || !focused || !modalAvailable || state != ApplicationState.Gameplay || flow.RunEndPending)
                continueRoomMovement = continueRoomSprint = false;
            var active = accountAvailable && focused && modalAvailable && roomAvailable && state == ApplicationState.Gameplay && !flow.RunEndPending;
            commands.SetInputActive(active);
            if (active)
            {
                activationFrame = Time.frameCount;
                movementNeedsNeutral = true;
                stickNeedsNeutral = true;
                attackNeedsRelease = true;
                interactNeedsRelease = true;
                sprintNeedsRelease = true;
                move.Enable();
                aim.Enable();
                attack.Enable();
                interact.Enable();
                sprint.Enable();
                foreach (var control in aim.controls)
                {
                    if (control.device is Pointer && control is Vector2Control position) pointer = position;
                    if (control.device is Gamepad && control is Vector2Control rightStick) stick = rightStick;
                }
            }
            else
            {
                // Keep live movement controls sampled during an ordinary room load, but submit no commands.
                // Pause, focus/account loss and run end clear continuity above and disable these too.
                if (!continueRoomMovement) move.Disable();
                if (!continueRoomSprint) sprint.Disable();
                aim.Disable(); attack.Disable(); interact.Disable();
            }
            if (focused && (state == ApplicationState.Gameplay || state == ApplicationState.Paused)) pause.Enable();
            else pause.Disable();
        }

        public void SampleMovement()
        {
            if (commands == null) return;
            if (pause.enabled && pause.WasPressedThisFrame())
            {
                if(pauseInterceptor?.Invoke()==true)return;
                flow.TryRequest(flow.State == ApplicationState.Paused ? ApplicationCommand.Resume : ApplicationCommand.Pause);
                return;
            }
            if (!GameplayInputEnabled || Time.frameCount <= activationFrame) return;
            if (!interact.IsPressed()) interactNeedsRelease = false;
            if (!sprint.IsPressed()) sprintNeedsRelease = false;
            commands.Interact(commands.State.Player, !interactNeedsRelease && interact.IsPressed());
            var direction = move.ReadValue<Vector2>();
            if (movementNeedsNeutral)
            {
                if (direction.sqrMagnitude < 0.000001f) movementNeedsNeutral = false;
                return;
            }
            commands.Move(new MoveIntent(commands.State.Player, new CoreVector(direction.x, direction.y), !sprintNeedsRelease && sprint.IsPressed()));
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
            commands.Aim(new AimIntent(commands.State.Player, new CoreVector(direction.x, direction.y)));
        }

        public static Vector2 PointerWorldDirection(Camera camera, Vector2 screenPosition, Vector2 playerPosition)
        {
            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            return plane.Raycast(ray, out var distance) ? (Vector2)ray.GetPoint(distance) - playerPosition : Vector2.zero;
        }

        public void SetUiGate(Func<Vector2, bool> isPointerOverInteractiveUi) => pointerOverUi = isPointerOverInteractiveUi;

        // The room orchestration supplies availability; input still submits only intentions.
        private bool accountAvailable=true;
        public void SetAccountAvailable(bool available)
        { accountAvailable=available;if(flow!=null)ApplyGate(flow.State); }
        public void SetRoomAvailable(bool available)
        {
            if (available == roomAvailable) return;
            if (!available)
            {
                continueRoomMovement = GameplayInputEnabled && !movementNeedsNeutral;
                continueRoomSprint = continueRoomMovement && !sprintNeedsRelease;
            }
            roomAvailable = available;
            if (flow != null) ApplyGate(flow.State);
            if (available)
            {
                // Read the current device state after entry; never replay a cached movement vector.
                if (GameplayInputEnabled && continueRoomMovement)
                {
                    movementNeedsNeutral = false;
                    sprintNeedsRelease = !continueRoomSprint;
                }
                continueRoomMovement = continueRoomSprint = false;
            }
        }

        // Called after SampleAim: the command carries accepted gameplay aim, never raw device aim.
        public void SampleAttack()
        {
            if (!GameplayInputEnabled || Time.frameCount <= activationFrame) return;
            if (attackNeedsRelease)
            {
                if (!attack.IsPressed()) attackNeedsRelease = false;
                return;
            }
            if (!attack.WasPressedThisFrame()) return;
            if (attack.activeControl?.device is Pointer attackPointer && pointerOverUi != null
                && pointerOverUi(attackPointer.position.ReadValue())) return;
            commands.Attack(new AttackIntent(commands.State.Player, commands.State.AimDirection));
        }

#if UNITY_EDITOR
        // Test-runner isolation only. Explicit HandleFocus calls still exercise the complete rule.
        // Native smoke tests leave this enabled; the switch is absent from standalone builds.
        public static bool ReceiveNativeFocusEvents { get; set; } = true;
#endif
        private void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_EDITOR
            if (!ReceiveNativeFocusEvents) return;
#endif
            HandleFocus(hasFocus);
        }

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
