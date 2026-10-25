#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RoomNavigationPerformanceTests
    {
        private readonly List<AuthoredRoom> rooms=new List<AuthoredRoom>();
        private readonly List<RoomNavigation> maps=new List<RoomNavigation>();
        [UnityTearDown]public IEnumerator TearDown(){for(int i=maps.Count-1;i>=0;i--)maps[i].Dispose();foreach(var room in rooms)if(room!=null)Object.Destroy(room.gameObject);maps.Clear();rooms.Clear();yield return null;}
        private AuthoredRoom Create(int index)
        {
            var room=Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Engine.prefab"));
            room.transform.position=Vector3.right*64*index;rooms.Add(room);return room;
        }
        [UnityTest]public IEnumerator BuildingAnotherRoomNeverRescansAnExistingRoomsObstacles()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");
            var first=Create(0);var wall=new GameObject("Cover").AddComponent<BoxCollider2D>();wall.transform.SetParent(first.ContentRoot);wall.transform.position=new Vector2(0,0);wall.size=new Vector2(1,40);
            var one=new RoomNavigation(first.transform);maps.Add(one);one.Build(first);
            Assert.That(one.Reachable(new Vector2(-3,0),new Vector2(3,0)),Is.False);
            var second=Create(1);var two=new RoomNavigation(second.transform,one);maps.Add(two);two.Build(second);
            Assert.That(one.Reachable(new Vector2(-3,0),new Vector2(3,0)),Is.False,"Scanning a new room must not erase static cover in the old room.");
            Assert.That(two.Reachable(new Vector2(61,0),new Vector2(67,0)),Is.True);
        }
        [UnityTest]public IEnumerator RecordRetainedRoomNavigationBuildCosts()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");
            var samples=new List<string>();
            for(int i=0;i<8;i++)
            {
                var room=Create(i);var map=new RoomNavigation(room.transform,i==0?null:maps[0]);maps.Add(map);
                long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();map.Build(room);watch.Stop();
                samples.Add($"{i+1},{watch.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},{GC.GetAllocatedBytesForCurrentThread()-before}");
                Assert.That(map.Reachable((Vector2)room.transform.position+Vector2.left*3,(Vector2)room.transform.position+Vector2.right*3),Is.True);
            }
            Directory.CreateDirectory("Logs/Validation/NavigationCosts");File.WriteAllLines("Logs/Validation/NavigationCosts/Build.csv",samples);
            TestContext.WriteLine("retainedGraphs,milliseconds,allocatedBytes\n"+string.Join("\n",samples));
        }
    }
}
#endif
