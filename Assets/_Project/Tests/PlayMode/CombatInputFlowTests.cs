using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class CombatInputFlowTests
    {
        private ApplicationRoot root;
        private GameplaySession session;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
        private int lastUiInputFrame = -1;
        private static readonly PropertyInfo DefaultUiEventSystem = typeof(VisualElement).Assembly
            .GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
            .GetProperty("defaultEventSystem", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateUiInput = DefaultUiEventSystem.PropertyType.GetMethod("Update");
        private static readonly object AlwaysUpdateUiInput = Enum.Parse(UpdateUiInput.GetParameters()[0].ParameterType, "Always");
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousInputBehavior;
#endif
        private CombatSimulation Combat => session.Combat.Simulation;
        private Vector2 GamePoint => new Vector2(Screen.width * 0.65f, Screen.height * 0.5f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            previousInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity", LoadSceneMode.Single);
            yield return WaitFor(() => (root = Object.FindFirstObjectByType<ApplicationRoot>()) != null && root.Flow.State == ApplicationState.MainMenu);
            root.Flow.TryRequest(ApplicationCommand.NewGame);
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay;
            session.Input.HandleFocus(true);
            MouseAt(GamePoint, false);
            yield return Frames(3);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousInputBehavior;
#endif
            foreach (var device in new InputDevice[] { keyboard, mouse, gamepad })
                if (device != null && device.added) InputSystem.RemoveDevice(device);
            if (root != null) Object.Destroy(root.gameObject);
            if (session != null) Object.Destroy(session.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MouseAndGamepadUseOnePressBoundaryWithCooldownAndIndependentMovement()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            MouseAt(GamePoint, true);
            yield return WaitFor(() => Combat.AcceptedAttackCount == 1);
            Assert.That(Combat.LastExecution.Attacker, Is.EqualTo(session.Simulation.State.Player));
            Assert.That(Combat.LastExecution.AimDirection.X, Is.EqualTo(1).Within(0.002f));
            Assert.That(Math.Abs(Combat.LastExecution.AimDirection.Y), Is.LessThan(0.002f));
            yield return new WaitForFixedUpdate();
            Assert.That(session.Motor.Body.linearVelocity.y, Is.EqualTo(5).Within(0.02f));
            // A new press before cooldown expires is rejected, and is not queued for later.
            MouseAt(GamePoint, false);
            yield return Frames(2);
            MouseAt(GamePoint, true);
            yield return Frames(2);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(1));
            yield return new WaitForSeconds(Combat.EquippedWeapon.Cooldown + 0.1f);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(1), "Holding does not repeat or buffer a rejected press.");
            MouseAt(GamePoint, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Frames(3);
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.left, rightStick = Vector2.up }.WithButton(GamepadButton.West));
            yield return WaitFor(() => Combat.AcceptedAttackCount == 2);
            Assert.That(Combat.LastExecution.Attacker, Is.EqualTo(session.Simulation.State.Player));
            Assert.That(Combat.LastExecution.Weapon, Is.SameAs(Combat.EquippedWeapon));
            Assert.That(Combat.LastExecution.AimDirection.Y, Is.EqualTo(1).Within(0.002f));
            Assert.That(Math.Abs(Combat.LastExecution.AimDirection.X), Is.LessThan(0.002f));
            yield return new WaitForFixedUpdate();
            Assert.That(session.Motor.Body.linearVelocity.x, Is.EqualTo(-5).Within(0.02f));
            yield return new WaitForSeconds(Combat.EquippedWeapon.Cooldown + 0.1f);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(2), "Gamepad hold also does not repeat.");
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return Frames(3);
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = Vector2.down }.WithButton(GamepadButton.West));
            yield return WaitFor(() => Combat.AcceptedAttackCount == 3);
            Assert.That(Combat.LastExecution.AimDirection.Y, Is.EqualTo(-1).Within(0.002f));
        }

        [UnityTest]
        public IEnumerator UiPauseResumeFocusAndNewSessionCannotLeakHeldAttacks()
        {
            var pausePoint = ButtonPoint("pause");
            var pointerDowns = 0;
            var pauseButton = root.CurrentScreen.View.Q<Button>("pause");
            root.CurrentScreen.View.RegisterCallback<PointerDownEvent>(e =>
            { if (ReferenceEquals(e.target, pauseButton)) pointerDowns++; }, TrickleDown.TrickleDown);
            Assert.That(root.CurrentScreen.IsPointerOverInteractiveUi(pausePoint), Is.True);
            Assert.That(root.CurrentScreen.IsPointerOverInteractiveUi(GamePoint), Is.False);
            MouseAt(pausePoint, true);
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.Zero, "The HUD's mouse-down must not fire before its click pauses.");
            MouseAt(pausePoint, false);
            yield return WaitFor(() => root.Flow.State == ApplicationState.Paused);
            Assert.That(pointerDowns, Is.EqualTo(1), "Pause must receive the synthetic pointer press.");
            Assert.That(mouse.leftButton.isPressed, Is.False, "Pause must complete after the synthetic pointer release.");
            MouseAt(GamePoint, true);
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.Zero);
            Submit("resume");
            yield return Frames(3);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Gameplay));
            Assert.That(Combat.AcceptedAttackCount, Is.Zero, "A press held across Resume must be released first.");
            MouseAt(GamePoint + Vector2.right * 10, true);
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.Zero);
            MouseAt(GamePoint, false);
            yield return Frames(3);
            MouseAt(GamePoint, true);
            yield return WaitFor(() => Combat.AcceptedAttackCount == 1);
            MouseAt(GamePoint, false);
            yield return WaitFor(() => Combat.CooldownRemaining == 0);

            // Exercise a real pointer click on Resume as well as a held-button transition.
            yield return ClickButton("pause");
            yield return WaitFor(() => root.Flow.State == ApplicationState.Paused);
            yield return ClickButton("resume");
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(1));
            session.Input.HandleFocus(false);
            MouseAt(GamePoint, true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(1));
            session.Input.HandleFocus(true);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Paused));
            Submit("resume");
            yield return new WaitForFixedUpdate();
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.EqualTo(1));
            Assert.That(session.Motor.Body.linearVelocity, Is.EqualTo(Vector2.zero));
            MouseAt(GamePoint, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Frames(3);
            MouseAt(GamePoint, true);
            yield return WaitFor(() => Combat.AcceptedAttackCount == 2);
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
            yield return WaitFor(() => root.Flow.State == ApplicationState.MainMenu);
            Assert.That(session == null, Is.True);
            Assert.That(Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None), Is.Empty);
            Submit("new-game");
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay;
            session.Input.HandleFocus(true);
            yield return Frames(3);
            Assert.That(Combat.AcceptedAttackCount, Is.Zero, "Held input cannot carry an attack into a new session.");
            MouseAt(GamePoint, false);
            yield return Frames(3);
            MouseAt(GamePoint, true);
            yield return WaitFor(() => Combat.AcceptedAttackCount == 1);
        }

        private void MouseAt(Vector2 position, bool pressed) => InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(pressed ? 1 : 0) });
        private Vector2 ButtonPoint(string name)
        {
            var button = root.CurrentScreen.View.Q<Button>(name);
            Assert.That(button, Is.Not.Null);
            var panel = button.panel;
            var origin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            var scale = RuntimePanelUtils.ScreenToPanel(panel, Vector2.one) - origin;
            var topLeft = (button.worldBound.center - origin) / scale;
            return new Vector2(topLeft.x, Screen.height - topLeft.y);
        }
        private IEnumerator ClickButton(string name)
        {
            var point = ButtonPoint(name);
            MouseAt(point, false);
            yield return Frames(2);
            MouseAt(point, true);
            yield return Frames(3);
            MouseAt(point, false);
            yield return Frames(3);
        }
        private void Submit(string name)
        {
            var button = root.CurrentScreen.View.Q<Button>(name);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }
        // Unity 6000.3's default UI event system ignores unfocused desktop input, even when
        // Input System's IgnoreFocus is enabled. Batch tests have no focused Game view.
        // Pump the existing provider/dispatcher once per unfocused frame; keep real pointer
        // picking, capture and Button click handling rather than substituting a submit event.
        // This version-sensitive engine seam is confined to tests; no global setting changes.
        private void UpdateUnfocusedUiInput()
        {
            if (Application.isFocused || lastUiInputFrame == Time.frameCount) return;
            lastUiInputFrame = Time.frameCount;
            UpdateUiInput.Invoke(DefaultUiEventSystem.GetValue(null), new[] { AlwaysUpdateUiInput });
        }
        private IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) { yield return null; UpdateUnfocusedUiInput(); }
        }
        private IEnumerator WaitFor(Func<bool> condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), $"Timed out waiting for condition at line {line}.");
                yield return null;
                UpdateUnfocusedUiInput();
            }
            yield return null;
        }
    }
}
