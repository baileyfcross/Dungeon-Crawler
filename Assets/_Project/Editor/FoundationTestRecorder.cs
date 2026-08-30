using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

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

        private void OnEnable()
        {
            if (recording) TestRunnerApi.RegisterTestCallback(this);
        }

        private void OnDisable() => TestRunnerApi.UnregisterTestCallback(this);

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
                    $"{result.SkipCount} skipped; {result.Duration:F3}s");
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
