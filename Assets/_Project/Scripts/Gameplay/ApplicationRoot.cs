using System;
using System.Threading;
using System.IO;
using SpaceCrawler.Core;
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
            if (SavedCheckpoint == null) return false;
            pendingCheckpoint = SavedCheckpoint;
            pendingSessionRole = pendingCheckpoint.Mode == SavedSessionMode.Cooperative ? SessionRole.Host : SessionRole.None;
            return Flow.TryRequest(ApplicationCommand.Continue);
        }

        private bool ReadCheckpoint(out RunCheckpoint checkpoint, out string message)
        {
            var status = checkpointStore.Load(out checkpoint, out message);
            if (status != CheckpointLoadStatus.Valid) return false;
            try { multiplayerSession.ValidateCheckpoint(checkpoint); return true; }
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
            checkpointStore = new RunCheckpointStore(Path.Combine(saveDirectory, "run.save"));
            Flow.StateChanged += PresentState;
        }

        private void Start() => Flow.TryRequest(ApplicationCommand.Initialize);

        private void OnDestroy()
        {
            if (CurrentMultiplayer != null) CurrentMultiplayer.Stop();
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
            try
            {
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
                            save: CommitCheckpoint, restoredWeapon: pendingCheckpoint != null ? multiplayerSession.ResolveWeapon(pendingCheckpoint.EquipmentIds[0]) : null);
                    }
                    else
                    {
                        CurrentGameplay = null;
                        CurrentMultiplayer = Instantiate(multiplayerSession);
                        var address = SessionDiagnostics.Option("--session-address") ?? "127.0.0.1";
                        var port = ushort.TryParse(SessionDiagnostics.Option("--session-port"), out var requestedPort) ? requestedPort : (ushort)7777;
                        ulong? seed = pendingSessionRole == SessionRole.Host && ulong.TryParse(SessionDiagnostics.Option("--session-seed"), out var requestedSeed) ? requestedSeed : (ulong?)null;
                        CurrentMultiplayer.Bind(pendingSessionRole, Flow, address, port, seed, useLevelSequence: true,
                            checkpoint: pendingCheckpoint, save: CommitCheckpoint);
                    }
                }
                else { CurrentGameplay = null; pendingSessionRole = SessionRole.None; }
                CurrentScreen = Instantiate(screenPrefab);
                CurrentScreen.Bind(Flow);
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
                if (destination == ApplicationState.MainMenu) RefreshCheckpoint();
                var automatic = SessionDiagnostics.Option("--session-role");
                if (destination == ApplicationState.MainMenu && !automaticSessionStarted && (automatic == "host" || automatic == "client"))
                {
                    automaticSessionStarted = true;
                    if (automatic == "host" && SessionDiagnostics.Option("--session-continue") == "true")
                    { if (!RequestContinue()) throw new InvalidOperationException("No compatible cooperative checkpoint is available."); }
                    else StartMultiplayerSession(automatic == "host" ? SessionRole.Host : SessionRole.Client);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
                Flow.FailLoading("Unable to open the requested screen. See the application log.");
                if (CurrentMultiplayer != null) { CurrentMultiplayer.Stop(); Destroy(CurrentMultiplayer.gameObject); CurrentMultiplayer = null; }
                if (CurrentGameplay != null) { Destroy(CurrentGameplay.gameObject); CurrentGameplay = null; }
                pendingSessionRole = SessionRole.None; pendingCheckpoint = null;
                try
                {
                    await Awaitable.FromAsyncOperation(SceneManager.LoadSceneAsync(mainMenuScenePath), lifetime);
                    CurrentScreen = Instantiate(mainMenuScreen); CurrentScreen.Bind(Flow);
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
