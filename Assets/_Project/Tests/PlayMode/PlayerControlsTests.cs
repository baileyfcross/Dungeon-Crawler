using System;
using System.Collections;
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
    public sealed class PlayerControlsTests
    {
        private ApplicationRoot root;
        private GameplaySession session;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            // Synthetic keyboard/pointer events must not depend on which Editor tab owns OS focus.
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
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
            Assert.That(session, Is.Not.Null);
            session.Input.HandleFocus(true);
            yield return Frames(3);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
#endif
            foreach (var device in new InputDevice[] { keyboard, mouse, gamepad })
                if (device != null && device.added) InputSystem.RemoveDevice(device);
            if (root != null) Object.Destroy(root.gameObject);
            if (session != null) Object.Destroy(session.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardMovementIsIndependentClampedAndBlockedByBoundary()
        {
            foreach (var entry in new[] { (Key.W, Vector2.up), (Key.A, Vector2.left), (Key.S, Vector2.down), (Key.D, Vector2.right) })
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(entry.Item1));
                yield return WaitFor(() => Vector2.Distance(new Vector2(session.Simulation.DesiredVelocity.X, session.Simulation.DesiredVelocity.Y), entry.Item2 * 5) < 0.01f);
                yield return FixedSteps(2);
                AssertVector(session.Motor.Body.linearVelocity, entry.Item2 * 5, 0.02f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return Frames(3);
            }
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width / 2f - 100, Screen.height / 2f) });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
            yield return new WaitForSeconds(0.25f);
            Assert.That(session.Motor.Body.linearVelocity.magnitude, Is.EqualTo(5).Within(0.02f));
            Assert.That(session.Motor.Body.linearVelocity.x, Is.GreaterThan(3.5f));
            AssertVector(Aim(), Vector2.left, 0.02f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return new WaitForSeconds(3);
            Assert.That(session.Motor.Body.position.x, Is.InRange(11.5f, 11.66f), "Player radius must remain inside the east wall.");
            Assert.That(Mathf.Abs(session.Motor.Body.linearVelocity.x), Is.LessThan(0.01f));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
            yield return WaitFor(() => session.Simulation.DesiredVelocity.Y > 3.5f);
            var beforeSlide = session.Motor.Body.position.y;
            yield return FixedSteps(15);
            Assert.That(session.Motor.Body.position.x, Is.LessThan(11.66f));
            Assert.That(session.Motor.Body.position.y, Is.GreaterThan(beforeSlide + 0.6f), "Zero friction permits sliding along the wall.");
        }

        [UnityTest]
        public IEnumerator AbsolutePointerTracksCurrentCameraAndKeepsFixedOrientation()
        {
            var camera = session.Follower.GameplayCamera;
            foreach (var direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down, Vector2.one.normalized })
            {
                var target = camera.WorldToScreenPoint(session.Motor.transform.position + (Vector3)direction * 2);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = target });
                yield return Frames(3);
                AssertVector(Aim(), direction, 0.002f);
            }
            var fixedPointer = (Vector2)camera.WorldToScreenPoint(session.Motor.transform.position + new Vector3(2, 1, 0));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = fixedPointer });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            var before = session.Motor.Body.position;
            yield return new WaitForSeconds(0.4f);
            Assert.That(session.Motor.Body.position.x, Is.GreaterThan(before.x + 1.5f));
            AssertVector(Aim(), new Vector2(2, 1).normalized, 0.01f);
            Assert.That(Vector2.Distance(camera.transform.position, session.Motor.transform.position), Is.LessThan(0.11f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.08f);
            var retained = Aim();
            var center = camera.pixelRect.center;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
            yield return Frames(3);
            AssertVector(Aim(), retained, 0.002f);
            // A camera displacement changes the world target even without a pointer event.
            camera.transform.position += new Vector3(1, 0, 0);
            session.Input.SampleAim(camera, session.Motor.transform.position);
            AssertVector(Aim(), Vector2.right, 0.002f);
            session.Follower.Follow();
            Assert.That(Quaternion.Angle(camera.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator GamepadAimDeadzonePauseResumeAndFocusUseTheSameSimulation()
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.left, rightStick = new Vector2(1, 1).normalized });
            yield return new WaitForSeconds(0.15f);
            AssertVector(session.Motor.Body.linearVelocity, Vector2.left * 5, 0.02f);
            AssertVector(Aim(), Vector2.one.normalized, 0.002f);
            Assert.That(session.Input.AimSource, Is.EqualTo("Right stick"));
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = new Vector2(0.02f, -0.03f) });
            yield return Frames(3);
            AssertVector(Aim(), Vector2.one.normalized, 0.002f);
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.Start));
            yield return Frames(3);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Paused));
            yield return new WaitForFixedUpdate();
            var pausedPosition = session.Motor.Body.position;
            var pausedAim = Aim();
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right, rightStick = Vector2.down });
            yield return new WaitForSeconds(0.2f);
            AssertVector(session.Motor.Body.position, pausedPosition, 0.001f);
            AssertVector(Aim(), pausedAim, 0.001f);
            Assert.That(root.CurrentScreen.View.Q<Button>("options").enabledInHierarchy, Is.False);
            Submit(root.CurrentScreen.View.Q<Button>("resume"));
            yield return new WaitForSeconds(0.15f);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Gameplay));
            AssertVector(session.Motor.Body.position, pausedPosition, 0.001f);
            AssertVector(Aim(), pausedAim, 0.001f);
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return Frames(3);
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.up, rightStick = Vector2.down });
            yield return new WaitForSeconds(0.15f);
            AssertVector(session.Motor.Body.linearVelocity, Vector2.up * 5, 0.02f);
            AssertVector(Aim(), Vector2.down, 0.002f);
            session.Input.HandleFocus(false);
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return new WaitForSeconds(0.08f);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Paused));
            AssertVector(session.Motor.Body.linearVelocity, Vector2.zero, 0.001f);
            session.Input.HandleFocus(true);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Paused), "Focus return must not silently resume.");
            Submit(root.CurrentScreen.View.Q<Button>("resume"));
            yield return Frames(3);
            AssertVector(session.Motor.Body.linearVelocity, Vector2.zero, 0.001f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return new WaitForSeconds(0.15f);
            AssertVector(session.Motor.Body.linearVelocity, Vector2.right * 5, 0.02f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return Frames(3);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Paused));
            Submit(root.CurrentScreen.View.Q<Button>("pause-return-to-menu"));
            yield return WaitFor(() => root.Flow.State == ApplicationState.MainMenu);
            Assert.That(session == null, Is.True, "The old gameplay session must unload with its scene.");
            Assert.That(root.CurrentGameplay, Is.Null);
        }

        private Vector2 Aim() => new Vector2(session.Simulation.State.AimDirection.X, session.Simulation.State.AimDirection.Y);
        private static void AssertVector(Vector2 actual, Vector2 expected, float tolerance) => Assert.That(Vector2.Distance(actual, expected), Is.LessThan(tolerance), $"Expected {expected}, got {actual}.");
        private static IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }
        private static IEnumerator FixedSteps(int count) { for (var i = 0; i < count; i++) yield return new WaitForFixedUpdate(); }
        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            yield return Frames(2);
        }
        private static void Submit(Button button)
        {
            Assert.That(button.enabledInHierarchy, Is.True);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }
    }
}
