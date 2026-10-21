using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceCrawler.Editor
{
    /// <summary>Read-only validation of authored object references and runtime composition assets.</summary>
    public static class ProjectReferenceValidation
    {
        [MenuItem("Tools/Space Crawler/Validation/Check Asset References")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Reference validation requires an idle Editor.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save modified scenes before validating references.");
            var failures = new List<string>();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var prefabs = 0;
            var scenes = 0;
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try { CheckHierarchy(root, path, failures); prefabs++; }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects()) CheckHierarchy(root, path, failures);
                    scenes++;
                }
                foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Project" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is ScriptableObject) CheckObject(asset, path, failures);
                }
            }
            finally
            {
                if (Array.Exists(setup, scene => scene.isLoaded && scene.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Directory.CreateDirectory("Logs/Validation");
            File.WriteAllText("Logs/Validation/AssetReferences.txt",
                $"Prefabs: {prefabs}\nScenes: {scenes}\nFailures: {failures.Count}\n" + string.Join("\n", failures));
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log($"Asset references valid: {prefabs} prefabs, {scenes} scenes.");
        }

        private static void CheckHierarchy(GameObject root, string path, List<string> failures)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    failures.Add($"{path}: missing script on {transform.name}");
                foreach (var component in transform.GetComponents<Component>())
                    if (component != null) CheckObject(component, path, failures);
            }
        }

        private static void CheckObject(UnityEngine.Object target, string path, List<string> failures)
        {
            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        failures.Add($"{path}: missing reference {target.name}.{property.propertyPath}");
            }
        }
    }
}
