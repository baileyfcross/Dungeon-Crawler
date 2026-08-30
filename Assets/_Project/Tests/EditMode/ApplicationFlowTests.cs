using System.Collections.Generic;
using NUnit.Framework;
using SpaceCrawler.Core;

namespace SpaceCrawler.Tests.EditMode
{
    public sealed class ApplicationFlowTests
    {
        [Test]
        public void CommandsProduceTheRequiredFlowAndLoadingStates()
        {
            var flow = new ApplicationFlow();
            var observed = new List<ApplicationState> { flow.State };
            flow.StateChanged += observed.Add;

            Assert.That(flow.TryRequest(ApplicationCommand.Initialize), Is.True);
            Assert.That(flow.CompleteLoading(ApplicationState.MainMenu), Is.True);
            Assert.That(flow.TryRequest(ApplicationCommand.NewGame), Is.True);
            Assert.That(flow.CompleteLoading(ApplicationState.Gameplay), Is.True);
            Assert.That(flow.TryRequest(ApplicationCommand.ReturnToMainMenu), Is.True);
            Assert.That(flow.CompleteLoading(ApplicationState.MainMenu), Is.True);

            Assert.That(observed, Is.EqualTo(new[]
            {
                ApplicationState.Boot, ApplicationState.Loading, ApplicationState.MainMenu,
                ApplicationState.Loading, ApplicationState.Gameplay,
                ApplicationState.Loading, ApplicationState.MainMenu
            }));
        }

        [TestCase(ApplicationCommand.Initialize)]
        [TestCase(ApplicationCommand.NewGame)]
        [TestCase(ApplicationCommand.ReturnToMainMenu)]
        [TestCase(ApplicationCommand.Exit)]
        public void LoadingRejectsReentrantCommands(ApplicationCommand command)
        {
            var flow = new ApplicationFlow();
            flow.TryRequest(ApplicationCommand.Initialize);
            Assert.That(flow.TryRequest(command), Is.False);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Loading));
            Assert.That(flow.Destination, Is.EqualTo(ApplicationState.MainMenu));
        }

        [Test]
        public void CompletionForTheWrongDestinationCannotAdvanceState()
        {
            var flow = new ApplicationFlow();
            flow.TryRequest(ApplicationCommand.Initialize);
            Assert.That(flow.CompleteLoading(ApplicationState.Gameplay), Is.False);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Loading));
            Assert.That(flow.CompleteLoading(ApplicationState.MainMenu), Is.True);
            Assert.That(flow.CompleteLoading(ApplicationState.MainMenu), Is.False);
        }

        [Test]
        public void FailedLoadRestoresSourceStateAndAllowsRetry()
        {
            var flow = MainMenuFlow();
            flow.TryRequest(ApplicationCommand.NewGame);
            Assert.That(flow.FailLoading("Scene unavailable"), Is.True);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.MainMenu));
            Assert.That(flow.Destination, Is.Null);
            Assert.That(flow.ErrorMessage, Is.EqualTo("Scene unavailable"));
            Assert.That(flow.TryRequest(ApplicationCommand.NewGame), Is.True);
            Assert.That(flow.ErrorMessage, Is.Empty);
        }

        [Test]
        public void ExitIsAnExplicitTerminalStateAndOnlyAvailableFromMenu()
        {
            var boot = new ApplicationFlow();
            Assert.That(boot.TryRequest(ApplicationCommand.Exit), Is.False);
            var flow = MainMenuFlow();
            Assert.That(flow.TryRequest(ApplicationCommand.Exit), Is.True);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Exiting));
            Assert.That(flow.TryRequest(ApplicationCommand.NewGame), Is.False);
            Assert.That(flow.TryRequest(ApplicationCommand.Exit), Is.False);
        }

        [Test]
        public void UnsupportedOrOutOfContextCommandsDoNotPublishStateChanges()
        {
            var flow = MainMenuFlow();
            int notifications = 0;
            flow.StateChanged += _ => notifications++;
            Assert.That(flow.TryRequest(ApplicationCommand.ReturnToMainMenu), Is.False);
            Assert.That(flow.TryRequest((ApplicationCommand)999), Is.False);
            Assert.That(flow.FailLoading("Not loading"), Is.False);
            Assert.That(notifications, Is.Zero);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.MainMenu));
        }

        private static ApplicationFlow MainMenuFlow()
        {
            var flow = new ApplicationFlow();
            flow.TryRequest(ApplicationCommand.Initialize);
            flow.CompleteLoading(ApplicationState.MainMenu);
            return flow;
        }
    }
}
