using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RunSessionTests
    {
        private ApplicationRoot root;
        private GameplaySession session;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (session != null) Object.Destroy(session.gameObject);
            if (root != null) Object.Destroy(root.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewGameOwnsACompleteGraphAndPauseDoesNotRegenerateIt()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity", LoadSceneMode.Single);
            yield return WaitFor(() => (root = Object.FindFirstObjectByType<ApplicationRoot>()) != null && root.Flow.State == ApplicationState.MainMenu);
            root.Flow.TryRequest(ApplicationCommand.NewGame);
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay;
            session.Input.HandleFocus(true);
            var graph = session.Run;
            Assert.That(graph, Is.Not.Null);
            Assert.That(graph.Nodes.Count, Is.GreaterThanOrEqualTo(graph.Configuration.Settings.MinRouteRooms));
            Assert.That(graph.GetRoom(graph.StartRoomId).Category, Is.EqualTo(RoomCategory.Starting));
            Assert.That(graph.GetRoom(graph.TerminalRoomId).IsTerminal, Is.True);
            Assert.That(graph.Connections.Any(e => e.To.Equals(graph.TerminalRoomId)), Is.True);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/_Project/Scenes/Gameplay.unity"));
            Assert.That(session.Combat.Simulation.EquippedWeapon, Is.Not.Null, "The existing combat session remains composed.");
            AssertEquivalent(graph, new RunGenerator().Generate(graph.Seed, graph.Configuration, graph.GenerationVersion));
            root.Flow.TryRequest(ApplicationCommand.Pause);
            yield return null;
            root.Flow.TryRequest(ApplicationCommand.Resume);
            yield return null;
            Assert.That(session.Run, Is.SameAs(graph));
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
            yield return WaitFor(() => root.Flow.State == ApplicationState.MainMenu);
            Assert.That(session == null, Is.True);
            Assert.That(root.CurrentGameplay, Is.Null);
            root.Flow.TryRequest(ApplicationCommand.NewGame);
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay;
            Assert.That(session.Run, Is.Not.SameAs(graph));
            AssertEquivalent(session.Run, new RunGenerator().Generate(session.Run.Seed, session.Run.Configuration));
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ExplicitAuthoritySeedReachesTheSameCoreGenerator()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Gameplay/ControlsTestArea.prefab");
            foreach (var seed in new[] { 0UL, ulong.MaxValue })
            {
                session = Object.Instantiate(prefab).GetComponent<GameplaySession>();
                var flow = new ApplicationFlow();
                flow.TryRequest(ApplicationCommand.Initialize);
                flow.CompleteLoading(ApplicationState.MainMenu);
                flow.TryRequest(ApplicationCommand.NewGame);
                flow.CompleteLoading(ApplicationState.Gameplay);
                session.Bind(new PlayerIdentity(1), flow, seed);
                Assert.That(session.Run.Seed, Is.EqualTo(seed));
                AssertEquivalent(session.Run, new RunGenerator().Generate(seed, session.Run.Configuration));
                yield return null;
                Object.Destroy(session.gameObject);
                yield return null;
            }
        }
#endif

        private static void AssertEquivalent(DungeonGraph actual, DungeonGraph expected)
        {
            Assert.That(actual.Nodes.Select(n => (n.Id, n.DefinitionId, n.Category, n.IsTerminal)),
                Is.EqualTo(expected.Nodes.Select(n => (n.Id, n.DefinitionId, n.Category, n.IsTerminal))));
            Assert.That(actual.Connections.Select(e => (e.From, e.To)), Is.EqualTo(expected.Connections.Select(e => (e.From, e.To))));
        }
        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            yield return null;
        }
    }
}
