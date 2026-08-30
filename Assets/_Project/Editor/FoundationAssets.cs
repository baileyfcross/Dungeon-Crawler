using System;
using System.IO;
using System.Reflection;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    /// <summary>Small foundation authoring tools; preserve existing assets and only fill missing Boot wiring.</summary>
    public static class FoundationAssets
    {
        public const string Root = "Assets/_Project";
        public const string BootPath = Root + "/Scenes/Boot.unity";
        public const string MenuPath = Root + "/Scenes/MainMenu.unity";
        public const string GameplayPath = Root + "/Scenes/Gameplay.unity";
        public const string ProfilePath = Root + "/Settings/BuildProfiles/WindowsDevelopment.asset";

        [MenuItem("Tools/Space Crawler/Foundation/Create Assets")]
        public static void CreateAssets()
        {
            RequireIdleEditor();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Prefabs/UI");
            EnsureFolder(Root + "/Settings/BuildProfiles");
            var panel = CreatePanel();
            CreateScreen("MainMenu", "MainMenu", ApplicationState.MainMenu, panel);
            CreateScreen("GameplayPlaceholder", "GameplayPlaceholder", ApplicationState.Gameplay, panel);
            CreateScene(MenuPath);
            CreateScene(GameplayPath);
            CreateScene(BootPath);
            WireBootScene();
            CreateDevelopmentProfile();
            AssetDatabase.SaveAssets();
            OpenBoot();
        }

        private static PanelSettings CreatePanel()
        {
            const string path = Root + "/UI/FoundationPanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel != null) return panel;
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "FoundationPanel";
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/FoundationTheme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            AssetDatabase.CreateAsset(panel, path);
            return panel;
        }

        private static ApplicationScreen CreateScreen(string name, string uxml, ApplicationState state, PanelSettings panel)
        {
            var path = $"{Root}/Prefabs/UI/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<ApplicationScreen>();
            var temporary = new GameObject(name);
            try
            {
                var document = temporary.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Root}/UI/{uxml}.uxml");
                if (document.visualTreeAsset == null || panel.themeStyleSheet == null)
                    throw new InvalidOperationException("Import the foundation UI assets before creating prefabs.");
                var screen = temporary.AddComponent<ApplicationScreen>();
                var data = new SerializedObject(screen);
                data.FindProperty("document").objectReferenceValue = document;
                data.FindProperty("representedState").enumValueIndex = (int)state;
                data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(temporary, path).GetComponent<ApplicationScreen>();
            }
            finally { Object.DestroyImmediate(temporary); }
        }

        private static void CreateScene(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(10 / 255f, 18 / 255f, 29 / 255f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"Could not save {path}.");
        }

        private static void WireBootScene()
        {
            var scene = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            foreach (var sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent<ApplicationRoot>(out _)) return;

            // Resolve persistent prefab assets after scene creation/import, not stale transient references.
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/MainMenu.prefab").GetComponent<ApplicationScreen>();
            var gameplay = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/UI/GameplayPlaceholder.prefab").GetComponent<ApplicationScreen>();
            if (menu == null || gameplay == null)
                throw new InvalidOperationException("Both screen prefabs must contain ApplicationScreen.");
            var root = new GameObject("Application Root").AddComponent<ApplicationRoot>();
            var data = new SerializedObject(root);
            data.FindProperty("mainMenuScenePath").stringValue = MenuPath;
            data.FindProperty("gameplayScenePath").stringValue = GameplayPath;
            data.FindProperty("mainMenuScreen").objectReferenceValue = menu;
            data.FindProperty("gameplayScreen").objectReferenceValue = gameplay;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Boot composition wiring.");
        }

        private static void CreateDevelopmentProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null)
            {
                // Unity 6000.3 exposes activation/building publicly but its platform factory is internal.
                // Keep this version-sensitive editor-only call isolated; fail clearly after an API change.
                var factory = typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
                if (factory == null)
                    throw new NotSupportedException("Build Profile factory changed. Create a Windows profile in Build Profiles and assign ProfilePath.");
                profile = (BuildProfile)factory.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
                profile.name = "Windows Development";
                AssetDatabase.CreateAsset(profile, ProfilePath);
                profile.overrideGlobalScenes = true;
                profile.scenes = new[]
                {
                    new EditorBuildSettingsScene(BootPath, true),
                    new EditorBuildSettingsScene(MenuPath, true),
                    new EditorBuildSettingsScene(GameplayPath, true)
                };
            }
            BuildProfile.SetActiveBuildProfile(profile);
            EditorUserBuildSettings.development = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        [MenuItem("Tools/Space Crawler/Foundation/Open Boot")]
        public static void OpenBoot()
        {
            RequireIdleEditor();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootPath);
        }

        [MenuItem("Tools/Space Crawler/Foundation/Enter Play Mode")]
        public static void EnterPlayMode()
        {
            OpenBoot();
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/Space Crawler/Foundation/Exit Play Mode")]
        public static void ExitPlayMode() => EditorApplication.ExitPlaymode();

        [MenuItem("Tools/Space Crawler/Foundation/Build Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            RequireIdleEditor();
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Create the foundation assets first.");
            BuildProfile.SetActiveBuildProfile(profile);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 || !EditorUserBuildSettings.development)
                throw new InvalidOperationException("The profile must target Windows Intel 64-bit with Development Build enabled.");
            Directory.CreateDirectory("Builds/WindowsDevelopment");
            Directory.CreateDirectory(FoundationValidation.ResultsDirectory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = "Builds/WindowsDevelopment/Dungeon-Crawler.exe",
                options = BuildOptions.Development
            });
            // Persist package post-build cleanup (for example, temporary preloaded input assets).
            AssetDatabase.SaveAssets();
            File.WriteAllText(FoundationValidation.ResultsDirectory + "/Build.summary.txt",
                $"Profile: {ProfilePath}\nResult: {report.summary.result}\nTarget: {report.summary.platform}\n" +
                $"Options: {report.summary.options}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\n" +
                $"Size: {report.summary.totalSize} bytes\nDuration: {report.summary.totalTime}\nOutput: {report.summary.outputPath}\n");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows Development build failed. See {FoundationValidation.ResultsDirectory}/Build.summary.txt.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void RequireIdleEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for an idle Editor in Edit Mode.");
        }
    }
}
