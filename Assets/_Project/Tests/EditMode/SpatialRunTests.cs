using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using System.Numerics;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class SpatialRunTests
    {
        private static RunGenerationConfiguration Catalog() => new RunGenerationConfiguration(new RunGenerationSettings(10,25,0),
            Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c => new RoomConfiguration("room."+c,c)));
        [TestCase(0UL,"519ef8f4e6cc9726f983dd1edbd11ba53c193a8ef0d74c8113811bd79470ac4e;ef762d962c272397dc737d883612ba2901952bf3197bc047ea5880b7c57adde8;3477980e7b3464054602addda69a7f34135176b426a2f1d73ca795962e20e076")]
        [TestCase(42UL,"93cc3e219a60c5f7f27dfe05edbe209c903fd661bd8c18cc54371a529d6c692e;3cc9072f02f1662d735966dfcc73be4fdca086b34da64c97e0f39f658e669436;ab5ba7a0bde9b1157c3135746ee6249b4a48e493e9e80307db00d59726d68e50")]
        [TestCase(ulong.MaxValue,"5ebbe79543f4270ebea37c2365286193d43a47264e3618d5d46e2a8186dc35e3;7f4f6f3c4aee6016c790aebfbc0e5333708149496f53922fbe6fa06e226c0ea8;bd6ad62b5365f2f36dd046e7c01131950bfe507091e380054cb9b36dd046153e")]
        public void ReleasedSpatialFingerprintsRemainFrozen(ulong seed,string expected)
        { Assert.That(new RunSequence(seed,Catalog()).Levels.Select(GraphFingerprint.Compute),Is.EqualTo(expected.Split(';'))); }
        [Test]
        public void SeededSpatialLevelsAreConnectedReciprocalNonoverlappingAndHaveFourUniquePorts()
        {
            var totals = new System.Collections.Generic.HashSet<int>(); int loops = 0;
            for (ulong seed = 0; seed < 1000; seed++)
            {
                var run = new RunSequence(seed,Catalog());
                foreach (var graph in run.Levels)
                {
                    totals.Add(graph.Nodes.Count); Assert.That(graph.Nodes.Count,Is.InRange(10,25));
                    Assert.That(graph.GenerationVersion,Is.EqualTo(2));
                    Assert.That(GraphFingerprint.Compute(graph),Is.EqualTo(GraphFingerprint.Compute(new RunGenerator().Generate(graph.Seed,graph.Configuration,2))));
                    if (graph.Connections.Count > (graph.Nodes.Count-1)*2) loops++;
                    foreach (var node in graph.Nodes)
                    {
                        var edges = graph.Connections.Where(e => e.From.Equals(node.Id)).ToArray();
                        Assert.That(edges.Length,Is.InRange(1,4)); Assert.That(edges.Select(e=>e.FromPort).Distinct().Count(),Is.EqualTo(edges.Length));
                        Assert.That(graph.Route(graph.StartRoomId,node.Id).Count,Is.LessThan(graph.Nodes.Count));
                        foreach (var e in edges) Assert.That(graph.Connections.Any(r => r.From.Equals(e.To) && r.To.Equals(e.From) && r.FromPort == e.ToPort && r.ToPort == e.FromPort),Is.True);
                        foreach (var other in graph.Nodes.Where(n => n.Id.Value > node.Id.Value))
                            Assert.That(Math.Abs(other.LayoutX-node.LayoutX) >= 28 || Math.Abs(other.LayoutY-node.LayoutY) >= 20,Is.True);
                    }
                }
            }
            Assert.That(totals.OrderBy(n=>n),Is.EqualTo(Enumerable.Range(10,16))); Assert.That(loops,Is.GreaterThan(100));
        }
        [Test]
        public void BacktrackingAndFinalRoomTravelRoundTripWithSavedWorld()
        {
            var run = new RunSequence(42,Catalog()); var player = new PlayerIdentity(1);
            var progress = new RunProgression(run.CurrentGraph,player); progress.SetGameplayActive(true); progress.TryCompleteEntry(run.CurrentGraph.StartRoomId);
            var next = progress.Choices[0].To;
            Step(progress,next); Step(progress,run.CurrentGraph.StartRoomId); Step(progress,next);
            var pose = new SavedPlayerPose(new Vector2(2,3),Vector2.UnitY,.25f);
            var enemy = new SavedEnemy(1,0,100,new Vector2(1,7),Vector2.UnitX);
            var world = new RunWorldSnapshot(new[] {pose},new[] { new SavedRoomState(1,next,new[] {enemy}) });
            var saved = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(RunCheckpoint.Capture(run,progress,SavedSessionMode.Solo,new[] {"weapon.knife"},world:world)));
            var restored = RunSequence.Restore(saved,Catalog());
            Assert.That(saved.SequenceVersion,Is.EqualTo(2)); Assert.That(restored.CurrentGraph.GenerationVersion,Is.EqualTo(2));
            Assert.That(saved.RoomVisits,Is.EqualTo(progress.VisitedRooms)); Assert.That(saved.World.Players[0].Position,Is.EqualTo(pose.Position));
            Assert.That(saved.World.Players[0].Cooldown,Is.EqualTo(.25f)); Assert.That(saved.World.Rooms[0].Enemies[0].Health,Is.Zero);
            foreach (var edge in run.CurrentGraph.Route(next,run.CurrentGraph.TerminalRoomId)) Step(progress,edge.To);
            Assert.That(progress.State,Is.EqualTo(RoomFlowState.Terminal)); Step(progress,progress.Choices[0].To);
        }
        [Test]
        public void SchemaThreeLegacyGraphRestoresAndArchivesOriginalBytes()
        {
            var run = new RunSequence(42,Catalog(),1); var progress = new RunProgression(run.CurrentGraph,new PlayerIdentity(1));
            progress.TryCompleteEntry(run.CurrentGraph.StartRoomId);
            var current = RunCheckpointCodec.Encode(RunCheckpoint.Capture(run,progress,SavedSessionMode.Solo,new[] {"weapon.knife"}));
            // Strip the absent-world byte and the difficulty/partner route and encounter-policy extensions.
            int size = BitConverter.ToInt32(current,8)-21; var payload = new byte[size]; Array.Copy(current,12,payload,0,size);
            byte[] legacy;
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            { writer.Write(0x53435253); writer.Write(3); writer.Write(size); writer.Write(payload); writer.Write(sha.ComputeHash(payload)); legacy = stream.ToArray(); }
            var restored = RunCheckpointCodec.Decode(legacy); Assert.That(restored.World,Is.Null); Assert.That(restored.SequenceVersion,Is.EqualTo(1));
            Assert.That(GraphFingerprint.Compute(RunSequence.Restore(restored,Catalog()).CurrentGraph),Is.EqualTo(restored.Fingerprint));
            var folder = System.IO.Path.Combine("Logs","Validation","Saves",Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder,"run.sav"); System.IO.File.WriteAllBytes(path,legacy);
            new RunCheckpointStore(path).Save(restored); Assert.That(System.IO.File.ReadAllBytes(path+".schema3"),Is.EqualTo(legacy));
        }
        private static void Step(RunProgression progress,RunRoomId destination)
        { Assert.That(progress.TryRequestExit(new ExitIntent(progress.Player,progress.CurrentRoomId,destination)),Is.True); Assert.That(progress.TryBeginAcceptedEntry() && progress.TryCompleteEntry(destination),Is.True); }
        [Test]
        public void InvalidPersistentDataAndInterruptedAttacksAreHandledExplicitly()
        {
            Assert.Throws<ArgumentException>(() => new SavedPlayerPose(new Vector2(float.NaN,0),Vector2.UnitX,0));
            Assert.Throws<ArgumentException>(() => new SavedEnemy(1,101,100,Vector2.Zero,Vector2.UnitX));
            var enemy = new EnemySimulation(new EnemyIdentity(1,1),100); enemy.RestoreHealth(60,100);
            var brain = new MeleeEnemyBehaviour(enemy,new MeleeEnemyConfiguration(),Vector2.Zero); brain.Restore(Vector2.UnitX);
            var observations = new[] {new EnemyObservation(new PlayerIdentity(1),Vector2.UnitX,true,true,true)};
            brain.Advance(.79f,Vector2.Zero,observations,true);
            Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Recovery)); Assert.That(brain.StrikeTarget,Is.Null);
            brain.Advance(.02f,Vector2.Zero,observations,true); brain.Advance(.01f,Vector2.Zero,observations,true);
            Assert.That(brain.State,Is.EqualTo(EnemyBehaviourState.Windup)); Assert.That(enemy.Health.Current,Is.EqualTo(60));
        }
    }
}
