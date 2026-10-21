using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SpaceCrawler.Editor
{
    // A separate, matching script asset preserves this ScriptableObject across test domain reloads.
    public sealed class FoundationTestRecorder : ScriptableObject, IErrorCallbacks
    {
        [SerializeField] private string resultPath;
        [SerializeField] private SceneAsset previousStartupScene;
        [SerializeField] private TestMode expectedMode;
        [SerializeField] private bool recording;
        [SerializeField] private bool restoreStartupScene;
        [SerializeField] private int excludedNativeEvents;

        private void OnEnable()
        {
            if (recording) { TestRunnerApi.RegisterTestCallback(this); InstallInputIsolation(); }
        }

        private void OnDisable()
        { TestRunnerApi.UnregisterTestCallback(this); InputSystem.onEvent -= IsolateNativeInput; SpaceCrawler.Gameplay.PlayerInputAdapter.ReceiveNativeFocusEvents = true; }

        private void InstallInputIsolation()
        {
            InputSystem.onEvent -= IsolateNativeInput;
            if (expectedMode == TestMode.PlayMode)
            {
                InputSystem.onEvent += IsolateNativeInput;
                SpaceCrawler.Gameplay.PlayerInputAdapter.ReceiveNativeFocusEvents = false;
            }
        }

        // Live desktop events must not compete with the suites' explicit synthetic devices.
        // This is scoped to automated Play Mode runs; real-device smoke tests remain unfiltered.
        private void IsolateNativeInput(InputEventPtr inputEvent, InputDevice device)
        {
            if (!recording || !Application.isPlaying || device == null || !device.native
                || !(device is Pointer || device is Keyboard || device is Gamepad)
                || (!inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>())) return;
            inputEvent.handled = true; excludedNativeEvents++;
        }

        public void Begin(TestMode mode, string path)
        {
            expectedMode = mode;
            resultPath = path;
            recording = true;
            if (mode == TestMode.PlayMode)
            {
                previousStartupScene = EditorSceneManager.playModeStartScene;
                restoreStartupScene = true;
                EditorSceneManager.playModeStartScene = null;
            }
            TestRunnerApi.RegisterTestCallback(this);
            InstallInputIsolation();
        }

        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (!recording || result.Test.TestMode != expectedMode) return;
            StopRecording();
            try
            {
                TestRunnerApi.SaveResultToFile(result, resultPath);
                File.WriteAllText(Path.ChangeExtension(resultPath, ".summary.txt"),
                    $"{result.ResultState}: {result.PassCount} passed, {result.FailCount} failed, " +
                    $"{result.SkipCount} skipped; {result.Duration:F3}s\nNative desktop events excluded during synthetic-device tests: {excludedNativeEvents}");
            }
            finally { DestroyImmediate(this); }
        }

        public void OnError(string message)
        {
            if (!recording) return;
            StopRecording();
            try { File.WriteAllText(Path.ChangeExtension(resultPath, ".summary.txt"), "Test launch failed: " + message); }
            finally { DestroyImmediate(this); }
        }

        private void StopRecording()
        {
            recording = false;
            InputSystem.onEvent -= IsolateNativeInput;
            SpaceCrawler.Gameplay.PlayerInputAdapter.ReceiveNativeFocusEvents = true;
            // Unregister immediately: a later test run must never overwrite this run's report.
            TestRunnerApi.UnregisterTestCallback(this);
            if (restoreStartupScene)
            {
                restoreStartupScene = false;
                EditorSceneManager.playModeStartScene = previousStartupScene;
            }
        }
    }
}
