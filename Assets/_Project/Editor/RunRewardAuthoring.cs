using System;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
namespace SpaceCrawler.Editor
{
    public static class RunRewardAuthoring
    {
        public static void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop play before editing rewards.");
            if(!AssetDatabase.IsValidFolder("Assets/_Project/Data/Rewards"))AssetDatabase.CreateFolder("Assets/_Project/Data","Rewards");
            string path="Assets/_Project/Data/Rewards/RunRewards.asset";
            var definition=AssetDatabase.LoadAssetAtPath<RunRewardDefinition>(path);
            if(definition==null){definition=ScriptableObject.CreateInstance<RunRewardDefinition>();AssetDatabase.CreateAsset(definition,path);}
            definition.CreatePolicy();
            foreach(string name in new[]{"RoomGameplaySession","MultiplayerSession"})
            {
                string prefab="Assets/_Project/Prefabs/Gameplay/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(prefab);
                try
                {
                    Component session=root.GetComponent<GameplaySession>();if(session==null)session=root.GetComponent<MultiplayerSession>();
                    if(session==null)throw new InvalidOperationException("Missing session composition.");
                    var data=new SerializedObject(session);data.FindProperty("rewardDefinition").objectReferenceValue=definition;data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root,prefab);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("[Rewards] Run reward data and session references configured.");
        }
    }
}
