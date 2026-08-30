using System;
using System.IO;
using System.Reflection;
using System.Text;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

namespace SpaceCrawler.Editor
{
    public static class FoundationValidation
    {
        public const string ResultsDirectory = "Logs/Phase2";

        [MenuItem("Tools/Space Crawler/Validation/Run Edit Mode Tests")]
        public static void RunEditModeTests() => RunTests(TestMode.EditMode, "SpaceCrawler.Tests.EditMode");

        [MenuItem("Tools/Space Crawler/Validation/Run Play Mode Tests")]
        public static void RunPlayModeTests() => RunTests(TestMode.PlayMode, "SpaceCrawler.Tests.PlayMode");

        private static void RunTests(TestMode mode, string assembly)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                throw new InvalidOperationException("Run validation with the Editor idle in Edit Mode.");

            Directory.CreateDirectory(ResultsDirectory);
            var recorder = ScriptableObject.CreateInstance<FoundationTestRecorder>();
            recorder.hideFlags = HideFlags.HideAndDontSave;
            recorder.Begin(mode, $"{ResultsDirectory}/{mode}.xml");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                api.Execute(new ExecutionSettings(new Filter { testMode = mode, assemblyNames = new[] { assembly } }));
            }
            catch (Exception exception)
            {
                recorder.OnError(exception.Message);
                throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(api); }
        }

        [MenuItem("Tools/Space Crawler/Validation/Report State")]
        public static void ReportState()
        {
            Directory.CreateDirectory(ResultsDirectory);
            var report = new StringBuilder();
            report.AppendLine($"UTC: {DateTime.UtcNow:O}\nUnity: {Application.unityVersion}\nPlaying: {EditorApplication.isPlaying}\nCompiling: {EditorApplication.isCompiling}");
            report.AppendLine($"Scene: {SceneManager.GetActiveScene().path}\nScene dirty: {SceneManager.GetActiveScene().isDirty}");
            var profile = BuildProfile.GetActiveBuildProfile();
            report.AppendLine($"Build target: {EditorUserBuildSettings.activeBuildTarget}\nProfile: {AssetDatabase.GetAssetPath(profile)}\nDevelopment: {EditorUserBuildSettings.development}");
            foreach (var scene in EditorBuildSettings.scenes)
                report.AppendLine($"Build scene: {scene.enabled} {scene.path}");
            foreach (var scene in EditorBuildSettings.globalScenes)
                report.AppendLine($"Global scene: {scene.enabled} {scene.path}");
            // Inspection only: runtime composition never uses object discovery.
            var roots = UnityEngine.Object.FindObjectsByType<ApplicationRoot>(FindObjectsSortMode.None);
            report.AppendLine($"Application roots: {roots.Length}");
            foreach (var root in roots)
            {
                report.AppendLine($"Application state: {root.Flow?.State}");
                var session = root.CurrentGameplay;
                if (session != null)
                {
                    report.AppendLine($"Player: {session.Simulation.State.Player}; position={session.Simulation.State.Position}; velocity={session.Simulation.State.Velocity}; desired={session.Simulation.DesiredVelocity}; aim={session.Simulation.State.AimDirection}");
                    report.AppendLine($"Camera: position={session.Follower.transform.position}; rotation={session.Follower.transform.eulerAngles}; pixels={session.Follower.GameplayCamera.pixelRect}");
                    report.AppendLine($"Input: active={session.Input.GameplayInputEnabled}; waitingForNeutral={session.Input.MovementNeedsNeutral}; source={session.Input.AimSource}; focus={Application.isFocused}; frame={Time.frameCount}");
                }
                if (root.CurrentScreen == null) continue;
                foreach (var name in new[] { "new-game", "continue", "load-game", "options", "exit", "return-to-menu", "pause", "resume", "pause-return-to-menu" })
                {
                    var button = root.CurrentScreen.View.Q<Button>(name);
                    if (button != null) report.AppendLine($"Button {name}: enabled={button.enabledInHierarchy}, bounds={button.worldBound}");
                }
            }
            report.AppendLine($"Gamepads: {Gamepad.all.Count}; deadzone={InputSystem.settings.defaultDeadzoneMin}..{InputSystem.settings.defaultDeadzoneMax}");
            foreach (var gamepad in Gamepad.all) report.AppendLine($"Gamepad: {gamepad.displayName}, interface={gamepad.description.interfaceName}, native={gamepad.native}");
            var entries = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
            var counts = entries?.GetMethod("GetCountsByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (counts != null)
            {
                object[] values = { 0, 0, 0 };
                counts.Invoke(null, values);
                report.AppendLine($"Console: {values[0]} errors, {values[1]} warnings, {values[2]} logs (not cleared)");
            }
            File.WriteAllText(ResultsDirectory + "/EditorState.txt", report.ToString());
        }
    }

}
