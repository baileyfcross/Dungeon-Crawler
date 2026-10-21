using System;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class CheckpointProgressionTests
    {
        private static RunGenerationConfiguration Catalog() => new RunGenerationConfiguration(new RunGenerationSettings(9, 11, 3),
            Enum.GetValues(typeof(RoomCategory)).Cast<RoomCategory>().Select(c => new RoomConfiguration("room." + c, c)));
        private static void Step(RunProgression progress)
        {
            var destination = progress.Choices[0].To;
            Assert.That(progress.TryRequestExit(new ExitIntent(progress.Player, progress.CurrentRoomId, destination)), Is.True);
            Assert.That(progress.TryBeginAcceptedEntry() && progress.TryCompleteEntry(destination), Is.True);
        }
        private static RunCheckpoint Snapshot()
        {
            var run = new RunSequence(42, Catalog());
            var progress = new RunProgression(run.CurrentGraph, new PlayerIdentity(1)); progress.SetGameplayActive(true);
            progress.TryCompleteEntry(run.CurrentGraph.StartRoomId);
            while (!progress.IsTerminal) Step(progress);
            Assert.That(run.TryAdvance(progress), Is.True);
            progress = new RunProgression(run.CurrentGraph, new PlayerIdentity(1)); progress.SetGameplayActive(true);
            progress.TryCompleteEntry(run.CurrentGraph.StartRoomId); Step(progress);
            return RunCheckpoint.Capture(run, progress, SavedSessionMode.Cooperative, new[] { "weapon.blaster", "weapon.blaster" });
        }
        [Test]
        public void LaterLevelCheckpointReconstructsAllGraphsAndExactHistoryOnce()
        {
            var saved = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(Snapshot()));
            var restored = RunSequence.Restore(saved, Catalog());
            Assert.That(restored.LevelNumber, Is.EqualTo(2));
            Assert.That(restored.Seed, Is.EqualTo(42));
            Assert.That(restored.CompletedRoutes.Count, Is.EqualTo(1));
            Assert.That(restored.Levels.Select(GraphFingerprint.Compute), Is.EqualTo(saved.LevelFingerprints));
            var progress = RunProgression.Restore(restored.CurrentGraph, new PlayerIdentity(1), saved.RoomVisits);
            Assert.That(progress.State, Is.EqualTo(RoomFlowState.EnteringRoom));
            Assert.That(progress.TryCompleteEntry(saved.CurrentRoom), Is.True);
            Assert.That(progress.RoomsEntered, Is.EqualTo(saved.RoomVisits.Count));
            Assert.That(progress.VisitedRooms, Is.EqualTo(saved.RoomVisits));
            Assert.That(progress.TryCompleteEntry(saved.CurrentRoom), Is.False);
            Assert.That(saved.EquipmentIds, Is.EqualTo(new[] { "weapon.blaster", "weapon.blaster" }));
        }
        [Test]
        public void ChangedGraphDataAndImpossibleCompletedRoutesAreRejected()
        {
            var good = Snapshot(); var fingerprints = good.LevelFingerprints.ToArray(); fingerprints[0] = new string('f', 64);
            var badGraph = new RunCheckpoint(good.Seed, good.GenerationVersion, good.Fingerprint, good.Mode, good.CurrentRoom,
                good.RoomVisits, good.EquipmentIds, good.RunSeed, good.SequenceVersion, good.LevelNumber, good.LevelSeeds, fingerprints, good.CompletedRoutes);
            Assert.Throws<ArgumentException>(() => RunSequence.Restore(badGraph, Catalog()));
            var graph = new RunSequence(good.RunSeed, Catalog()).Levels[0];
            var badRoute = new RunCheckpoint(good.Seed, good.GenerationVersion, good.Fingerprint, good.Mode, good.CurrentRoom,
                good.RoomVisits, good.EquipmentIds, good.RunSeed, good.SequenceVersion, good.LevelNumber, good.LevelSeeds, good.LevelFingerprints,
                new[] { new[] { graph.StartRoomId, graph.TerminalRoomId } });
            Assert.Throws<ArgumentException>(() => RunSequence.Restore(badRoute, Catalog()));
        }
        [Test]
        public void ContinueRequiresExplicitAvailabilityAndUsesGameplayLoadingPath()
        {
            var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
            Assert.That(flow.TryRequest(ApplicationCommand.Continue), Is.False);
            flow.SetContinueAvailable(true);
            Assert.That(flow.TryRequest(ApplicationCommand.Continue), Is.True);
            Assert.That(flow.PendingCommand, Is.EqualTo(ApplicationCommand.Continue));
            Assert.That(flow.Destination, Is.EqualTo(ApplicationState.Gameplay));
            Assert.That(flow.TryRequest(ApplicationCommand.Continue), Is.False);
            flow.CompleteLoading(ApplicationState.Gameplay);
            Assert.That(flow.PendingCommand, Is.Null);
        }
    }
}
