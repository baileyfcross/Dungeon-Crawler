using System;
using System.Threading;
using System.IO;
using SpaceCrawler.Core;
using SpaceCrawler.Platform;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceCrawler.Gameplay
{
    /// <summary>The Boot scene's sole composition root. Scene views receive explicit dependencies.</summary>
    public sealed class ApplicationRoot : MonoBehaviour
    {
        [SerializeField] private string mainMenuScenePath;
        [SerializeField] private string gameplayScenePath;
        [SerializeField] private ApplicationScreen mainMenuScreen;
        [SerializeField] private ApplicationScreen gameplayScreen;
        [SerializeField] private GameplaySession gameplaySession;
        [SerializeField] private MultiplayerSession multiplayerSession;
        private CancellationToken lifetime;
        private SessionRole pendingSessionRole;
        private bool automaticSessionStarted;
        private RunCheckpointStore checkpointStore;
        private RunCheckpoint pendingCheckpoint;
        private string saveNotice;
        private ProfileProgressStore progress;
        public ProfileProgress Profile => progress?.Profile;
        public RunRewardSummary LastRunReward { get; private set; }
        private readonly System.Collections.Generic.Dictionary<Guid,RunRewardSummary> runRewards = new System.Collections.Generic.Dictionary<Guid,RunRewardSummary>();
        private string profileFailure;
        private string profileDirectory;
        private AccountProfileStore accounts;
        private SessionParticipantsStore participants;
        private SteamClientService steam;
        private SteamLobbyService lobbies;
        private SteamSessionConnection pendingSteam;
        private bool steamContinue,accountChanged,steamProfileAssociated;
        private ulong invitation;
        private string steamNotice;
        private double nextIdentityCheck;
        private bool? restoreBackground;
        public bool SteamReady=>steam?.Online==true && !accountChanged;
        public bool SteamBusy=>lobbies?.Busy==true;
        public bool SteamSessionActive=>lobbies?.Lobby>0;
        public bool CanResumeCurrentRun=>!accountChanged;
        public bool HasSteamInvitation=>invitation!=0;
        public bool CanContinueCurrentSave=>SavedCheckpoint!=null && !accountChanged && (SavedCheckpoint.Mode==SavedSessionMode.Solo || LocalDiagnostics || SteamReady && SteamProfileAssociated);
        public bool SteamProfileAssociated=>SteamReady && Profile!=null && steamProfileAssociated;
        private bool VerifySteamProfile()
        {
            try{steamProfileAssociated=SteamReady && Profile!=null && accounts.IsAssociated(steam.AccountId.ToString(),Profile.Id);return SteamProfileAssociated;}
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {steamProfileAssociated=false;steamNotice="Steam profile ownership could not be verified. Existing data is preserved.";return false;}
        }

        public bool CanInviteSteam=>SteamReady && lobbies?.IsHost==true && SteamSessionActive;
        public string SteamStatus=>steamNotice ?? (SteamReady ? SteamProfileAssociated ? lobbies?.Status ?? "Steam ready." : "Link this local profile to the signed-in Steam account to play co-op. XP and saves are preserved and backed up first." : "Steam unavailable. Local solo play remains available.");
        private bool LocalDiagnostics=>Application.isEditor || SessionDiagnostics.Option("--session-role")!=null || SessionDiagnostics.Option("--completion-validation")!=null || SessionDiagnostics.Option("--save-validation")!=null || SessionDiagnostics.Option("--disable-steam")=="true";

        public RunCheckpoint SavedCheckpoint { get; private set; }
#if UNITY_EDITOR
        public static string SaveDirectoryOverride { get; set; }
#endif

        public ApplicationFlow Flow { get; private set; }
        public ApplicationScreen CurrentScreen { get; private set; }
        public GameplaySession CurrentGameplay { get; private set; }
        public MultiplayerSession CurrentMultiplayer { get; private set; }

        public bool StartMultiplayerSession(SessionRole mode)
        {
            if (!Debug.isDebugBuild || mode == SessionRole.None || Flow.State != ApplicationState.MainMenu) return false;
            pendingSessionRole = mode;
            pendingCheckpoint = null;
            return Flow.TryRequest(ApplicationCommand.NewGame);
        }

        public bool RequestContinue()
        {
            if (Flow.State != ApplicationState.MainMenu) return false;
            RefreshCheckpoint();
            if (SavedCheckpoint == null || accountChanged) return false;
            if(SavedCheckpoint.Mode==SavedSessionMode.Cooperative && !LocalDiagnostics)return HostSteam(true);
            pendingCheckpoint = SavedCheckpoint;
            pendingSessionRole = pendingCheckpoint.Mode == SavedSessionMode.Cooperative ? SessionRole.Host : SessionRole.None;
            return Flow.TryRequest(ApplicationCommand.Continue);
        }

        private bool ReadCheckpoint(out RunCheckpoint checkpoint, out string message)
        {
            if (progress == null || accountChanged) { checkpoint = null; message = profileFailure; return false; }
            var status = checkpointStore.Load(out checkpoint, out message);
            if (status != CheckpointLoadStatus.Valid) return false;
            try
            {
                multiplayerSession.ValidateCheckpoint(checkpoint);
                if (Profile.HasEndedRun(checkpoint.Life.RunId)) throw new InvalidOperationException("This run has ended.");
                if (checkpoint.Life.Profiles[0] != Guid.Empty && checkpoint.Life.Profiles[0] != Profile.Id)
                    throw new InvalidOperationException("Run belongs to a different host profile.");
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
            { checkpoint = null; message = "Saved run is incompatible with the installed level or equipment data."; return false; }
        }

        public void RefreshCheckpoint()
        {
            var valid = ReadCheckpoint(out var checkpoint, out var message);
            SavedCheckpoint = valid ? checkpoint : null;
            Flow.SetContinueAvailable(valid);
            CurrentScreen?.UpdateContinue(SavedCheckpoint);
            CurrentScreen?.ShowSaveStatus(!string.IsNullOrEmpty(saveNotice) ? saveNotice : message);
            CurrentScreen?.SetProfileAvailable(progress != null, profileFailure);
            CurrentScreen?.ShowProfile(Profile);
        }

        private void CommitCheckpoint(RunCheckpoint checkpoint)
        {
            try
            {
                checkpointStore.Save(checkpoint); SavedCheckpoint = checkpoint;
                Flow.SetContinueAvailable(true); saveNotice = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                saveNotice = "Checkpoint could not be saved. Check storage availability and permissions. The previous checkpoint is retained.";
                throw new IOException(saveNotice, exception);
            }
        }

        private void Awake()
        {
            lifetime = destroyCancellationToken;
            DontDestroyOnLoad(gameObject);
            Flow = new ApplicationFlow();
            var saveDirectory = SessionDiagnostics.Option("--save-directory") ?? Application.persistentDataPath;
#if UNITY_EDITOR
            saveDirectory = SaveDirectoryOverride ?? saveDirectory;
#endif
            if(!LocalDiagnostics)
            {
                steam=new SteamClientService(new SteamworksClientApi());
                if(steam.TryStart())
                {
                    restoreBackground=Application.runInBackground;Application.runInBackground=true;
                    string build=string.IsNullOrEmpty(Application.buildGUID)?"editor-"+typeof(ApplicationRoot).Module.ModuleVersionId:Application.buildGUID;
                    lobbies=new SteamLobbyService(new SteamLobbyApi(),build);
                    lobbies.Ready+=SteamSessionReady;lobbies.Failed+=SteamSessionFailed;lobbies.InviteReceived+=SteamInvitation;
                    invitation=SteamLobbyApi.LaunchInvitation();
                }
            }
            try
            {
                accounts=new AccountProfileStore(saveDirectory);
                profileDirectory=SteamReady?accounts.DirectoryFor(steam.AccountId.ToString()):accounts.OfflineDirectory();
                checkpointStore=new RunCheckpointStore(Path.Combine(profileDirectory,"run.save"));
                progress=new ProfileProgressStore(profileDirectory);participants=new SessionParticipantsStore(profileDirectory);
                steamProfileAssociated=SteamReady && accounts.IsAssociated(steam.AccountId.ToString(),Profile.Id);
                if(SteamProfileAssociated)accounts.Select(steam.AccountId.ToString(),Profile.Id);
            }
            catch(Exception exception) when(exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            { profileFailure="Profile could not be loaded safely. Existing progress is preserved. Check storage and permissions."; }
            Flow.StateChanged += PresentState;
        }

        public bool AssociateSteamProfile()
        {
            if(Flow.State!=ApplicationState.MainMenu || !SteamReady || Profile==null || SteamBusy)return false;
            try {accounts.Associate(steam.AccountId.ToString(),Profile.Id);steamProfileAssociated=true;steamNotice="Profile linked to your Steam account. Existing progress is preserved.";RefreshCheckpoint();return true;}
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
            {steamNotice="Profile could not be linked safely. Existing data is preserved. Check storage and retry.";return false;}
        }
        public bool HostSteam(bool resume)
        {
            if(Flow.State!=ApplicationState.MainMenu || !SteamReady || !VerifySteamProfile() || SteamBusy || SteamSessionActive)return false;
            try
            {
                ulong partner=0;
                if(resume)
                {
                    if(!ReadCheckpoint(out var saved,out _) || saved.Mode!=SavedSessionMode.Cooperative)return false;
                    string account=participants.ExpectedPeer(saved.Life.RunId,steam.AccountId.ToString(),Profile.Id,saved.Life.Profiles[1]);
                    if(account!=null && !ulong.TryParse(account,out partner))throw new InvalidDataException("Invalid saved partner account.");
                }
                steamContinue=resume;steamNotice=null;return lobbies.Host(Time.realtimeSinceStartupAsDouble,partner);
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
            {steamNotice="Saved session ownership could not be verified. Existing saves are preserved.";return false;}
        }
        public bool JoinSteamInvitation()
        {
            if(Flow.State!=ApplicationState.MainMenu || !SteamReady || !VerifySteamProfile() || invitation==0 || SteamBusy || SteamSessionActive)return false;
            steamContinue=false;steamNotice=null;return lobbies.Join(invitation,Time.realtimeSinceStartupAsDouble);
        }
        public void InviteSteamFriend()
        {
            if(!CanInviteSteam)return;
            try{lobbies.Invite();}catch(InvalidOperationException){steamNotice="Launch the game through Steam to use its invitation overlay.";}
        }
        public void CancelSteamSession()
        {
            if(Flow.State==ApplicationState.MainMenu){lobbies?.Cancel();pendingSteam=null;steamContinue=false;steamNotice=null;}
            else Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
        }
        private void SteamInvitation(ulong lobby)
        {
            invitation=lobby;steamNotice=Flow.State==ApplicationState.MainMenu?"Steam invitation received.":"Invitation received. Return to Main Menu to join; your current run has not been left.";
            if(Flow.State==ApplicationState.MainMenu && SteamProfileAssociated && !SteamSessionActive && !SteamBusy)JoinSteamInvitation();
        }
        private void SteamSessionReady()
        {
            if(Flow.State!=ApplicationState.MainMenu || !SteamProfileAssociated){lobbies.Cancel();return;}
            pendingSteam=lobbies.Connection;
            pendingSteam.RecordParticipants=(run,host,peer,profile)=>participants.Bind(run,steam.AccountId.ToString(),host,peer.ToString(),profile);
            pendingSessionRole=lobbies.IsHost?SessionRole.Host:SessionRole.Client;
            if(!Flow.TryRequest(steamContinue?ApplicationCommand.Continue:ApplicationCommand.NewGame))lobbies.Cancel();
            invitation=0;
        }
        private void SteamSessionFailed()
        {
            steamNotice=lobbies.Status;
            if(CurrentMultiplayer!=null && pendingSteam!=null)CurrentMultiplayer.EndExternalSession(steamNotice);
        }
        private void Update()
        {
            if(steam==null)return;
            steam.Pump();lobbies?.Tick(Time.realtimeSinceStartupAsDouble);
            if(steam.IsReady && !accountChanged && Time.realtimeSinceStartupAsDouble>=nextIdentityCheck)
            {
                nextIdentityCheck=Time.realtimeSinceStartupAsDouble+1;
                if(!steam.IdentityUnchanged())
                {
                    accountChanged=true;profileFailure="Steam account changed or signed out. Restart the game to load that account's profile.";steamNotice=profileFailure;
                    if(pendingSteam!=null)CurrentMultiplayer?.EndExternalSession(profileFailure);
                    lobbies?.Cancel();
                    if(CurrentGameplay!=null){CurrentGameplay.Input.SetAccountAvailable(false);Flow.TryRequest(ApplicationCommand.Pause);}
                    CurrentScreen?.SetProfileAvailable(false,profileFailure);
                }
                else if(!steam.Online && (SteamBusy || SteamSessionActive))
                {
                    steamNotice="Steam connection lost. The host retains the unfinished run; solo play remains available.";
                    if(pendingSteam!=null)CurrentMultiplayer?.EndExternalSession(steamNotice);
                    lobbies?.Cancel();
                }
            }
        }
        private void BindSteamScreen()
        {CurrentScreen.gameObject.AddComponent<SteamSessionPresenter>().Bind(this,CurrentScreen.View);}

        private void FinishRun(RunEndReceipt receipt)
        {
            if (progress == null) throw new IOException(profileFailure);
            var credited = progress.EndRunWithCredit(receipt);
            if (!runRewards.ContainsKey(receipt.RunId)) runRewards[receipt.RunId] = new RunRewardSummary(receipt,credited);
            LastRunReward = runRewards[receipt.RunId];
            SavedCheckpoint = null; Flow.SetContinueAvailable(false);
        }

        private void SaveUnfinishedRun()
        { CurrentGameplay?.SaveCurrent(); CurrentMultiplayer?.SaveCurrent(); }

        private void OnApplicationQuit()
        {
            try { SaveUnfinishedRun(); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { Debug.LogError("Unfinished run could not be saved: " + exception.Message, this); }
        }

        private void Start() => Flow.TryRequest(ApplicationCommand.Initialize);

        private void OnDestroy()
        {
            if (CurrentMultiplayer != null) CurrentMultiplayer.Stop();
            if (CurrentMultiplayer != null) CurrentMultiplayer.ReleaseSteamTransport();
            lobbies?.Dispose();steam?.Dispose();
            if(restoreBackground.HasValue)Application.runInBackground=restoreBackground.Value;
            if (Flow != null)
                Flow.StateChanged -= PresentState;
        }

        private void PresentState(ApplicationState state)
        {
            if (Debug.isDebugBuild)
                Debug.Log($"[ApplicationFlow] {state}", this);

            if (state == ApplicationState.Loading)
                LoadDestination(Flow.Destination.Value);
            else if (state == ApplicationState.Exiting)
                ExitApplication();
        }

        private async void LoadDestination(ApplicationState destination)
        {
            var sceneLoadStarted = false;
            try
            {
                if (destination == ApplicationState.MainMenu) SaveUnfinishedRun();
                if (destination == ApplicationState.Gameplay && (progress == null || accountChanged)) throw new IOException(profileFailure);
                if (destination == ApplicationState.Gameplay && Flow.PendingCommand == ApplicationCommand.Continue)
                {
                    if (!ReadCheckpoint(out pendingCheckpoint, out var message)) throw new IOException(message);
                    pendingSessionRole = pendingCheckpoint.Mode == SavedSessionMode.Cooperative ? SessionRole.Host : SessionRole.None;
                }
                else if (destination == ApplicationState.Gameplay) pendingCheckpoint = null;
                var scenePath = destination == ApplicationState.MainMenu ? mainMenuScenePath : gameplayScenePath;
                var screenPrefab = destination == ApplicationState.MainMenu ? mainMenuScreen : gameplayScreen;
                if (screenPrefab == null || !Application.CanStreamedLevelBeLoaded(scenePath))
                    throw new InvalidOperationException($"Missing screen or build scene for {destination}: {scenePath}");
                if (destination == ApplicationState.Gameplay && pendingSessionRole == SessionRole.None && (gameplaySession == null || gameplaySession.Rooms == null))
                    throw new InvalidOperationException("Missing room-enabled gameplay session prefab.");
                if (destination == ApplicationState.Gameplay && pendingSessionRole != SessionRole.None && multiplayerSession == null)
                    throw new InvalidOperationException("Missing explicitly authored multiplayer session composition.");
                if(destination==ApplicationState.Gameplay && pendingSteam==null && (SteamBusy || SteamSessionActive))lobbies.Cancel();
                if(destination==ApplicationState.MainMenu){lobbies?.Cancel();pendingSteam=null;steamContinue=false;}
                sceneLoadStarted = true;
                if (CurrentMultiplayer != null)
                { CurrentMultiplayer.Stop(); Destroy(CurrentMultiplayer.gameObject); CurrentMultiplayer = null; }

                var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException($"Could not start loading {scenePath}.");

                await Awaitable.FromAsyncOperation(operation, lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (destination == ApplicationState.Gameplay)
                {
                    if (pendingSessionRole == SessionRole.None)
                    {
                        CurrentGameplay = Instantiate(gameplaySession);
                        CurrentGameplay.Bind(new PlayerIdentity(1), Flow, useLevelSequence: true, checkpoint: pendingCheckpoint,
                            save: CommitCheckpoint, restoredWeapon: pendingCheckpoint != null ? multiplayerSession.ResolveWeapon(pendingCheckpoint.EquipmentIds[0]) : null,
                            profileId: Profile.Id, finish: FinishRun);
                    }
                    else
                    {
                        CurrentGameplay = null;
                        CurrentMultiplayer = Instantiate(multiplayerSession);
                        if(pendingSteam!=null)
                        {
                            if(!SteamReady || lobbies.Connection!=pendingSteam || !SteamSessionActive)throw new InvalidOperationException("Steam session closed while loading.");
                            CurrentMultiplayer.ConfigureSteam(pendingSteam);
                        }
                        var address = SessionDiagnostics.Option("--session-address") ?? "127.0.0.1";
                        var port = ushort.TryParse(SessionDiagnostics.Option("--session-port"), out var requestedPort) ? requestedPort : (ushort)7777;
                        ulong? seed = pendingSessionRole == SessionRole.Host && ulong.TryParse(SessionDiagnostics.Option("--session-seed"), out var requestedSeed) ? requestedSeed : (ulong?)null;
                        CurrentMultiplayer.Bind(pendingSessionRole, Flow, address, port, seed, useLevelSequence: true,
                            checkpoint: pendingCheckpoint, save: CommitCheckpoint, profileId: Profile.Id, finish: FinishRun,
                            pendingRewards: progress.PendingFor, acknowledgeReward: progress.Acknowledge);
                    }
                }
                else { CurrentGameplay = null; pendingSessionRole = SessionRole.None; }
                CurrentScreen = Instantiate(screenPrefab);
                CurrentScreen.Bind(Flow);
                BindSteamScreen();
                CurrentScreen.BindRewards(id => runRewards.TryGetValue(id,out var summary) ? summary : null);
                CurrentScreen.BindContinue(() => RequestContinue());
                CurrentScreen.BindLocalSessionControls(mode => StartMultiplayerSession(mode));
                if (CurrentGameplay != null)
                {
                    CurrentGameplay.Input.SetUiGate(CurrentScreen.IsPointerOverInteractiveUi);
                    CurrentScreen.ShowWeapon(CurrentGameplay.WeaponDisplayName);
                    CurrentScreen.BindRun(CurrentGameplay);
                }
                if (CurrentMultiplayer != null)
                {
                    CurrentMultiplayer.SetUiGate(CurrentScreen.IsPointerOverInteractiveUi);
                    CurrentScreen.BindMultiplayer(CurrentMultiplayer);
                }
                Flow.CompleteLoading(destination);
                pendingCheckpoint = null;
                if (destination == ApplicationState.MainMenu)
                {RefreshCheckpoint();if(invitation!=0 && SteamProfileAssociated && !SteamBusy && !SteamSessionActive)JoinSteamInvitation();}
                var automatic = SessionDiagnostics.Option("--session-role");
                if (destination == ApplicationState.MainMenu && !automaticSessionStarted && (automatic == "host" || automatic == "client"))
                {
                    automaticSessionStarted = true;
                    if (automatic == "host" && SessionDiagnostics.Option("--session-continue") == "true")
                    { if (!RequestContinue()) throw new InvalidOperationException("No compatible cooperative checkpoint is available."); }
                    else StartMultiplayerSession(automatic == "host" ? SessionRole.Host : SessionRole.Client);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(destination==ApplicationState.MainMenu && SessionDiagnostics.Option("--steam-session-validation")!=null
                    && GetComponent<SteamSessionValidationDriver>()==null)gameObject.AddComponent<SteamSessionValidationDriver>().Bind(this);
                if (destination == ApplicationState.MainMenu && SessionDiagnostics.Option("--completion-validation") != null
                    && GetComponent<CompletionValidationDriver>() == null) gameObject.AddComponent<CompletionValidationDriver>().Bind(this);
                if (destination == ApplicationState.MainMenu && SessionDiagnostics.Option("--save-validation") != null
                    && GetComponent<SaveValidationDriver>() == null) gameObject.AddComponent<SaveValidationDriver>().Bind(this);
#endif
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Editor stop / application teardown must not resume an abandoned transition.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (!sceneLoadStarted && destination == ApplicationState.MainMenu && (CurrentGameplay != null || CurrentMultiplayer != null))
                {
                    Flow.FailLoading("Unable to save the unfinished run. Check storage availability before leaving.");
                    CurrentScreen?.ShowSaveStatus(Flow.ErrorMessage);
                    return;
                }
                Flow.FailLoading("Unable to open the requested screen. See the application log.");
                if (CurrentMultiplayer != null) { CurrentMultiplayer.Stop(); Destroy(CurrentMultiplayer.gameObject); CurrentMultiplayer = null; }
                if (CurrentGameplay != null) { Destroy(CurrentGameplay.gameObject); CurrentGameplay = null; }
                pendingSessionRole = SessionRole.None; pendingCheckpoint = null;pendingSteam=null;lobbies?.Cancel();
                try
                {
                    await Awaitable.FromAsyncOperation(SceneManager.LoadSceneAsync(mainMenuScenePath), lifetime);
                    CurrentScreen = Instantiate(mainMenuScreen); CurrentScreen.Bind(Flow);BindSteamScreen();
                CurrentScreen.BindRewards(id => runRewards.TryGetValue(id,out var summary) ? summary : null);
                    CurrentScreen.BindContinue(() => RequestContinue()); CurrentScreen.BindLocalSessionControls(mode => StartMultiplayerSession(mode));
                    RefreshCheckpoint(); CurrentScreen.ShowSaveStatus(saveNotice ?? "The requested run could not be loaded. The previous checkpoint is retained.");
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            }
        }

        private static void ExitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}
