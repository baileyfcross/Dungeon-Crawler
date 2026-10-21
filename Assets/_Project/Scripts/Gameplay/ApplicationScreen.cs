using System;
using System.Text;
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
        private GameplaySession session;
        private MultiplayerSession multiplayer;
        private Action<SessionRole> startMultiplayerSession;
        private Button host, join;
        private Button continueGame;
        private Action requestContinue;

        public void BindContinue(Action request)
        {
            requestContinue = request; continueGame = View.Q<Button>("continue");
            if (continueGame != null) continueGame.clicked += RequestContinue;
        }
        private void RequestContinue() => requestContinue?.Invoke();
        public void UpdateContinue(RunCheckpoint checkpoint)
        {
            if (continueGame == null) return;
            continueGame.SetEnabled(checkpoint != null && flow.CanRequest(ApplicationCommand.Continue));
            continueGame.text = checkpoint == null ? "Continue  /  No valid save" : $"Continue  /  Level {checkpoint.LevelNumber}  /  {(checkpoint.Mode == SavedSessionMode.Solo ? "Solo" : "Co-op")}";
            continueGame.tooltip = checkpoint == null ? "No compatible checkpoint is available." : "Restore the saved room entry.";
        }
        public void ShowSaveStatus(string message) { if (status != null && !string.IsNullOrEmpty(message)) status.text = message; }

        public VisualElement View => document.rootVisualElement;

        public bool IsPointerOverInteractiveUi(Vector2 screenPosition)
        {
            if (View.panel == null) return true;
            // Input uses bottom-left screen origin; UI Toolkit panel picking uses top-left.
            var panelPosition = RuntimePanelUtils.ScreenToPanel(View.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = View.panel.Pick(panelPosition);
            return picked is Button || picked?.GetFirstAncestorOfType<Button>() != null;
        }

        public void ShowWeapon(string displayName)
        {
            var label = View.Q<Label>("equipped-weapon");
            if (label != null) label.text = $"{displayName}  /  ATTACK: LEFT MOUSE OR GAMEPAD WEST";
        }

        public void BindRun(GameplaySession gameplaySession)
        {
            session = gameplaySession;
            session.Rooms.RoomChanged += ShowRun;
            ShowRun();
        }

        private void ShowRun()
        {
            var label = View.Q<Label>("run-readout");
            if (label == null || session == null) return;
            var progression = session.Rooms.Progression;
            var node = session.Run.GetRoom(progression.CurrentRoomId);
            var text = new StringBuilder();
            text.AppendLine($"Level {session.LevelNumber} / {RunSequence.LevelCount}  /  {node.Id}  /  {node.Category}  /  {node.DefinitionId}  /  {progression.State}");
            text.Append($"Seed {session.Run.Seed}  /  Generation v{session.Run.GenerationVersion}  /  Rooms entered {progression.RoomsEntered}");
            for (var i = 0; i < progression.Choices.Count; i++)
            {
                var next = session.Run.GetRoom(progression.Choices[i].To);
                text.Append($"\n{(progression.Choices.Count == 1 ? "CENTER" : i == 0 ? "LEFT" : "RIGHT")} -> {next.Id} / {next.Category}");
            }
            if (progression.IsTerminal) text.Append("\nTerminal room reached. No forward exits.");
            if (progression.Failure != null) text.Append("\nRoom loading failed: " + progression.Failure);
            label.text = text.ToString();
        }

        public void BindLocalSessionControls(Action<SessionRole> start)
        {
            startMultiplayerSession = start;
            var panel = View.Q("local-session-controls");
            if (panel == null) return;
            panel.style.display = Debug.isDebugBuild ? DisplayStyle.Flex : DisplayStyle.None;
            host = View.Q<Button>("local-host"); join = View.Q<Button>("local-join");
            host.clicked += Host; join.clicked += Join;
        }
        private void Host() => startMultiplayerSession?.Invoke(SessionRole.Host);
        private void Join() => startMultiplayerSession?.Invoke(SessionRole.Client);

        public void BindMultiplayer(MultiplayerSession session)
        {
            multiplayer = session;
            multiplayer.Changed += ShowMultiplayer;
            var hint = View.Q<Label>("pause-hint");
            if (hint != null) hint.text = "Local pause only. The other player and host simulation continue.\nRelease movement before resuming.";
            ShowMultiplayer();
        }
        private void ShowMultiplayer()
        {
            var label = View.Q<Label>("run-readout");
            if (label != null) { label.text = multiplayer.Describe(); label.style.fontSize = 10; }
            ShowWeapon(multiplayer.WeaponName);
        }

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

            SetUnavailable("load-game", "Manual save selection is unavailable. Use Continue for the saved run.");
            SetUnavailable("options", "Options unavailable.");
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
            continueGame?.SetEnabled(flow.CanRequest(ApplicationCommand.Continue));
            host?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame)); join?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame));
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
            if (session != null && session.Rooms != null) session.Rooms.RoomChanged -= ShowRun;
            if (multiplayer != null) multiplayer.Changed -= ShowMultiplayer;
            if (host != null) host.clicked -= Host;
            if (join != null) join.clicked -= Join;
            if (flow != null) flow.StateChanged -= Render;
            if (newGame != null) newGame.clicked -= RequestNewGame;
            if (continueGame != null) continueGame.clicked -= RequestContinue;
            if (returnToMenu != null) returnToMenu.clicked -= RequestReturn;
            if (exit != null) exit.clicked -= RequestExit;
            if (pause != null) pause.clicked -= RequestPause;
            if (resume != null) resume.clicked -= RequestResume;
            if (pauseReturn != null) pauseReturn.clicked -= RequestReturn;
        }
    }
}
