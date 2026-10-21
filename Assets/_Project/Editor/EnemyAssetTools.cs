using System;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Editor
{
    public static class EnemyAssetTools
    {
        public const string DefinitionPath = "Assets/_Project/Data/Enemies/MeleeEnemy.asset";
        public const string PrefabPath = "Assets/_Project/Prefabs/Combat/MeleeEnemy.prefab";
        [MenuItem("Tools/Space Crawler/Enemies/Create Melee Encounter")]
        public static void CreateMeleeEncounter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Enemy authoring requires an idle Editor.");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Enemies")) AssetDatabase.CreateFolder("Assets/_Project/Data", "Enemies");
            var definition = AssetDatabase.LoadAssetAtPath<MeleeEnemyDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MeleeEnemyDefinition>();
                definition.CreateConfiguration(); AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Combat/DamageableTestTarget.prefab");
                var root = Object.Instantiate(original);
                try
                {
                    root.name = "MeleeEnemy";
                    var indicator = new GameObject("AttackIndicator"); indicator.transform.SetParent(root.transform, false);
                    var line = indicator.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.widthMultiplier = .045f;
                    line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/ControlsUnlit.mat");
                    line.sortingOrder = 6; line.positionCount = 0;
                    var view = root.AddComponent<MeleeEnemyPresentation>();
                    var data = new SerializedObject(view); data.FindProperty("indicator").objectReferenceValue = line; data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            var path = "Assets/_Project/Prefabs/Rooms/Engine.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room = contents.GetComponent<AuthoredRoom>(); var data = new SerializedObject(room);
                var slots = data.FindProperty("enemySpawns");
                if (slots.arraySize != 0) throw new InvalidOperationException("Preserve existing encounter authoring; do not overwrite spawn slots.");
                var anchor = new GameObject("MeleeEnemySpawn").transform; anchor.SetParent(room.ContentRoot, false); anchor.localPosition = new Vector3(0, 6, 0);
                slots.arraySize = 1; var slot = slots.GetArrayElementAtIndex(0);
                slot.FindPropertyRelative("identity").intValue = 1;
                slot.FindPropertyRelative("anchor").objectReferenceValue = anchor;
                slot.FindPropertyRelative("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageableTarget>(PrefabPath);
                slot.FindPropertyRelative("behaviour").objectReferenceValue = definition;
                data.ApplyModifiedPropertiesWithoutUndo(); room.ValidateAuthoring(); room.ValidateMultiplayerAuthoring();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }
    }
}
