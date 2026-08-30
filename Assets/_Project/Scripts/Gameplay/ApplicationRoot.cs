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
        private CancellationToken lifetime;

        public ApplicationFlow Flow { get; private set; }
        public ApplicationScreen CurrentScreen { get; private set; }
        public GameplaySession CurrentGameplay { get; private set; }

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
                if (destination == ApplicationState.Gameplay && gameplaySession == null)
                    throw new InvalidOperationException("Missing gameplay session prefab.");

                var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                if (operation == null)
                    throw new InvalidOperationException($"Could not start loading {scenePath}.");

                await Awaitable.FromAsyncOperation(operation, lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (destination == ApplicationState.Gameplay)
                {
                    CurrentGameplay = Instantiate(gameplaySession);
                    CurrentGameplay.Bind(new PlayerIdentity(1), Flow);
                }
                else CurrentGameplay = null;
                CurrentScreen = Instantiate(screenPrefab);
                CurrentScreen.Bind(Flow);
                Flow.CompleteLoading(destination);
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
