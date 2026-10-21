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
    public static class ProjectValidationTools
    {
        public const string ResultsDirectory = "Logs/Validation";

        [MenuItem("Tools/Space Crawler/Validation/Run Edit Mode Tests")]
        public static void RunEditModeTests() => RunTests(TestMode.EditMode, "SpaceCrawler.Tests.EditMode");

        [MenuItem("Tools/Space Crawler/Validation/Run Play Mode Tests")]
        public static void RunPlayModeTests() => RunTests(TestMode.PlayMode, "SpaceCrawler.Tests.PlayMode");

        private static void RunTests(TestMode mode, string assembly)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Run validation with the Editor idle in Edit Mode.");

            Directory.CreateDirectory(ResultsDirectory);
            var recorder = ScriptableObject.CreateInstance<TestResultRecorder>();
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
            report.AppendLine($"Script compilation failed: {EditorUtility.scriptCompilationFailed}");
            report.AppendLine($"Native focus notifications enabled: {PlayerInputAdapter.ReceiveNativeFocusEvents}");
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
                if (root.CurrentMultiplayer != null) report.AppendLine(root.CurrentMultiplayer.Describe());
                var session = root.CurrentGameplay;
                if (session != null)
                {
                    if (session.Run != null)
                        report.AppendLine($"Run: seed={session.Run.Seed}; generationVersion={session.Run.GenerationVersion}; nodes={session.Run.Nodes.Count}; connections={session.Run.Connections.Count}; start={session.Run.StartRoomId}; terminal={session.Run.TerminalRoomId}");
                    if (session.Rooms != null && session.Rooms.Progression != null)
                    {
                        var rooms = session.Rooms;
                        var progression = rooms.Progression;
                        report.AppendLine($"Room: current={progression.CurrentRoomId}; definition={session.Run.GetRoom(progression.CurrentRoomId).DefinitionId}; state={progression.State}; entered={progression.RoomsEntered}; containerChildren={rooms.RoomContainer.childCount}; spawn={rooms.CurrentRoom?.PlayerSpawnAnchor.position}; registeredColliders={session.Combat.RegisteredColliderCount}");
                        foreach (var choice in progression.Choices) report.AppendLine($"Room choice: {choice.From} -> {choice.To}");
                        if (rooms.CurrentRoom != null)
                            foreach (var exit in new[] { rooms.CurrentRoom.SingleExit, rooms.CurrentRoom.LeftExit, rooms.CurrentRoom.RightExit })
                                report.AppendLine($"Exit: active={exit.gameObject.activeSelf}; source={exit.Source}; destination={exit.Destination}; position={exit.transform.position}");
                    }
                    report.AppendLine($"Player: {session.Simulation.State.Player}; position={session.Simulation.State.Position}; velocity={session.Simulation.State.Velocity}; desired={session.Simulation.DesiredVelocity}; aim={session.Simulation.State.AimDirection}");
                    report.AppendLine($"Camera: position={session.Follower.transform.position}; rotation={session.Follower.transform.eulerAngles}; pixels={session.Follower.GameplayCamera.pixelRect}");
                    report.AppendLine($"Input: active={session.Input.GameplayInputEnabled}; waitingForNeutral={session.Input.MovementNeedsNeutral}; source={session.Input.AimSource}; focus={Application.isFocused}; frame={Time.frameCount}");
                    var combat = session.Combat;
                    if (combat != null && combat.Simulation != null)
                    {
                        report.AppendLine($"Combat: weapon={combat.Simulation.EquippedWeapon?.Id}; attacks={combat.Simulation.AcceptedAttackCount}; cooldown={combat.Simulation.CooldownRemaining}; projectiles={combat.Projectiles.Count}");
                        foreach (var target in combat.Targets)
                            report.AppendLine($"Target: id={target.Identity}; position={target.transform.position}; health={target.Health.Current}/{target.Health.Maximum}; alive={target.Health.IsAlive}");
                    }
                }
                if (root.CurrentScreen == null) continue;
                foreach (var name in new[] { "new-game", "continue", "load-game", "options", "exit", "return-to-menu", "pause", "resume", "pause-return-to-menu" })
                {
                    var button = root.CurrentScreen.View.Q<Button>(name);
                    if (button != null) report.AppendLine($"Button {name}: enabled={button.enabledInHierarchy}, bounds={button.worldBound}");
                }
            }
            report.AppendLine($"Gamepads: {Gamepad.all.Count}; deadzone={InputSystem.settings.defaultDeadzoneMin}..{InputSystem.settings.defaultDeadzoneMax}");
            report.AppendLine($"Input settings: background={InputSystem.settings.backgroundBehavior}; editor={InputSystem.settings.editorInputBehaviorInPlayMode}");
            foreach (var device in InputSystem.devices)
                if (device.native) report.AppendLine($"Native device: {device.displayName}; enabled={device.enabled}");
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
