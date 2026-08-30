using System;

namespace SpaceCrawler.Core
{
    /// <summary>Owns application transition rules; neither input nor presentation sets state.</summary>
    public sealed class ApplicationFlow
    {
        private ApplicationState previousState;

        public ApplicationState State { get; private set; } = ApplicationState.Boot;
        public ApplicationState? Destination { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;
        public event Action<ApplicationState> StateChanged;

        public bool CanRequest(ApplicationCommand command)
        {
            switch (command)
            {
                case ApplicationCommand.Initialize:
                    return State == ApplicationState.Boot;
                case ApplicationCommand.NewGame:
                case ApplicationCommand.Exit:
                    return State == ApplicationState.MainMenu;
                case ApplicationCommand.ReturnToMainMenu:
                    return State == ApplicationState.Gameplay || State == ApplicationState.Paused;
                case ApplicationCommand.Pause:
                    return State == ApplicationState.Gameplay;
                case ApplicationCommand.Resume:
                    return State == ApplicationState.Paused;
                default:
                    return false;
            }
        }

        public bool TryRequest(ApplicationCommand command)
        {
            if (!CanRequest(command))
                return false;

            ErrorMessage = string.Empty;
            if (command == ApplicationCommand.Pause || command == ApplicationCommand.Resume)
            {
                ChangeState(command == ApplicationCommand.Pause ? ApplicationState.Paused : ApplicationState.Gameplay);
                return true;
            }
            if (command == ApplicationCommand.Exit)
            {
                ChangeState(ApplicationState.Exiting);
                return true;
            }

            previousState = State;
            Destination = command == ApplicationCommand.NewGame
                ? ApplicationState.Gameplay
                : ApplicationState.MainMenu;
            ChangeState(ApplicationState.Loading);
            return true;
        }

        public bool CompleteLoading(ApplicationState destination)
        {
            if (State != ApplicationState.Loading || Destination != destination)
                return false;

            Destination = null;
            ChangeState(destination);
            return true;
        }

        public bool FailLoading(string message)
        {
            if (State != ApplicationState.Loading)
                return false;

            Destination = null;
            ErrorMessage = message ?? string.Empty;
            ChangeState(previousState);
            return true;
        }

        private void ChangeState(ApplicationState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
