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
        private GameplaySession session;
        private MultiplayerSession multiplayer;
        private Action<SessionRole> startMultiplayerSession;
        private Button host, join;
        private Button continueGame;
        private Action requestContinue;
        private Button deathReturn;
        private VisualElement deathOverlay;
        private ProgressBar healthBar;
        private ProgressBar teammateHealth,reviveBar;
        private Label runXp;
        private int lastHealth=-1;
        private float damageUntil;
        private Label healthStatus;
        private VisualElement completionOverlay;
        private Button completionReturn;
        private Func<Guid,RunRewardSummary> reward;
        private AudioOptionsPanel audioOptions;
        private int heardLevel;
        private RunRoomId heardRoom;
        private bool heardRoomSet;
        private ExploredMinimap minimap;
        private NewRunPanel newRunPanel;
        public void BindDifficulty(ApplicationRoot root)
        {
            if(newGame==null)return;
            newRunPanel=gameObject.AddComponent<NewRunPanel>();newRunPanel.Bind(root,View);
        }
        public void OpenRunSetup(Func<bool> start,bool chooseDifficulty,string title,Button source=null)
        { newRunPanel?.Open(start,chooseDifficulty,title,source); }
        public void BindRewards(Func<Guid,RunRewardSummary> source) => reward = source;
        private void Update()
        {

            if (healthBar != null) healthBar.EnableInClassList("health-damaged",Time.unscaledTime < damageUntil);
            if (runXp != null) runXp.text = $"Run XP  {session?.Life.RunXp ?? multiplayer?.RunXp ?? 0}";
            var hint = View.Q<Label>("completion-hint");
            if (hint != null) hint.text = session != null ? session.CompletionHint : multiplayer?.CompletionHint ?? string.Empty;
            if (flow?.State != ApplicationState.Completed) return;
            var outcome = session?.Outcome ?? multiplayer?.Outcome;
            var summary = outcome != null ? reward?.Invoke(outcome.RunId) : null;
            var label = View.Q<Label>("completion-rewards");
            if (label != null) label.text = summary == null ? "Run completed." : $"Run XP: {summary.Receipt.RunXp}\nProfile XP awarded: {summary.ProfileXp}\nFractional profile XP carries forward.";
            bool ready = multiplayer == null || multiplayer.CanLeaveCompletion;
            if (completionReturn != null && completionReturn.enabledSelf != ready)
            { completionReturn.SetEnabled(ready); if (ready) completionReturn.Focus(); }
            var pending = View.Q<Label>("completion-status");
            if (pending != null) pending.text = ready ? "Progress saved. This run can no longer be continued." : "Waiting for your teammate to save their reward...";
        }

        public void SetProfileAvailable(bool available, string message)
        {
            if (available) return;
            newGame?.SetEnabled(false); host?.SetEnabled(false); join?.SetEnabled(false);
            ShowSaveStatus(message);
        }
        public void ShowProfile(ProfileProgress profile)
        { var label = View.Q<Label>("profile-xp"); if (label != null) label.text = profile == null ? "Profile unavailable" : $"Profile XP  {profile.Experience}"; }

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
        public void ShowSaveStatus(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (status != null) status.text = message;
            if (healthStatus != null) healthStatus.text = message;
        }

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
            session.HealthChanged += ShowHealth;
            session.Input.SetPauseInterceptor(()=>audioOptions.ConsumePause());
            ShowRun();
            ShowHealth();
            ShowMinimap();
        }

        private void ShowRun()
        {
            var label = View.Q<Label>("run-readout");
            if (label == null || session == null) return;
            var progression = session.Rooms.Progression;
            PresentRoomSound(session.LevelNumber,progression.CurrentRoomId);
            label.text = $"Level {session.LevelNumber} / {RunSequence.LevelCount}  ·  {DifficultyRules.DisplayName(session.Difficulty)}  ·  Rooms entered {progression.RoomsEntered}\n" +
                (progression.IsTerminal ? "Level exit room. Connected rooms remain available." : "Walk onto a doorway to travel. You can return to rooms you have visited.");
            if (session.Persistence?.RecoveryMessage != null) label.text += "\n" + session.Persistence.RecoveryMessage;
            if (progression.Failure != null) label.text += "\nThe room could not be loaded. Return to the menu and try Continue.";
            ShowMinimap();
        }

        public void BindLocalSessionControls(Action<SessionRole> start)
        {
            startMultiplayerSession = start;
            var panel = View.Q("local-session-controls");
            if (panel == null) return;
            panel.style.display = Debug.isDebugBuild && (Application.isEditor || SessionDiagnostics.Option("--local-session-controls")=="true") ? DisplayStyle.Flex : DisplayStyle.None;
            host = View.Q<Button>("local-host"); join = View.Q<Button>("local-join");
            host.clicked += Host; join.clicked += Join;
        }
        private void Host() => OpenRunSetup(()=>{startMultiplayerSession?.Invoke(SessionRole.Host);return true;},true,"Host Co-op",host);
        private void Join() => OpenRunSetup(()=>{startMultiplayerSession?.Invoke(SessionRole.Client);return true;},false,"Join Co-op",join);

        public void BindMultiplayer(MultiplayerSession session)
        {
            multiplayer = session;
            multiplayer.Changed += ShowMultiplayer;
            multiplayer.SetPauseInterceptor(()=>audioOptions.ConsumePause());
            var hint = View.Q<Label>("pause-hint");
            if (hint != null) hint.text = "Local pause only. The other player and host simulation continue.\nRelease movement before resuming.";
            ShowMultiplayer();
        }
        private void ShowMultiplayer()
        {
            if(multiplayer.RoomReady)PresentRoomSound(multiplayer.LevelNumber,multiplayer.CurrentRoom);
            var label = View.Q<Label>("run-readout");
            if (label != null) label.text = SessionDiagnostics.Option("--show-diagnostics")=="true" ? multiplayer.Describe() : multiplayer.PlayerSummary();
            ShowWeapon(multiplayer.WeaponDisplayName);
            ShowHealth();
            ShowMinimap();
        }
        private void ShowMinimap()
        {
            if(minimap==null)return;
            if(session?.Rooms.Progression!=null)
                minimap.Present(new ExploredMap(session.Run,session.Rooms.Progression.VisitedRooms,new[]{session.Rooms.Progression.CurrentRoomId}),session.LevelNumber,1);
            else minimap.Present(multiplayer?.Map, multiplayer?.LevelNumber ?? 1,multiplayer?.LocalPlayer ?? 1);
        }
        private void PresentRoomSound(int level,RunRoomId room)
        {
            if(heardRoomSet && (level!=heardLevel || !room.Equals(heardRoom)))GameAudio.Emit(level!=heardLevel?SoundCue.LevelTransition:SoundCue.RoomTransition);
            heardRoomSet=true;heardLevel=level;heardRoom=room;
        }

        private void ShowHealth()
        {
            if (healthBar == null) return;
            if (teammateHealth != null) teammateHealth.style.display = DisplayStyle.None;
            if (reviveBar != null) reviveBar.style.display = DisplayStyle.None;
            if (multiplayer != null && (!multiplayer.RoomReady || multiplayer.Players[0] == null || multiplayer.Players[1] == null))
            {
                healthBar.style.display = DisplayStyle.None;
                if (healthStatus != null) healthStatus.text = "Waiting for the shared run...";
                return;
            }
            healthBar.style.display = DisplayStyle.Flex;
            var vitals = session != null ? session.Life[0] : multiplayer != null ? multiplayer.Vitals(multiplayer.LocalPlayer - 1) : PlayerVitals.Starting;
            if(lastHealth>=0 && vitals.Current<lastHealth)damageUntil=Time.unscaledTime+.3f;
            lastHealth=vitals.Current;
            healthBar.highValue = vitals.Maximum; healthBar.SetValueWithoutNotify(vitals.Current);
            healthBar.title = $"Health {vitals.Current} / {vitals.Maximum}";
            if (healthStatus == null) return;
            var failure = session != null ? session.HealthFailure : multiplayer?.HealthFailure;
            if (!string.IsNullOrEmpty(failure)) { healthStatus.text = failure; return; }
            if (flow.RunEndPending) { healthStatus.text = "Finalizing run and saving profile rewards..."; return; }
            if (multiplayer == null) { healthStatus.text = string.Empty; return; }
            var local = multiplayer.LocalPlayer - 1; var other = 1 - local;
            var teammate=multiplayer.Vitals(other);
            if(teammateHealth!=null)
            {
                teammateHealth.style.display=DisplayStyle.Flex;teammateHealth.highValue=teammate.Maximum;
                teammateHealth.SetValueWithoutNotify(teammate.Current);
                teammateHealth.title=teammate.State==PlayerLifeState.Downed ? "Teammate downed — move close to revive" : $"Teammate {teammate.Current} / {teammate.Maximum}";
            }
            var progress = multiplayer.Players[vitals.State == PlayerLifeState.Downed ? other : local].ReviveProgress;
            if(reviveBar!=null && (vitals.State==PlayerLifeState.Downed || teammate.State==PlayerLifeState.Downed))
            {
                reviveBar.style.display=DisplayStyle.Flex;reviveBar.highValue=RunLife.ReviveSeconds;reviveBar.SetValueWithoutNotify(progress);
                reviveBar.title=$"Reviving  {progress:F1} / {RunLife.ReviveSeconds:0} seconds";
            }
            healthStatus.text = vitals.State == PlayerLifeState.Downed ? $"Downed — waiting for revival. {progress:F1} / 3 seconds"
                : multiplayer.Vitals(other).State == PlayerLifeState.Downed ? $"Hold E / gamepad South near your teammate to revive. {progress:F1} / 3 seconds" : string.Empty;
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
            if(hud!=null){minimap=new ExploredMinimap();hud.Add(minimap);minimap.Present(null,1,1);}
            pauseOverlay = View.Q("pause-overlay");
            deathOverlay = View.Q("death-overlay"); deathReturn = View.Q<Button>("death-return-to-menu");
            completionOverlay = View.Q("completion-overlay"); completionReturn = View.Q<Button>("completion-return-to-menu");
            if (completionReturn != null) completionReturn.clicked += RequestReturn;
            healthBar = View.Q<ProgressBar>("player-health"); healthStatus = View.Q<Label>("health-status");
            teammateHealth=View.Q<ProgressBar>("teammate-health");reviveBar=View.Q<ProgressBar>("revive-progress");runXp=View.Q<Label>("run-xp");
            if (deathReturn != null) deathReturn.clicked += RequestReturn;
            if (newGame != null) newGame.clicked += RequestNewGame;
            if (returnToMenu != null) returnToMenu.clicked += RequestReturn;
            if (exit != null) exit.clicked += RequestExit;
            if (pause != null) pause.clicked += RequestPause;
            if (resume != null) resume.clicked += RequestResume;
            if (pauseReturn != null) pauseReturn.clicked += RequestReturn;

            SetUnavailable("load-game", "Manual save selection is unavailable. Use Continue for the saved run.");
            audioOptions=gameObject.AddComponent<AudioOptionsPanel>();audioOptions.Bind(View,flow,GameAudio.Current);
            flow.StateChanged += Render;
            Render(flow.State);
        }

        private void RequestNewGame() => OpenRunSetup(()=>flow.TryRequest(ApplicationCommand.NewGame),true,"New Game",newGame);
        private void RequestReturn()
        { if (flow.State != ApplicationState.Completed || multiplayer == null || multiplayer.CanLeaveCompletion) flow.TryRequest(ApplicationCommand.ReturnToMainMenu); }
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
            if(state==ApplicationState.Death)GameAudio.Emit(SoundCue.RunOver);
            if(state==ApplicationState.Completed)GameAudio.Emit(SoundCue.RunComplete);
            newGame?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame));
            continueGame?.SetEnabled(flow.CanRequest(ApplicationCommand.Continue));
            host?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame)); join?.SetEnabled(flow.CanRequest(ApplicationCommand.NewGame));
            returnToMenu?.SetEnabled(flow.CanRequest(ApplicationCommand.ReturnToMainMenu));
            exit?.SetEnabled(flow.CanRequest(ApplicationCommand.Exit));
            pause?.SetEnabled(flow.CanRequest(ApplicationCommand.Pause));
            resume?.SetEnabled(flow.CanRequest(ApplicationCommand.Resume));
            pauseReturn?.SetEnabled(flow.CanRequest(ApplicationCommand.ReturnToMainMenu));
            deathReturn?.SetEnabled(state == ApplicationState.Death && flow.CanRequest(ApplicationCommand.ReturnToMainMenu));
            if (completionOverlay != null) completionOverlay.style.display = state == ApplicationState.Completed ? DisplayStyle.Flex : DisplayStyle.None;
            if (state == ApplicationState.Completed && completionReturn != null) completionReturn.schedule.Execute(() => completionReturn.Focus());
            if (deathOverlay != null) deathOverlay.style.display = state == ApplicationState.Death ? DisplayStyle.Flex : DisplayStyle.None;
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
            else if (state == ApplicationState.Death && deathReturn != null)
                deathReturn.schedule.Execute(() => { if (flow.State == ApplicationState.Death) deathReturn.Focus(); });
            else if (state == ApplicationState.Gameplay)
                View.focusController?.focusedElement?.Blur();
        }

        private void OnDestroy()
        {
            if (session != null && session.Rooms != null) session.Rooms.RoomChanged -= ShowRun;
            if (session != null) session.HealthChanged -= ShowHealth;
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
            if (deathReturn != null) deathReturn.clicked -= RequestReturn;
            if (completionReturn != null) completionReturn.clicked -= RequestReturn;
        }
    }
}
