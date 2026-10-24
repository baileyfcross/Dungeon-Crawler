using System;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
namespace SpaceCrawler.Editor
{
    public static class RangedEnemyAuthoring
    {
        public const string DefinitionPath="Assets/_Project/Data/Enemies/RangedEnemy.asset";
        public const string PrefabPath="Assets/_Project/Prefabs/Combat/RangedEnemy.prefab";
        [MenuItem("Tools/Space Crawler/Enemies/Configure Ranged Encounters")]
        public static void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop play before authoring.");
            var definition=AssetDatabase.LoadAssetAtPath<RangedEnemyDefinition>(DefinitionPath);
            if(definition==null){definition=ScriptableObject.CreateInstance<RangedEnemyDefinition>();AssetDatabase.CreateAsset(definition,DefinitionPath);}
            definition.CreateConfiguration();
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)==null)
            {
                var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyAssetTools.PrefabPath));
                try
                {
                    root.name="RangedEnemy";
                    var data=new SerializedObject(root.GetComponent<DamageableTarget>());data.FindProperty("initialHealth").intValue=80;data.ApplyModifiedPropertiesWithoutUndo();
                    // Tint the body only; health fill and background must remain distinct.
                    var presentation=new SerializedObject(root.GetComponentInChildren<TargetPresentation>());
                    var body=(SpriteRenderer)presentation.FindProperty("targetSprite").objectReferenceValue;
                    body.color=new Color(.65f,.25f,.85f);
                    PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Project/Prefabs/Rooms"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var room=root.GetComponent<AuthoredRoom>();if(room==null)continue;var data=new SerializedObject(room);
                    data.FindProperty("rangedEnemy").objectReferenceValue=AssetDatabase.LoadAssetAtPath<DamageableTarget>(PrefabPath);
                    data.FindProperty("rangedBehaviour").objectReferenceValue=definition;data.ApplyModifiedPropertiesWithoutUndo();
                    room.ValidateAuthoring();room.ValidateMultiplayerAuthoring();PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("[EnemyAuthoring] Ranged encounters configured.");
        }
    }
}
