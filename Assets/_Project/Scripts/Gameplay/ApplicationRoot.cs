using System;
using System.Threading;
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

        public ApplicationFlow Flow { get; private set; }
        public ApplicationScreen CurrentScreen { get; private set; }
        public GameplaySession CurrentGameplay { get; private set; }
        public MultiplayerSession CurrentMultiplayer { get; private set; }

        public bool StartMultiplayerSession(SessionRole mode)
        {
            if (!Debug.isDebugBuild || mode == SessionRole.None || Flow.State != ApplicationState.MainMenu) return false;
            pendingSessionRole = mode;
            return Flow.TryRequest(ApplicationCommand.NewGame);
        }

        private void Awake()
        {
            lifetime = destroyCancellationToken;
            DontDestroyOnLoad(gameObject);
            Flow = new ApplicationFlow();
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
                        CurrentGameplay.Bind(new PlayerIdentity(1), Flow);
                    }
                    else
                    {
                        CurrentGameplay = null;
                        CurrentMultiplayer = Instantiate(multiplayerSession);
                        var address = SessionDiagnostics.Option("--session-address") ?? "127.0.0.1";
                        var port = ushort.TryParse(SessionDiagnostics.Option("--session-port"), out var requestedPort) ? requestedPort : (ushort)7777;
                        ulong? seed = pendingSessionRole == SessionRole.Host && ulong.TryParse(SessionDiagnostics.Option("--session-seed"), out var requestedSeed) ? requestedSeed : (ulong?)null;
                        CurrentMultiplayer.Bind(pendingSessionRole, Flow, address, port, seed);
                    }
                }
                else { CurrentGameplay = null; pendingSessionRole = SessionRole.None; }
                CurrentScreen = Instantiate(screenPrefab);
                CurrentScreen.Bind(Flow);
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
                var automatic = SessionDiagnostics.Option("--session-role");
                if (destination == ApplicationState.MainMenu && !automaticSessionStarted && (automatic == "host" || automatic == "client"))
                { automaticSessionStarted = true; StartMultiplayerSession(automatic == "host" ? SessionRole.Host : SessionRole.Client); }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Editor stop / application teardown must not resume an abandoned transition.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Flow.FailLoading("Unable to open the requested screen. See the application log.");
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
