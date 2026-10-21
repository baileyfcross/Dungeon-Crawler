using System;
using System.Linq;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    public static class MultiplayerAssetTools
    {
        public const string RigPath = "Assets/_Project/Prefabs/Gameplay/NetworkPlayerRig.prefab";
        public const string SessionPath = "Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab";

        [MenuItem("Tools/Space Crawler/Multiplayer/Create Multiplayer Assets")]
        public static void Create()
        {
            RequireIdle();
            foreach (RoomCategory category in Enum.GetValues(typeof(RoomCategory)))
            {
                var path = RoomAssetTools.RoomsPath + "/" + category + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var room = root.GetComponent<AuthoredRoom>();
                    if (room.PlayerOneSpawnAnchor == null) Set(room, "playerOneSpawnAnchor", Anchor(root.transform, "PlayerOneSpawnAnchor", new Vector2(-0.8f, 0.75f)));
                    if (room.PlayerTwoSpawnAnchor == null) Set(room, "playerTwoSpawnAnchor", Anchor(root.transform, "PlayerTwoSpawnAnchor", new Vector2(0.8f, 0.75f)));
                    room.ValidateMultiplayerAuthoring();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (AssetDatabase.LoadAssetAtPath<NetworkPlayerRig>(RigPath) == null) CreateRig();
            if (AssetDatabase.LoadAssetAtPath<MultiplayerSession>(SessionPath) == null) CreateSession();
            var scene = EditorSceneManager.OpenScene(ApplicationAssetTools.BootPath, OpenSceneMode.Single);
            var application = scene.GetRootGameObjects().Select(o => o.GetComponent<ApplicationRoot>()).Single(r => r != null);
            Set(application, "multiplayerSession", AssetDatabase.LoadAssetAtPath<MultiplayerSession>(SessionPath));
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save multiplayer Boot composition.");
            AssetDatabase.SaveAssets();
        }

        private static Transform Anchor(Transform parent, string name, Vector2 position)
        {
            var value = new GameObject(name).transform;
            value.SetParent(parent, false); value.localPosition = position; return value;
        }

        private static void CreateRig()
        {
            var root = PrefabUtility.LoadPrefabContents(RoomAssetTools.SessionPath);
            try
            {
                var original = root.GetComponent<GameplaySession>();
                var rig = root.AddComponent<NetworkPlayerRig>();
                Set(rig, "motor", original.Motor); Set(rig, "input", original.Input); Set(rig, "combat", original.Combat);
                Set(rig, "presentation", new SerializedObject(original).FindProperty("presentation").objectReferenceValue);
                Set(rig, "bodySprite", original.Motor.GetComponentsInChildren<SpriteRenderer>().Single(s => s.sortingOrder == 2));
                var data = new SerializedObject(rig);
                var colliders = original.Motor.GetComponents<Collider2D>();
                var array = data.FindProperty("playerColliders"); array.arraySize = colliders.Length;
                for (var i = 0; i < colliders.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(original.Follower.gameObject);
                Object.DestroyImmediate(original.Rooms.RoomContainer.gameObject);
                Object.DestroyImmediate(original.Rooms);
                Object.DestroyImmediate(original);
                root.name = "Network Player Rig";
                PrefabUtility.SaveAsPrefabAsset(root, RigPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateSession()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameplaySession>(RoomAssetTools.SessionPath);
            var root = new GameObject("Multiplayer Technical Session");
            try
            {
                var session = root.AddComponent<MultiplayerSession>();
                var manager = root.AddComponent<NetworkManager>();
                var transport = root.AddComponent<UnityTransport>();
                manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false,
                    AutoSpawnPlayerPrefabClientSide = false, ConnectionApproval = true, TickRate = 30 };
                var loader = root.AddComponent<RoomLoader>();
                Set(loader, "roomContainer", Anchor(root.transform, "RoomContainer", Vector2.zero));
                var camera = Object.Instantiate(source.Follower.gameObject, root.transform).GetComponent<PlayerCameraFollower>();
                Set(session, "network", manager); Set(session, "transport", transport); Set(session, "rooms", loader);
                Set(session, "follower", camera); Set(session, "playerPrefab", AssetDatabase.LoadAssetAtPath<NetworkPlayerRig>(RigPath));
                Set(session, "generation", new SerializedObject(source).FindProperty("generation").objectReferenceValue);
                Set(session, "knife", AssetDatabase.LoadAssetAtPath<WeaponDefinition>(CombatAssetTools.KnifePath));
                Set(session, "blaster", AssetDatabase.LoadAssetAtPath<WeaponDefinition>(CombatAssetTools.BlasterPath));
                PrefabUtility.SaveAsPrefabAsset(root, SessionPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Space Crawler/Multiplayer/Use Knife Default")]
        public static void Knife() => SetFlag("useBlaster", false);
        [MenuItem("Tools/Space Crawler/Multiplayer/Use Blaster Default")]
        public static void Blaster() => SetFlag("useBlaster", true);
        [MenuItem("Tools/Space Crawler/Multiplayer/Use Seed 42")]
        public static void Seed42() => SetFlag("useFixedSeed", true);
        [MenuItem("Tools/Space Crawler/Multiplayer/Use Random Seed")]
        public static void RandomSeed() => SetFlag("useFixedSeed", false);
        private static void SetFlag(string name, bool value)
        {
            RequireIdle();
            var root = PrefabUtility.LoadPrefabContents(SessionPath);
            try
            {
                var data = new SerializedObject(root.GetComponent<MultiplayerSession>());
                data.FindProperty(name).boolValue = value; data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, SessionPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); AssetDatabase.SaveAssets(); }
        }
        private static void Set(Object target, string property, Object reference)
        {
            var data = new SerializedObject(target); data.FindProperty(property).objectReferenceValue = reference;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Asset authoring requires a compiled, idle Editor.");
        }
    }
}
