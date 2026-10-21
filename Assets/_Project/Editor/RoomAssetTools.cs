using System;
using System.IO;
using System.Linq;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    /// <summary>Authors six reusable technical rooms and the persistent session through Unity APIs.</summary>
    public static class RoomAssetTools
    {
        public const string SessionPath = "Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab";
        public const string RoomsPath = "Assets/_Project/Prefabs/Rooms";

        [MenuItem("Tools/Space Crawler/Rooms/Create Authored Rooms")]
        public static void CreateRooms()
        {
            RequireIdle();
            EnsureFolder(RoomsPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/ControlsUnlit.mat");
            foreach (RoomCategory category in Enum.GetValues(typeof(RoomCategory)))
            {
                var path = RoomsPath + "/" + category + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<AuthoredRoom>(path) == null) CreateRoom(category, path, material);
                var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(RunGenerationAssetTools.RoomsPath + "/" + category + ".asset");
                SetReference(definition, "roomPrefab", AssetDatabase.LoadAssetAtPath<AuthoredRoom>(path));
            }
            if (AssetDatabase.LoadAssetAtPath<GameplaySession>(SessionPath) == null) CreateSession();
            var scene = EditorSceneManager.OpenScene(ApplicationAssetTools.BootPath, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().Select(o => o.GetComponent<ApplicationRoot>()).Single(r => r != null);
            SetReference(root, "gameplaySession", AssetDatabase.LoadAssetAtPath<GameplaySession>(SessionPath));
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Boot room composition.");
            AssetDatabase.SaveAssets();
        }

        private static void CreateRoom(RoomCategory category, string path, Material material)
        {
            var root = new GameObject(category + " Technical Room");
            try
            {
                var room = root.AddComponent<AuthoredRoom>();
                var floor = Sprite("Playable floor", root.transform, new Vector2(0, 6), new Vector2(24, 16), new Color(0.065f, 0.10f, 0.13f), -10, material);
                for (var x = -10; x <= 10; x += 2) Sprite("Floor grid", root.transform, new Vector2(x, 6), new Vector2(0.025f, 16), new Color(0.12f, 0.18f, 0.21f), -9, material);
                for (var y = 0; y <= 12; y += 2) Sprite("Floor grid", root.transform, new Vector2(0, y), new Vector2(24, 0.025f), new Color(0.12f, 0.18f, 0.21f), -9, material);
                var walls = new[] {
                    Wall(root.transform, new Vector2(-12.5f, 6), new Vector2(1, 18), material),
                    Wall(root.transform, new Vector2(12.5f, 6), new Vector2(1, 18), material),
                    Wall(root.transform, new Vector2(0, -2.5f), new Vector2(24, 1), material),
                    Wall(root.transform, new Vector2(0, 14.5f), new Vector2(24, 1), material) };
                var entry = Anchor("EntryAnchor", root.transform, new Vector2(0, -2));
                var spawn = Anchor("PlayerSpawnAnchor", root.transform, Vector2.zero);
                Sprite("Non-traversable entry marker", root.transform, new Vector2(0, -1.6f), new Vector2(2.4f, 0.25f), new Color(0.35f, 0.4f, 0.45f), -8, material);
                var single = Exit("SingleExit", root.transform, new Vector2(0, 13), new Color(0.25f, 0.8f, 0.7f), material);
                var left = Exit("LeftExit", root.transform, new Vector2(-6, 13), new Color(0.35f, 0.7f, 0.9f), material);
                var right = Exit("RightExit", root.transform, new Vector2(6, 13), new Color(0.9f, 0.7f, 0.3f), material);
                var content = Anchor("RoomContent", root.transform, Vector2.zero);
                SetReference(room, "floor", floor); SetReference(room, "entryAnchor", entry); SetReference(room, "playerSpawnAnchor", spawn);
                SetReference(room, "contentRoot", content); SetReference(room, "singleExit", single); SetReference(room, "leftExit", left); SetReference(room, "rightExit", right);
                SetArray(room, "staticCollision", walls); SetArray(room, "targets", Array.Empty<DamageableTarget>());
                room.ValidateAuthoring();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CreateSession()
        {
            var root = PrefabUtility.LoadPrefabContents(CombatAssetTools.FixturePath);
            try
            {
                var session = root.GetComponent<GameplaySession>();
                var combatData = new SerializedObject(session.Combat);
                var presentation = (CombatPresentation)combatData.FindProperty("presentation").objectReferenceValue;
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    if (child != session.Motor.transform && child != session.Follower.transform && child != presentation.transform)
                        Object.DestroyImmediate(child.gameObject);
                SetArray(session.Combat, "targets", Array.Empty<Object>());
                var container = Anchor("RoomContainer", root.transform, Vector2.zero);
                var loader = root.AddComponent<RoomLoader>();
                SetReference(loader, "roomContainer", container);
                SetReference(session, "rooms", loader);
                root.name = "Room Gameplay Session";
                PrefabUtility.SaveAsPrefabAsset(root, SessionPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/Space Crawler/Rooms/Use Seed 42")]
        public static void UseSeed42() => SetSeed(true, 42);
        [MenuItem("Tools/Space Crawler/Rooms/Use Seed 0")]
        public static void UseSeed0() => SetSeed(true, 0);
        [MenuItem("Tools/Space Crawler/Rooms/Use Random Seed")]
        public static void UseRandomSeed() => SetSeed(false, 42);
        private static void SetSeed(bool enabled, uint seed)
        {
            RequireIdle();
            var root = PrefabUtility.LoadPrefabContents(SessionPath);
            try
            {
                var data = new SerializedObject(root.GetComponent<GameplaySession>());
                data.FindProperty("useFixedSeed").boolValue = enabled;
                data.FindProperty("fixedSeed").longValue = seed;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, SessionPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); AssetDatabase.SaveAssets(); }
        }

        private static Transform Anchor(string name, Transform parent, Vector2 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false); child.localPosition = position;
            return child;
        }
        private static RoomExitTrigger Exit(string name, Transform parent, Vector2 position, Color color, Material material)
        {
            var anchor = Anchor(name, parent, position);
            var trigger = anchor.gameObject.AddComponent<BoxCollider2D>(); trigger.isTrigger = true; trigger.size = new Vector2(2.6f, 1);
            var exit = anchor.gameObject.AddComponent<RoomExitTrigger>(); SetReference(exit, "trigger", trigger);
            Sprite("Exit pad", anchor, Vector2.zero, trigger.size, color, -7, material);
            return exit;
        }
        private static Collider2D Wall(Transform parent, Vector2 position, Vector2 size, Material material)
        {
            var wall = Anchor("Static boundary", parent, position);
            var collider = wall.gameObject.AddComponent<BoxCollider2D>(); collider.size = size;
            Sprite("Wall primitive", wall, Vector2.zero, size, new Color(0.3f, 0.42f, 0.48f), -5, material);
            return collider;
        }
        private static SpriteRenderer Sprite(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order, Material material)
        {
            var sprite = AssetDatabase.LoadAllAssetsAtPath("Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png").OfType<Sprite>().First();
            var child = Anchor(name, parent, position);
            child.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            var renderer = child.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.color = color; renderer.sortingOrder = order;
            return renderer;
        }
        private static void SetReference(Object target, string property, Object value)
        {
            if (target == null || value == null) throw new InvalidOperationException("Missing authoring dependency: " + property);
            var data = new SerializedObject(target); data.FindProperty(property).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArray(Object target, string property, Object[] values)
        {
            var data = new SerializedObject(target); var array = data.FindProperty(property); array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Author rooms only in compiled idle Edit Mode.");
        }
    }
}
