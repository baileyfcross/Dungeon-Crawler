using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class AuthoredRoomTests
    {
        [TestCase(RoomCategory.Starting)]
        [TestCase(RoomCategory.Engine)]
        [TestCase(RoomCategory.Cockpit)]
        [TestCase(RoomCategory.Storage)]
        [TestCase(RoomCategory.Generator)]
        [TestCase(RoomCategory.Lab)]
        public void SixDefinitionsMapToValidReusableTechnicalRoomsWithoutChangingSnapshots(RoomCategory category)
        {
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>($"Assets/_Project/Data/Rooms/{category}.asset");
            var snapshot = definition.CreateConfiguration();
            var room = definition.RoomPrefab;
            Assert.That(room.Targets, Is.Empty, "Stationary targets belong only in explicitly enabled combat fixtures.");
            Assert.That(room.GetComponentsInChildren<DamageableTarget>(true), Is.Empty, "No inactive fixture remnants in playable prefabs.");
            Assert.That(room, Is.SameAs(AssetDatabase.LoadAssetAtPath<AuthoredRoom>($"Assets/_Project/Prefabs/Rooms/{category}.prefab")));
            Assert.DoesNotThrow(room.ValidateAuthoring);
            Assert.That(room.PlayableBounds.size, Is.EqualTo(new Vector2(24, 16)));
            Assert.That(room.EntryAnchor.localPosition, Is.EqualTo(new Vector3(0, -2, 0)));
            Assert.That(room.PlayerSpawnAnchor.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(room.SingleExit.transform.localPosition, Is.EqualTo(new Vector3(0, 13, 0)));
            Assert.That(room.LeftExit.transform.localPosition.x, Is.LessThan(room.RightExit.transform.localPosition.x));
            Assert.That(snapshot.Category, Is.EqualTo(category));
            Assert.That(snapshot.Id, Is.EqualTo("room." + category.ToString().ToLowerInvariant()));
            Assert.That(snapshot.Weight, Is.EqualTo(1));
            Assert.That(typeof(RoomConfiguration).GetProperties().Any(p => typeof(Object).IsAssignableFrom(p.PropertyType)), Is.False);
        }

        [TestCase("entryAnchor")]
        [TestCase("playerSpawnAnchor")]
        [TestCase("singleExit")]
        [TestCase("leftExit")]
        [TestCase("rightExit")]
        [TestCase("contentRoot")]
        [TestCase("floor")]
        public void MissingExplicitReferenceFailsInsteadOfSearchingOrChoosingAnotherAnchor(string field)
        {
            var room = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Starting.prefab"));
            try
            {
                var data = new SerializedObject(room); data.FindProperty(field).objectReferenceValue = null; data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
            }
            finally { Object.DestroyImmediate(room.gameObject); }
        }

        [Test]
        public void EnemySpawnAuthoringValidatesBoundsIdentityAndPrefabWithoutChangingExistingRoomAssets()
        {
            var room = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Starting.prefab"));
            try
            {
                var anchor = new GameObject("EnemySpawn").transform; anchor.SetParent(room.ContentRoot); anchor.position = new Vector3(0, 5, 0);
                var data = new SerializedObject(room); var spawns = data.FindProperty("enemySpawns"); spawns.arraySize = 1;
                var slot = spawns.GetArrayElementAtIndex(0);
                slot.FindPropertyRelative("identity").intValue = 1;
                slot.FindPropertyRelative("anchor").objectReferenceValue = anchor;
                slot.FindPropertyRelative("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DamageableTarget>("Assets/_Project/Prefabs/Combat/DamageableTestTarget.prefab");
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.DoesNotThrow(room.ValidateAuthoring);
                anchor.position = new Vector3(100, 100, 0);
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
                anchor.position = new Vector3(0, 5, 0);
                data.Update(); spawns.arraySize = 2; data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
            }
            finally { Object.DestroyImmediate(room.gameObject); }
        }

        [Test]
        public void SharedEntrySpawnReverseExitAndNonTriggerExitAreInvalid()
        {
            var room = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AuthoredRoom>("Assets/_Project/Prefabs/Rooms/Starting.prefab"));
            try
            {
                room.PlayerSpawnAnchor.position = room.EntryAnchor.position;
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
                room.PlayerSpawnAnchor.localPosition = Vector3.zero;
                room.SingleExit.transform.localPosition = new Vector3(0, -1, 0);
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
                room.SingleExit.transform.localPosition = new Vector3(0, 13, 0);
                room.SingleExit.Trigger.isTrigger = false;
                Assert.Throws<InvalidOperationException>(room.ValidateAuthoring);
            }
            finally { Object.DestroyImmediate(room.gameObject); }
        }
    }
}
