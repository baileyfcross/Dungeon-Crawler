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
        private SpikeMode pendingSpike;
        private bool autoSpikeStarted;

        public ApplicationFlow Flow { get; private set; }
        public ApplicationScreen CurrentScreen { get; private set; }
        public GameplaySession CurrentGameplay { get; private set; }
        public MultiplayerSession CurrentMultiplayer { get; private set; }

        public bool StartSpike(SpikeMode mode)
        {
            if (!Debug.isDebugBuild || mode == SpikeMode.None || Flow.State != ApplicationState.MainMenu) return false;
            pendingSpike = mode;
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
                if (destination == ApplicationState.Gameplay && pendingSpike == SpikeMode.None && (gameplaySession == null || gameplaySession.Rooms == null))
                    throw new InvalidOperationException("Missing room-enabled gameplay session prefab.");
                if (destination == ApplicationState.Gameplay && pendingSpike != SpikeMode.None && multiplayerSession == null)
                    throw new InvalidOperationException("Missing explicitly authored multiplayer spike composition.");
                if (CurrentMultiplayer != null)
                { CurrentMultiplayer.Stop(); Destroy(CurrentMultiplayer.gameObject); CurrentMultiplayer = null; }

                var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException($"Could not start loading {scenePath}.");

                await Awaitable.FromAsyncOperation(operation, lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (destination == ApplicationState.Gameplay)
                {
                    if (pendingSpike == SpikeMode.None)
                    {
                        CurrentGameplay = Instantiate(gameplaySession);
                        CurrentGameplay.Bind(new PlayerIdentity(1), Flow);
                    }
                    else
                    {
                        CurrentGameplay = null;
                        CurrentMultiplayer = Instantiate(multiplayerSession);
                        var address = SpikeDiagnostics.Option("--spike-address") ?? "127.0.0.1";
                        var port = ushort.TryParse(SpikeDiagnostics.Option("--spike-port"), out var requestedPort) ? requestedPort : (ushort)7777;
                        ulong? seed = pendingSpike == SpikeMode.Host && ulong.TryParse(SpikeDiagnostics.Option("--spike-seed"), out var requestedSeed) ? requestedSeed : (ulong?)null;
                        CurrentMultiplayer.Bind(pendingSpike, Flow, address, port, seed);
                    }
                }
                else { CurrentGameplay = null; pendingSpike = SpikeMode.None; }
                CurrentScreen = Instantiate(screenPrefab);
                CurrentScreen.Bind(Flow);
                CurrentScreen.BindSpikeMenu(mode => StartSpike(mode));
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
                var automatic = SpikeDiagnostics.Option("--spike-role");
                if (destination == ApplicationState.MainMenu && !autoSpikeStarted && (automatic == "host" || automatic == "client"))
                { autoSpikeStarted = true; StartSpike(automatic == "host" ? SpikeMode.Host : SpikeMode.Client); }
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
