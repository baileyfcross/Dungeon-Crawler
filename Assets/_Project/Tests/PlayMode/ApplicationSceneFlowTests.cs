using System;
using System.Collections;
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
    public sealed class ApplicationSceneFlowTests
    {
        [UnityTest]
        public IEnumerator BootAndUiCommandsCompleteTwoRoundTripsWithoutDuplicatingSystems()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity");
            yield return WaitFor(() => Object.FindFirstObjectByType<ApplicationRoot>()?.Flow?.State == ApplicationState.MainMenu);
            var root = Object.FindFirstObjectByType<ApplicationRoot>();

            for (var roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
                AssertSingleRootAndScreen(root);
                var menu = root.CurrentScreen;
                foreach (var name in new[] { "continue", "load-game", "options" })
                {
                    var unavailable = menu.View.Q<Button>(name);
                    Assert.That(unavailable, Is.Not.Null, name);
                    Assert.That(unavailable.enabledInHierarchy, Is.False, name);
                }
                Assert.That(menu.View.Q<Button>("exit").enabledInHierarchy, Is.True);

                Submit(menu.View.Q<Button>("new-game"));
                Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Loading));
                Assert.That(menu.View.Q<Button>("new-game").enabledInHierarchy, Is.False);
                Assert.That(root.Flow.TryRequest(ApplicationCommand.NewGame), Is.False, "No duplicate transition while loading.");
                yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Gameplay"));
                Assert.That(menu == null, Is.True, "The old screen is torn down with its scene.");
                AssertSingleRootAndScreen(root);

                var gameplay = root.CurrentScreen;
                Submit(gameplay.View.Q<Button>("return-to-menu"));
                yield return WaitFor(() => root.Flow.State == ApplicationState.MainMenu);
                Assert.That(gameplay == null, Is.True);
            }
            AssertSingleRootAndScreen(root);
            // The runner rejects unexpected errors/exceptions; development flow logs are intentional.
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Discovery is test inspection/cleanup only, never runtime dependency resolution.
            foreach (var root in Object.FindObjectsByType<ApplicationRoot>(FindObjectsSortMode.None))
                Object.Destroy(root.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None))
                Object.Destroy(screen.gameObject);
            yield return null;
        }

        private static void Submit(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.enabledInHierarchy, Is.True);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private static void AssertSingleRootAndScreen(ApplicationRoot expected)
        {
            var roots = Object.FindObjectsByType<ApplicationRoot>(FindObjectsSortMode.None);
            Assert.That(roots, Has.Length.EqualTo(1));
            Assert.That(roots[0], Is.SameAs(expected));
            Assert.That(Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Scene transition timed out.");
                yield return null;
            }
            yield return null; // Allow UI layout and deferred destruction to settle.
        }
    }
}
