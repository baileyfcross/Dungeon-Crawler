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
    public sealed class RoomDefinitionTests
    {
        private const string GenerationPath = "Assets/_Project/Data/Runs/DefaultRun.asset";

        [TestCase("Starting", "room.starting", RoomCategory.Starting)]
        [TestCase("Engine", "room.engine", RoomCategory.Engine)]
        [TestCase("Cockpit", "room.cockpit", RoomCategory.Cockpit)]
        [TestCase("Storage", "room.storage", RoomCategory.Storage)]
        [TestCase("Generator", "room.generator", RoomCategory.Generator)]
        [TestCase("Lab", "room.lab", RoomCategory.Lab)]
        public void AuthoredRoomHasStableIdentityAndOnlyStaticGenerationData(string name, string id, RoomCategory category)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RoomDefinition>($"Assets/_Project/Data/Rooms/{name}.asset");
            Assert.That(asset, Is.Not.Null);
            var before = EditorJsonUtility.ToJson(asset);
            var snapshot = asset.CreateConfiguration();
            Assert.That(snapshot.Id, Is.EqualTo(id));
            Assert.That(snapshot.Category, Is.EqualTo(category));
            Assert.That(snapshot.Weight, Is.EqualTo(1));
            var renamed = Object.Instantiate(asset);
            try
            {
                renamed.name = "An unrelated asset display name";
                Assert.That(renamed.CreateConfiguration().Id, Is.EqualTo(id));
            }
            finally { Object.DestroyImmediate(renamed); }
            var companion = category == RoomCategory.Starting
                ? new RoomConfiguration("test.regular", RoomCategory.Engine) : new RoomConfiguration("test.start", RoomCategory.Starting);
            new RunGenerator().Generate(42, new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3), new[] { snapshot, companion }));
            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
        }

        [Test]
        public void FullAuthoringSnapshotIsImmutableAndCanonicalAcrossGeneratedRuns()
        {
            var asset = AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>(GenerationPath);
            Assert.That(asset, Is.Not.Null);
            var before = EditorJsonUtility.ToJson(asset);
            var snapshot = asset.CreateConfiguration();
            Assert.That(snapshot.Definitions.Count, Is.EqualTo(6));
            Assert.That(snapshot.Settings.MinRouteRooms, Is.EqualTo(9));
            Assert.That(snapshot.Settings.MaxRouteRooms, Is.EqualTo(11));
            Assert.That(snapshot.Settings.MaxSplits, Is.EqualTo(3));
            Assert.That(snapshot.Definitions.Select(d => d.Id), Is.Ordered.Using<string>(StringComparer.Ordinal));
            for (ulong seed = 0; seed < 16; seed++)
            {
                var graph = new RunGenerator().Generate(seed, snapshot);
                Assert.That(graph.Configuration, Is.SameAs(snapshot));
                Assert.That(graph.GenerationVersion, Is.EqualTo(1));
            }
            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
        }

        [Test]
        public void MissingRoomReferencesAndInvalidAuthoringSettingsFailExplicitly()
        {
            var copy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>(GenerationPath));
            try
            {
                var data = new SerializedObject(copy);
                data.FindProperty("minRouteRooms").intValue = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<ArgumentOutOfRangeException>(() => copy.CreateConfiguration());
                data.FindProperty("minRouteRooms").intValue = 9;
                data.FindProperty("roomDefinitions").GetArrayElementAtIndex(0).objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<InvalidOperationException>(() => copy.CreateConfiguration());
                data.FindProperty("roomDefinitions").arraySize = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<ArgumentException>(() => copy.CreateConfiguration());
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void InvalidRoomAuthoringIsRejectedByTheSnapshotBoundary()
        {
            var asset = ScriptableObject.CreateInstance<RoomDefinition>();
            try
            {
                Assert.Throws<ArgumentException>(() => asset.CreateConfiguration());
                var data = new SerializedObject(asset);
                data.FindProperty("stableId").stringValue = "room.test";
                data.FindProperty("generationWeight").intValue = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<ArgumentOutOfRangeException>(() => asset.CreateConfiguration());
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
