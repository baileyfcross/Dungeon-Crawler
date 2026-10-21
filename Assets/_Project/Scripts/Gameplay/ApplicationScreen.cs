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
        private Action<SpikeMode> startSpike;
        private Button host, join;

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
            text.AppendLine($"{node.Id}  /  {node.Category}  /  {node.DefinitionId}  /  {progression.State}");
            text.Append($"Seed {session.Run.Seed}  /  Generation v{session.Run.GenerationVersion}  /  Rooms entered {progression.RoomsEntered}");
            for (var i = 0; i < progression.Choices.Count; i++)
            {
                var next = session.Run.GetRoom(progression.Choices[i].To);
                text.Append($"\n{(progression.Choices.Count == 1 ? "CENTER" : i == 0 ? "LEFT" : "RIGHT")} -> {next.Id} / {next.Category}");
            }
            if (progression.IsTerminal) text.Append("\nTerminal room reached. No victory objective has been defined. No forward exits.");
            if (progression.Failure != null) text.Append("\nRoom loading failed: " + progression.Failure);
            label.text = text.ToString();
        }

        public void BindSpikeMenu(Action<SpikeMode> start)
        {
            startSpike = start;
            var panel = View.Q("spike-menu");
            if (panel == null) return;
            panel.style.display = Debug.isDebugBuild ? DisplayStyle.Flex : DisplayStyle.None;
            host = View.Q<Button>("dev-host"); join = View.Q<Button>("dev-join");
            host.clicked += Host; join.clicked += Join;
        }
        private void Host() => startSpike?.Invoke(SpikeMode.Host);
        private void Join() => startSpike?.Invoke(SpikeMode.Client);

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
            if (returnToMenu != null) returnToMenu.clicked -= RequestReturn;
            if (exit != null) exit.clicked -= RequestExit;
            if (pause != null) pause.clicked -= RequestPause;
            if (resume != null) resume.clicked -= RequestResume;
            if (pauseReturn != null) pauseReturn.clicked -= RequestReturn;
        }
    }
}
