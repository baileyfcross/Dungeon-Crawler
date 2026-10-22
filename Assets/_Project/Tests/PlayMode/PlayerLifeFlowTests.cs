#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class PlayerLifeFlowTests
    {
        private string directory, previousDirectory;
        private ApplicationRoot root;
        [UnitySetUp] public IEnumerator SetUp()
        {
            previousDirectory = ApplicationRoot.SaveDirectoryOverride;
            directory = Path.GetFullPath(Path.Combine("Logs/Validation/Health/Solo", Guid.NewGuid().ToString("N")));
            ApplicationRoot.SaveDirectoryOverride = directory;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity");
            yield return Until(() => (root = Object.FindFirstObjectByType<ApplicationRoot>()) != null && root.Flow.State == ApplicationState.MainMenu);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root.gameObject);
            foreach (var session in Object.FindObjectsByType<GameplaySession>(FindObjectsSortMode.None)) Object.Destroy(session.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            ApplicationRoot.SaveDirectoryOverride = previousDirectory; yield return null;
        }
        private static RunProgression FinalProgression(RunSequence sequence)
        {
            while(true) {
                var progress=new RunProgression(sequence.CurrentGraph,new PlayerIdentity(1));progress.SetGameplayActive(true);progress.TryCompleteEntry(sequence.CurrentGraph.StartRoomId);
                foreach(var edge in sequence.CurrentGraph.Route(progress.CurrentRoomId,sequence.CurrentGraph.TerminalRoomId))
                { progress.TryRequestExit(new ExitIntent(progress.Player,edge.From,edge.To));progress.TryBeginAcceptedEntry();progress.TryCompleteEntry(edge.To); }
                if(!sequence.TryAdvance(progress))return progress;
            }
        }
        private void Submit(string name)
        {
            var button = root.CurrentScreen.View.Q<Button>(name); Assert.That(button, Is.Not.Null); Assert.That(button.enabledInHierarchy, Is.True);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }
        [UnityTest]
        public IEnumerator CompletionRetryCreditsOnceShowsRewardsAndDisablesContinue()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>("Assets/_Project/Data/Runs/DefaultRun.asset").CreateConfiguration();
            var sequence=new RunSequence(42,catalog);
            var progress=FinalProgression(sequence);
            var life=new RunLifeSnapshot(Guid.NewGuid(),250,new[]{PlayerVitals.Starting},new[]{root.Profile.Id});
            var checkpoint=RunCheckpoint.Capture(sequence,progress,SavedSessionMode.Solo,new[]{"weapon.knife"},life);
            new RunCheckpointStore(Path.Combine(directory,"run.save")).Save(checkpoint);root.RefreshCheckpoint();Submit("continue");
            yield return Until(()=>root.Flow.State==ApplicationState.Gameplay);
            var session=root.CurrentGameplay;session.PollLocalDevices=false;session.Input.HandleFocus(true);
            Assert.That(session.Rooms.CurrentRoom.RunExitAnchor.gameObject.activeSelf,Is.True);
            Assert.That(session.Rooms.Progression.Choices.Count,Is.GreaterThan(0));
            Assert.That(session.Interact(new PlayerIdentity(1),true),Is.False,"fresh release is required");
            session.Motor.PlaceAt(session.Rooms.CurrentRoom.RunExitAnchor.position);
            using(new FileStream(Path.Combine(directory,"progress.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
            {
                session.Interact(new PlayerIdentity(1),false);session.Interact(new PlayerIdentity(1),true);yield return null;
                Assert.That(root.Flow.RunEndPending,Is.True);Assert.That(session.Simulation.CanAct,Is.False);
                Assert.That(root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu),Is.False);Assert.That(root.Profile.Experience,Is.Zero);
                Assert.That(session.HealthFailure,Is.Not.Empty);
            }
            yield return Until(()=>root.Flow.State==ApplicationState.Completed);yield return null;
            Assert.That(root.Profile.Experience,Is.EqualTo(15));Assert.That(root.LastRunReward.ProfileXp,Is.EqualTo(15));
            Assert.That(root.CurrentScreen.View.Q<Label>("completion-rewards").text,Does.Contain("250").And.Contain("15"));
            Assert.That(root.CurrentScreen.View.Q("completion-overlay").resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
            Assert.That(new RunCheckpointStore(Path.Combine(directory,"run.save")).Load(out _,out _),Is.EqualTo(CheckpointLoadStatus.Missing));
            Assert.That(session.Interact(new PlayerIdentity(1),true),Is.False);
            Submit("completion-return-to-menu");yield return Until(()=>root.Flow.State==ApplicationState.MainMenu);
            Assert.That(root.Flow.ContinueAvailable,Is.False);Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay);
            Assert.That(root.Profile.Experience,Is.EqualTo(15));Assert.That(root.CurrentGameplay.Life.RunXp,Is.Zero);
        }
        [UnityTest]
        public IEnumerator SuspendContinueAndSoloDeathPreserveOnlyPermanentProgress()
        {
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            var session = root.CurrentGameplay; session.PollLocalDevices = false; session.Input.HandleFocus(true);
            var run = session.Life.RunId;
            session.Life.TrySetMaximum(new PlayerIdentity(1), 150); session.DamagePlayer(Guid.NewGuid(), 25); session.Life.TryAwardRunXp(250);
            Assert.That(root.Profile.Experience, Is.Zero);
            Assert.That(root.CurrentScreen.View.Q<ProgressBar>("player-health").value, Is.EqualTo(75));
            Submit("return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            Submit("continue"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay; session.PollLocalDevices = false;
            Assert.That(session.Life.RunId, Is.EqualTo(run)); Assert.That(session.Life[0].Current, Is.EqualTo(75));
            Assert.That(session.Life[0].Maximum, Is.EqualTo(150)); Assert.That(session.Life.RunXp, Is.EqualTo(250));
            Assert.That(session.DamagePlayer(Guid.NewGuid(), 1000), Is.True);
            yield return Until(() => root.Flow.State == ApplicationState.Death);
            Assert.That(session.Simulation.CanAct, Is.False); Assert.That(root.Profile.Experience, Is.EqualTo(15));
            Assert.That(new RunCheckpointStore(Path.Combine(directory, "run.save")).Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Missing));
            Submit("death-return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            var journal = ProfileProgressStore.ReceiptPath(directory, run);
            File.Move(journal, journal + ".held");
            try { root.RefreshCheckpoint(); Assert.That(root.RequestContinue(), Is.False, "Profile history also blocks a settled run."); }
            finally { File.Move(journal + ".held", journal); }
            Assert.That(root.RequestContinue(), Is.False); Assert.That(root.CurrentScreen.View.Q<Label>("profile-xp").text, Does.Contain("15"));
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            Assert.That(root.CurrentGameplay.Life[0].Current, Is.EqualTo(100)); Assert.That(root.CurrentGameplay.Life.RunXp, Is.Zero);
            Assert.That(root.CurrentGameplay.Life.RunId, Is.Not.EqualTo(run)); Assert.That(root.Profile.Experience, Is.EqualTo(15));
        }
        [UnityTest]
        public IEnumerator FailedSuspendPreservesTheLiveSessionAndAllowsRetry()
        {
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            var session = root.CurrentGameplay;
            session.Motor.PlaceAt(new Vector2(1,2)); // Dirty state requires a real write, even with unchanged-state suppression.
            using (new FileStream(Path.Combine(directory, "run.save.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Sharing violation"));
                Submit("return-to-menu"); yield return null;
                Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Gameplay)); Assert.That(root.CurrentGameplay, Is.SameAs(session));
                Assert.That(session.gameObject.activeInHierarchy, Is.True);
            }
            Submit("return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            Assert.That(root.Flow.ContinueAvailable, Is.True);
        }
        [UnityTest]
        public IEnumerator EndWriteFailureFreezesGameplayUntilReceiptCanBeCommitted()
        {
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            var session = root.CurrentGameplay; var journal = ProfileProgressStore.ReceiptPath(directory, session.Life.RunId);
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            using (new FileStream(journal + ".pending", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                session.DamagePlayer(Guid.NewGuid(), 100);
                yield return null;
                Assert.That(root.Flow.RunEndPending, Is.True); Assert.That(root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu), Is.False);
                Assert.That(session.Simulation.IsActive, Is.False); Assert.That(session.HealthFailure, Is.Not.Empty);
            }
            yield return Until(() => root.Flow.State == ApplicationState.Death);
            Assert.That(root.Profile.Experience, Is.Zero);
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 15;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            yield return null;
        }
    }
}
#endif
