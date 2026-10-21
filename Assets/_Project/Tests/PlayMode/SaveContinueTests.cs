#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class SaveContinueTests
    {
        private ApplicationRoot root;
        private string previousDirectory, directory;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousDirectory = ApplicationRoot.SaveDirectoryOverride;
            directory = Path.GetFullPath(Path.Combine("Logs", "Validation", "Saves", "Integration", Guid.NewGuid().ToString("N")));
            ApplicationRoot.SaveDirectoryOverride = directory;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity");
            yield return Until(() => (root = Object.FindFirstObjectByType<ApplicationRoot>()) != null && root.Flow.State == ApplicationState.MainMenu);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root.gameObject);
            foreach (var session in Object.FindObjectsByType<GameplaySession>(FindObjectsSortMode.None)) Object.Destroy(session.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            ApplicationRoot.SaveDirectoryOverride = previousDirectory;
            yield return null;
        }
        private void Submit(string name)
        {
            var button = root.CurrentScreen.View.Q<Button>(name);
            Assert.That(button.enabledInHierarchy, Is.True);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }
        [UnityTest]
        public IEnumerator ContinueRestoresLaterLevelRoomAndResetsTransientCombat()
        {
            Assert.That(root.Flow.ContinueAvailable, Is.False);
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            var session = root.CurrentGameplay;
            session.Input.HandleFocus(true);
            while (session.LevelNumber < 2 || session.Rooms.Progression.CurrentRoomId.Value == 0)
            {
                var level = session.LevelNumber; var room = session.Rooms.Progression.CurrentRoomId;
                var edge = session.Run.Route(room,session.Run.TerminalRoomId)[0];
                var exit = session.Rooms.CurrentRoom.ExitFor(edge,session.Rooms.Progression.Choices);
                session.Motor.PlaceAt(exit.transform.position);
                yield return Until(() => session.LevelNumber != level || !session.Rooms.Progression.CurrentRoomId.Equals(room));
            }
            var saved = root.SavedCheckpoint;
            Assert.That(saved.LevelNumber, Is.EqualTo(2));
            session.Combat.ClearRoom(); session.Rooms.AddCombatFixtureToCurrentRoom();
            session.Combat.RegisterRoomTargets(session.Rooms.CurrentRoom.CombatTargets);
            var target = session.Combat.Targets[0];
            target.Health.TryApplyDamage(new DamageRequest(session.Simulation.State.Player, 999, target.Identity, 25), out _);
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
            yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            Submit("continue"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay;
            Assert.That(session.LevelNumber, Is.EqualTo(saved.LevelNumber));
            Assert.That(session.Rooms.Progression.CurrentRoomId, Is.EqualTo(saved.CurrentRoom));
            Assert.That(session.Rooms.Progression.VisitedRooms, Is.EqualTo(saved.RoomVisits));
            Assert.That(session.Sequence.CompletedRoutes.Count, Is.EqualTo(1));
            session.Combat.ClearRoom(); session.Rooms.AddCombatFixtureToCurrentRoom();
            session.Combat.RegisterRoomTargets(session.Rooms.CurrentRoom.CombatTargets);
            Assert.That((Vector2)session.Motor.Body.position, Is.EqualTo(new Vector2(saved.World.Players[0].Position.X,saved.World.Players[0].Position.Y)));
            Assert.That(session.Combat.Targets[0].Health.Current, Is.EqualTo(session.Combat.Targets[0].InitialHealth));
            Assert.That(session.Combat.Projectiles, Is.Empty);
        }
        [UnityTest]
        public IEnumerator FailedMultiplayerLoadReturnsToMenuWithoutChangingNextNewGameToHosting()
        {
            var data = new UnityEditor.SerializedObject(root);
            var prefab = data.FindProperty("multiplayerSession").objectReferenceValue;
            data.FindProperty("multiplayerSession").objectReferenceValue = null; data.ApplyModifiedPropertiesWithoutUndo();
            var oldScreen = root.CurrentScreen;
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Missing explicitly authored multiplayer session composition"));
            Assert.That(root.StartMultiplayerSession(SessionRole.Host), Is.True);
            yield return Until(() => root.Flow.State == ApplicationState.MainMenu && root.CurrentScreen != null && root.CurrentScreen != oldScreen);
            data.Update(); data.FindProperty("multiplayerSession").objectReferenceValue = prefab; data.ApplyModifiedPropertiesWithoutUndo();
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            Assert.That(root.CurrentGameplay, Is.Not.Null);
            Assert.That(root.CurrentMultiplayer, Is.Null);
        }

        [UnityTest]
        public IEnumerator CorruptSlotDisablesContinueWithoutDeletingIt()
        {
            Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            var bytes = new byte[] { 7, 8, 9 }; File.WriteAllBytes(Path.Combine(directory, "run.save"), bytes);
            root.RefreshCheckpoint();
            Assert.That(root.RequestContinue(), Is.False);
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.MainMenu));
            Assert.That(root.CurrentScreen.View.Q<Button>("continue").enabledInHierarchy, Is.False);
            Assert.That(root.CurrentScreen.View.Q<Label>("status").text, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "run.save")), Is.EqualTo(bytes));
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
