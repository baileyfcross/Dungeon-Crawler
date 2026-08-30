using System;
using System.IO;
using System.Linq;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    /// <summary>Authors only the primitive Phase 2 control fixture using Unity serialization APIs.</summary>
    public static class PlayerFoundationAssets
    {
        private const string Root = FoundationAssets.Root;
        private const string PrefabPath = Root + "/Prefabs/Gameplay/ControlsTestArea.prefab";
        private const string SpriteRoot = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/";

        [MenuItem("Tools/Space Crawler/Phase 2/Create Controls Fixture")]
        public static void CreateControlsFixture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("An idle Edit Mode Editor is required.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Prefabs/Gameplay");
            EnsureFolder(Root + "/Art/Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Art/Materials/ControlsUnlit.mat");
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) throw new InvalidOperationException("The existing URP 2D unlit sprite shader is required.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, Root + "/Art/Materials/ControlsUnlit.mat");
            }
            var physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(Root + "/Settings/PlayerMovement.physicsMaterial2D");
            if (physics == null)
            {
                physics = new PhysicsMaterial2D("Player movement") { friction = 0, bounciness = 0 };
                AssetDatabase.CreateAsset(physics, Root + "/Settings/PlayerMovement.physicsMaterial2D");
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreatePrefab(material, physics);

            var gameplayScene = EditorSceneManager.OpenScene(FoundationAssets.GameplayPath);
            foreach (var sceneRoot in gameplayScene.GetRootGameObjects())
                if (sceneRoot.name == "Main Camera" && sceneRoot.TryGetComponent<Camera>(out _)) Object.DestroyImmediate(sceneRoot);
            if (!EditorSceneManager.SaveScene(gameplayScene)) throw new IOException("Could not save Gameplay.");

            var boot = EditorSceneManager.OpenScene(FoundationAssets.BootPath);
            var root = boot.GetRootGameObjects().Select(go => go.GetComponent<ApplicationRoot>()).Single(component => component != null);
            SetReference(root, "gameplaySession", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<GameplaySession>());
            if (!EditorSceneManager.SaveScene(boot)) throw new IOException("Could not save Boot wiring.");
            AssetDatabase.SaveAssets();
            FoundationAssets.OpenBoot();
        }

        private static void CreatePrefab(Material material, PhysicsMaterial2D physics)
        {
            var temporary = new GameObject("Controls Test Area");
            try
            {
                var square = LoadSprite("Square");
                var circle = LoadSprite("Circle");
                var triangle = LoadSprite("Triangle");
                AddSprite("Floor", temporary.transform, square, Vector2.zero, new Vector2(24, 16), new Color(0.07f, 0.12f, 0.17f), -10, material);
                for (var x = -10; x <= 10; x += 2)
                    AddSprite("Grid X " + x, temporary.transform, square, new Vector2(x, 0), new Vector2(0.025f, 16), new Color(0.12f, 0.19f, 0.24f), -9, material);
                for (var y = -6; y <= 6; y += 2)
                    AddSprite("Grid Y " + y, temporary.transform, square, new Vector2(0, y), new Vector2(24, 0.025f), new Color(0.12f, 0.19f, 0.24f), -9, material);
                AddWall("North boundary", new Vector2(0, 8.25f), new Vector2(25, 0.5f));
                AddWall("South boundary", new Vector2(0, -8.25f), new Vector2(25, 0.5f));
                AddWall("East boundary", new Vector2(12.25f, 0), new Vector2(0.5f, 16));
                AddWall("West boundary", new Vector2(-12.25f, 0), new Vector2(0.5f, 16));

                var player = new GameObject("Local Player");
                player.transform.SetParent(temporary.transform, false);
                var body = player.AddComponent<Rigidbody2D>();
                body.gravityScale = 0;
                body.linearDamping = 0;
                body.angularDamping = 0;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.sharedMaterial = physics;
                player.AddComponent<CircleCollider2D>().radius = 0.35f;
                AddSprite("Player primitive", player.transform, circle, Vector2.zero, Vector2.one * 0.7f, new Color(0.36f, 0.88f, 0.85f), 2, material);
                var indicator = new GameObject("Aim indicator").transform;
                indicator.SetParent(player.transform, false);
                AddSprite("Aim line", indicator, square, new Vector2(0.75f, 0), new Vector2(0.7f, 0.07f), new Color(1, 0.76f, 0.42f), 3, material);
                var tip = AddSprite("Aim tip", indicator, triangle, new Vector2(1.15f, 0), new Vector2(0.22f, 0.24f), new Color(1, 0.76f, 0.42f), 3, material);
                tip.transform.localRotation = Quaternion.Euler(0, 0, -90);

                var motor = player.AddComponent<PlayerMotor>();
                SetReference(motor, "body", body);
                var input = player.AddComponent<PlayerInputAdapter>();
                SetReference(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
                var presentation = player.AddComponent<PlayerPresentation>();
                SetReference(presentation, "aimIndicator", indicator);
                var cameraObject = new GameObject("Gameplay Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
                cameraObject.transform.SetParent(temporary.transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 0, -10);
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.04f, 0.06f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100;
                var follower = cameraObject.AddComponent<PlayerCameraFollower>();
                SetReference(follower, "gameplayCamera", camera);
                var session = temporary.AddComponent<GameplaySession>();
                SetReference(session, "motor", motor);
                SetReference(session, "input", input);
                SetReference(session, "presentation", presentation);
                SetReference(session, "follower", follower);
                PrefabUtility.SaveAsPrefabAsset(temporary, PrefabPath);

                void AddWall(string name, Vector2 position, Vector2 size)
                {
                    // Collider uses world-sized geometry on an unscaled parent.
                    var wall = new GameObject(name);
                    wall.transform.SetParent(temporary.transform, false);
                    wall.transform.localPosition = position;
                    var collider = wall.AddComponent<BoxCollider2D>();
                    collider.size = size;
                    collider.sharedMaterial = physics;
                    AddSprite("Boundary primitive", wall.transform, square, Vector2.zero, size, new Color(0.43f, 0.36f, 0.26f), 0, material);
                }
            }
            finally { Object.DestroyImmediate(temporary); }
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAllAssetsAtPath(SpriteRoot + name + ".png").OfType<Sprite>().First();
        private static GameObject AddSprite(string name, Transform parent, Sprite sprite, Vector2 position, Vector2 size, Color color, int order, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = color;
            renderer.sortingOrder = order;
            return child;
        }
        private static void SetReference(Object target, string name, Object value)
        {
            if (value == null) throw new InvalidOperationException("Missing reference: " + name);
            var data = new SerializedObject(target);
            data.FindProperty(name).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
