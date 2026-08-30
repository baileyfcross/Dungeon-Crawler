using System;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Maps UI intent to commands, then renders the flow's resulting state.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ApplicationScreen : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private ApplicationState representedState;
        private ApplicationFlow flow;
        private Button newGame;
        private Button returnToMenu;
        private Button exit;
        private Button pause, resume, pauseReturn;
        private VisualElement hud, pauseOverlay;
        private Label status;

        public VisualElement View => document.rootVisualElement;

        public void Bind(ApplicationFlow applicationFlow)
        {
            if (flow != null)
                throw new InvalidOperationException("A screen can only be bound once.");

            flow = applicationFlow ?? throw new ArgumentNullException(nameof(applicationFlow));
            newGame = View.Q<Button>("new-game");
            returnToMenu = View.Q<Button>("return-to-menu");
            exit = View.Q<Button>("exit");
            status = View.Q<Label>("status");
            pause = View.Q<Button>("pause");
            resume = View.Q<Button>("resume");
            pauseReturn = View.Q<Button>("pause-return-to-menu");
            hud = View.Q("gameplay-hud");
            pauseOverlay = View.Q("pause-overlay");
            if (newGame != null) newGame.clicked += RequestNewGame;
            if (returnToMenu != null) returnToMenu.clicked += RequestReturn;
            if (exit != null) exit.clicked += RequestExit;
            if (pause != null) pause.clicked += RequestPause;
            if (resume != null) resume.clicked += RequestResume;
            if (pauseReturn != null) pauseReturn.clicked += RequestReturn;

            SetUnavailable("continue", "Unavailable: no save system or valid save exists yet.");
            SetUnavailable("load-game", "Unavailable: save loading is planned for Phase 6.");
            SetUnavailable("options", "Unavailable: options are planned for Phase 7.");
            flow.StateChanged += Render;
            Render(flow.State);
        }

        private void RequestNewGame() => flow.TryRequest(ApplicationCommand.NewGame);
        private void RequestReturn() => flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
        private void RequestExit() => flow.TryRequest(ApplicationCommand.Exit);
        private void RequestPause() => flow.TryRequest(ApplicationCommand.Pause);
        private void RequestResume() => flow.TryRequest(ApplicationCommand.Resume);

        private void SetUnavailable(string elementName, string reason)
        {
            var button = View.Q<Button>(elementName);
            if (button == null) return;
            button.SetEnabled(false);
            button.tooltip = reason;
        }

        private void Render(ApplicationState state)
        {
            newGame?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame));
            returnToMenu?.SetEnabled(flow.CanRequest(ApplicationCommand.ReturnToMainMenu));
            exit?.SetEnabled(flow.CanRequest(ApplicationCommand.Exit));
            pause?.SetEnabled(flow.CanRequest(ApplicationCommand.Pause));
            resume?.SetEnabled(flow.CanRequest(ApplicationCommand.Resume));
            pauseReturn?.SetEnabled(flow.CanRequest(ApplicationCommand.ReturnToMainMenu));
            if (hud != null) hud.style.display = state == ApplicationState.Gameplay ? DisplayStyle.Flex : DisplayStyle.None;
            if (pauseOverlay != null) pauseOverlay.style.display = state == ApplicationState.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            if (status != null)
                status.text = state == ApplicationState.Loading ? "Loading..." : flow.ErrorMessage;
            if (state == ApplicationState.Paused && resume != null)
            {
                resume.schedule.Execute(() => { if (flow.State == ApplicationState.Paused) resume.Focus(); });
            }
            else if (state == representedState && newGame != null)
                newGame.schedule.Execute(() => { if (flow.State == representedState) newGame.Focus(); });
            else if (state == ApplicationState.Gameplay)
                View.focusController?.focusedElement?.Blur();
        }

        private void OnDestroy()
        {
            if (flow != null) flow.StateChanged -= Render;
            if (newGame != null) newGame.clicked -= RequestNewGame;
            if (returnToMenu != null) returnToMenu.clicked -= RequestReturn;
            if (exit != null) exit.clicked -= RequestExit;
            if (pause != null) pause.clicked -= RequestPause;
            if (resume != null) resume.clicked -= RequestResume;
            if (pauseReturn != null) pauseReturn.clicked -= RequestReturn;
        }
    }
}
