using System;
using System.Collections.Generic;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SpaceCrawler.Editor
{
    public static class EncounterAuthoringTools
    {
        [MenuItem("Tools/Space Crawler/Rooms/Configure Difficulty Spawns")]
        public static void ConfigureDifficultySpawns()
        {
            var enemy=AssetDatabase.LoadAssetAtPath<DamageableTarget>(EnemyAssetTools.PrefabPath);
            var behaviour=AssetDatabase.LoadAssetAtPath<MeleeEnemyDefinition>(EnemyAssetTools.DefinitionPath);
            if(enemy==null || behaviour==null)throw new InvalidOperationException("Existing enemy assets are required.");
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Project/Prefabs/Rooms"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var contents=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var room=contents.GetComponent<AuthoredRoom>();if(room==null)continue;
                    var data=new SerializedObject(room);var slots=data.FindProperty("difficultySpawns");
                    if(slots.arraySize!=0 && slots.arraySize!=5)throw new InvalidOperationException("Unexpected encounter slot authoring: "+path);
                    Physics2D.SyncTransforms();
                    var points=new List<Vector2>();
                    // Resolve geometry once during authoring; runtime only chooses these explicit safe slots.
                    for(float y=3;y<=11 && points.Count<5;y+=2)
                        for(float x=-8;x<=8 && points.Count<5;x+=2)
                        {
                            Vector2 p=room.transform.TransformPoint(new Vector2(x,y));
                            if(!room.IsSafePosition(p) || !room.IsClearOfArrivals(p))continue;
                            bool clear=true;
                            foreach(var door in room.CardinalExits)if(Vector2.Distance(p,door.transform.position)<3)clear=false;
                            foreach(var point in points)if(Vector2.Distance(p,point)<3)clear=false;
                            if(clear)points.Add(p);
                        }
                    if(points.Count!=5)throw new InvalidOperationException("Room cannot fit five safe encounter slots: "+path);
                    slots.arraySize=5;data.FindProperty("baseEnemyCount").intValue=3;
                    for(int i=0;i<5;i++)
                    {
                        var slot=slots.GetArrayElementAtIndex(i);
                        var anchor=slot.FindPropertyRelative("anchor").objectReferenceValue as Transform;
                        if(anchor==null){anchor=new GameObject("EnemySpawn"+(i+1)).transform;anchor.SetParent(room.ContentRoot,false);}
                        anchor.position=points[i];slot.FindPropertyRelative("identity").intValue=i+1;
                        slot.FindPropertyRelative("anchor").objectReferenceValue=anchor;slot.FindPropertyRelative("prefab").objectReferenceValue=enemy;
                        slot.FindPropertyRelative("behaviour").objectReferenceValue=behaviour;
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();room.ValidateAuthoring();room.ValidateMultiplayerAuthoring();
                    PrefabUtility.SaveAsPrefabAsset(contents,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(contents);}
            }
            AssetDatabase.SaveAssets();Debug.Log("[EncounterAuthoring] Difficulty spawn slots validated and saved.");
        }
    }
}
