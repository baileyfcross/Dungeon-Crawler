using System;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    public static class RoomContentTools
    {
        private const string FixturePath = "Assets/_Project/Prefabs/Combat/CombatTargetFixture.prefab";
        private const string TargetPath = "Assets/_Project/Prefabs/Combat/DamageableTestTarget.prefab";
        [MenuItem("Tools/Space Crawler/Rooms/Separate Combat Fixtures")]
        public static void SeparateCombatFixtures()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Room content authoring requires an idle Editor.");
            if (AssetDatabase.LoadAssetAtPath<CombatTargetFixture>(FixturePath) == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Starting.prefab");
                ValidateLegacyTargets(source);
                var root = new GameObject("CombatTargetFixture");
                try
                {
                    var targets = new DamageableTarget[source.Targets.Count];
                    for (var i = 0; i < targets.Length; i++)
                    {
                        targets[i] = Object.Instantiate(source.Targets[i], root.transform);
                        targets[i].transform.localPosition = source.Targets[i].transform.localPosition;
                    }
                    var fixture = root.AddComponent<CombatTargetFixture>(); var data = new SerializedObject(fixture);
                    var list = data.FindProperty("targets"); list.arraySize = targets.Length;
                    for (var i = 0; i < targets.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
                    data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, FixturePath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            // Validate every candidate before modifying any production room.
            foreach (RoomCategory category in Enum.GetValues(typeof(RoomCategory)))
            {
                var room = AssetDatabase.LoadAssetAtPath<AuthoredRoom>($"Assets/_Project/Prefabs/Rooms/{category}.prefab");
                if (room.Targets.Count != 0) ValidateLegacyTargets(room);
            }
            foreach (RoomCategory category in Enum.GetValues(typeof(RoomCategory)))
            {
                var path = $"Assets/_Project/Prefabs/Rooms/{category}.prefab"; var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var room = root.GetComponent<AuthoredRoom>(); var targets = new DamageableTarget[room.Targets.Count];
                    for (var i = 0; i < targets.Length; i++) targets[i] = room.Targets[i];
                    var data = new SerializedObject(room); data.FindProperty("targets").arraySize = 0; data.ApplyModifiedPropertiesWithoutUndo();
                    foreach (var target in targets) Object.DestroyImmediate(target.gameObject);
                    room.ValidateAuthoring(); room.ValidateMultiplayerAuthoring(); PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var path in new[] { "Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab", "Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var loader = root.GetComponent<RoomLoader>(); var data = new SerializedObject(loader);
                    data.FindProperty("combatFixturePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatTargetFixture>(FixturePath);
                    data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        private static void ValidateLegacyTargets(AuthoredRoom room)
        {
            if (room == null || room.Targets.Count != 3) throw new InvalidOperationException("Expected exactly the three legacy combat targets; preserve other content.");
            foreach (var target in room.Targets)
                if (target == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target.gameObject) != TargetPath)
                    throw new InvalidOperationException("Refusing to remove non-fixture room content.");
        }
    }
}
