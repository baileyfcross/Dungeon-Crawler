using System;
using System.IO;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SpaceCrawler.Editor
{
    /// <summary>Authors only static graph data and the existing session's configuration reference.</summary>
    public static class RunGenerationAssetTools
    {
        public const string GenerationPath = "Assets/_Project/Data/Runs/DefaultRun.asset";
        public const string RoomsPath = "Assets/_Project/Data/Rooms";

        [MenuItem("Tools/Space Crawler/Run Generation/Create Generation Definitions")]
        public static void CreateDefinitions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Author generation definitions only in idle Edit Mode.");
            EnsureFolder(RoomsPath);
            EnsureFolder("Assets/_Project/Data/Runs");
            var categories = new[] { RoomCategory.Starting, RoomCategory.Engine, RoomCategory.Cockpit, RoomCategory.Storage, RoomCategory.Generator, RoomCategory.Lab };
            var definitions = new RoomDefinition[categories.Length];
            for (var i = 0; i < categories.Length; i++)
            {
                var path = RoomsPath + "/" + categories[i] + ".asset";
                definitions[i] = AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
                if (definitions[i] != null) continue;
                var definition = ScriptableObject.CreateInstance<RoomDefinition>();
                var data = new SerializedObject(definition);
                data.FindProperty("stableId").stringValue = "room." + categories[i].ToString().ToLowerInvariant();
                data.FindProperty("category").enumValueIndex = (int)categories[i];
                data.FindProperty("generationWeight").intValue = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                definition.CreateConfiguration();
                AssetDatabase.CreateAsset(definition, path);
                definitions[i] = definition;
            }
            var generation = AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>(GenerationPath);
            if (generation == null)
            {
                generation = ScriptableObject.CreateInstance<RunGenerationDefinition>();
                var data = new SerializedObject(generation);
                var rooms = data.FindProperty("roomDefinitions");
                rooms.arraySize = definitions.Length;
                for (var i = 0; i < definitions.Length; i++) rooms.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                generation.CreateConfiguration();
                AssetDatabase.CreateAsset(generation, GenerationPath);
            }
            generation.CreateConfiguration();
            var contents = PrefabUtility.LoadPrefabContents(CombatAssetTools.FixturePath);
            try
            {
                var data = new SerializedObject(contents.GetComponent<GameplaySession>());
                var reference = data.FindProperty("generation");
                if (reference.objectReferenceValue == null)
                {
                    reference.objectReferenceValue = generation;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(contents, CombatAssetTools.FixturePath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); AssetDatabase.SaveAssets(); }
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
