using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace SpaceCrawler.Tests.PlayMode
{
    /// <summary>Keep real desktop events from competing with the suites' explicitly created test devices.</summary>
    [SetUpFixture]
    public sealed class SyntheticDeviceTestScope
    {
        private readonly List<InputDevice> temporarilyDisabled = new List<InputDevice>();
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;

        [OneTimeSetUp]
        public void IsolateSyntheticInput()
        {
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            // Editor focus events otherwise disable newly added synthetic devices as well.
            // Gameplay pause/focus behavior is still exercised through HandleFocus in tests.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var device in InputSystem.devices)
            {
                if (!device.native || !device.enabled || !(device is Keyboard || device is Pointer || device is Gamepad)) continue;
                temporarilyDisabled.Add(device);
                // Frontend only: do not stop native event delivery to the Editor/operating system.
                InputSystem.DisableDevice(device, keepSendingEvents: true);
            }
        }

        [OneTimeTearDown]
        public void RestoreNativeDevices()
        {
            foreach (var device in temporarilyDisabled) if (device.added) InputSystem.EnableDevice(device);
            temporarilyDisabled.Clear();
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        }
    }
}
