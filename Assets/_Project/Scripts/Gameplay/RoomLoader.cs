using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Executes only authority-accepted room changes inside the persistent Gameplay scene.</summary>
    public sealed class RoomLoader : MonoBehaviour
    {
        [SerializeField] private Transform roomContainer;
        private readonly Dictionary<string, AuthoredRoom> prefabs = new Dictionary<string, AuthoredRoom>(StringComparer.Ordinal);
        private GameplaySession session;
        private ApplicationFlow flow;
        private uint encounterIncarnation;
        public Transform RoomContainer => roomContainer;
        public AuthoredRoom CurrentRoom { get; private set; }
        public AuthoredRoom CurrentPrefab { get; private set; }
        public RunProgression Progression { get; private set; }
        public event Action RoomChanged;

        public void Bind(GameplaySession gameplaySession, ApplicationFlow applicationFlow, RunGenerationDefinition definitions, RunCheckpoint checkpoint = null)
        {
            if (session != null) throw new InvalidOperationException("Room loader is already bound.");
            session = gameplaySession;
            flow = applicationFlow;
            session.Input.SetRoomAvailable(false);
            session.Motor.Stop();
            InitializeCatalog(session.Run, definitions, false);
            Progression = checkpoint == null ? new RunProgression(session.Run, session.Simulation.State.Player)
                : RunProgression.Restore(session.Run, session.Simulation.State.Player, checkpoint.RoomVisits);
            flow.StateChanged += ApplyApplicationState;
            ApplyApplicationState(flow.State);
            EnterRoom(Progression.CurrentRoomId);
        }

        public void InitializeCatalog(DungeonGraph graph, RunGenerationDefinition definitions, bool multiplayer)
        {
            if (prefabs.Count != 0) throw new InvalidOperationException("Room catalog is already initialized.");
            if (roomContainer == null || roomContainer.childCount != 0 || !roomContainer.IsChildOf(transform))
                throw new InvalidOperationException("An empty explicit room container under the session is required.");
            // Validate every explicit mapping before removing or instantiating any physical room.
            foreach (var definition in definitions.RoomDefinitions)
            {
                if (definition == null || definition.RoomPrefab == null) throw new InvalidOperationException("Missing authored room definition/prefab.");
                var snapshot = definition.CreateConfiguration();
                if (prefabs.ContainsKey(snapshot.Id)) throw new InvalidOperationException("Duplicate room prefab mapping: " + snapshot.Id);
                definition.RoomPrefab.ValidateAuthoring();
                if (multiplayer) definition.RoomPrefab.ValidateMultiplayerAuthoring();
                prefabs.Add(snapshot.Id, definition.RoomPrefab);
            }
            if (prefabs.Count != graph.Configuration.Definitions.Count) throw new InvalidOperationException("Room catalog does not match the generated configuration.");
            foreach (var definition in graph.Configuration.Definitions)
                if (!prefabs.ContainsKey(definition.Id)) throw new InvalidOperationException("Missing generated definition mapping: " + definition.Id);
        }

        // Called by host orchestration or by a validated authoritative descriptor on the client.
        // The same explicit mapping/replacement is used; no client progression model is advanced.
        public void LoadSharedRoom(DungeonGraph graph, RunRoomId destination, IReadOnlyList<RoomConnection> choices,
            Action<RoomConnection, Collider2D, bool> presence)
        {
            ReplaceRoom(graph, destination);
            CurrentRoom.ValidateMultiplayerAuthoring();
            CurrentRoom.ConfigureSharedExits(choices, presence);
        }

        private void ReplaceRoom(DungeonGraph graph, RunRoomId destination)
        {
            RemoveCurrentRoom();
            CurrentPrefab = prefabs[graph.GetRoom(destination).DefinitionId];
            CurrentRoom = Instantiate(CurrentPrefab, roomContainer);
            CurrentRoom.ValidateAuthoring();
        }

        private void ApplyApplicationState(ApplicationState state) => Progression.SetGameplayActive(state == ApplicationState.Gameplay);

        public bool RequestExit(ExitIntent intent)
        {
            if (Progression == null || !session.Simulation.IsActive || !Progression.TryRequestExit(intent)) return false;
            EnterRoom(Progression.AcceptedDestination.Value);
            return true;
        }

        private void EnterRoom(RunRoomId destination)
        {
            session.Input.SetRoomAvailable(false);
            session.Motor.Stop();
            session.Motor.Body.simulated = false;
            try
            {
                if (Progression.State == RoomFlowState.LeavingRoom && !Progression.TryBeginAcceptedEntry())
                    throw new InvalidOperationException("Accepted transition cannot enter its destination.");
                session.Combat.ClearRoom();
                var node = session.Run.GetRoom(destination);
                ReplaceRoom(session.Run, destination);
                CurrentRoom.InitializeEncounter(true, checked(++encounterIncarnation));
                var sourceRoom = CurrentRoom;
                CurrentRoom.ConfigureExits(Progression.ConnectionsFrom(destination), Progression.Player, session.Motor.Body,
                    intent => ReferenceEquals(CurrentRoom, sourceRoom) && RequestExit(intent));
                session.Combat.RegisterRoomTargets(CurrentRoom.CombatTargets);
                session.Motor.PlaceAt(CurrentRoom.PlayerSpawnAnchor.position);
                session.Motor.Body.simulated = true;
                Physics2D.SyncTransforms();
                session.Motor.CaptureState();
                session.Follower.Follow();
                if (!Progression.TryCompleteEntry(destination)) throw new InvalidOperationException("Room entry does not match the accepted destination.");
                if (session.TryAdvanceLevel(Progression))
                {
                    Progression = new RunProgression(session.Run, session.Simulation.State.Player);
                    ApplyApplicationState(flow.State);
                    EnterRoom(session.Run.StartRoomId);
                    return;
                }
                session.CommitCheckpoint(Progression);
                session.Input.SetRoomAvailable(true);
                if (Debug.isDebugBuild)
                    Debug.Log($"[RoomFlow] room={destination} definition={node.DefinitionId} state={Progression.State} choices={Progression.Choices.Count} spawn={session.Motor.Body.position} entered={Progression.RoomsEntered}", this);
                RoomChanged?.Invoke();
            }
            catch (Exception exception)
            {
                session.Combat.ClearRoom();
                RemoveCurrentRoom();
                session.Motor.Stop();
                Progression.Fail(exception.Message);
                RoomChanged?.Invoke();
                throw;
            }
        }

        private void RemoveCurrentRoom()
        {
            if (CurrentRoom == null) return;
            CurrentRoom.RetireEncounter();
            // Unity destroys at end-of-frame. Remove all physics/presentation immediately and
            // detach the retired object so RoomContainer contains only the new current instance.
            CurrentRoom.gameObject.SetActive(false);
            CurrentRoom.transform.SetParent(null);
            Destroy(CurrentRoom.gameObject);
            CurrentRoom = null;
            CurrentPrefab = null;
        }

        private void OnDestroy() { if (flow != null) flow.StateChanged -= ApplyApplicationState; }
    }
}
