using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SpaceCrawler.Core;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Two-player session adapter: sender-authenticated requests in, authoritative state out.</summary>
    public sealed class MultiplayerSession : MonoBehaviour
    {
        private const string InputMessage = "session.input.v2", DescriptorMessage = "session.run.v2",
            AckMessage = "session.room-ready.v2", StateMessage = "session.state.v2";
        [SerializeField] private NetworkManager network;
        [SerializeField] private UnityTransport transport;
        [SerializeField] private NetworkPlayerRig playerPrefab;
        [SerializeField] private PlayerCameraFollower follower;
        [SerializeField] private RoomLoader rooms;
        [SerializeField] private RunGenerationDefinition generation;
        [SerializeField] private WeaponDefinition knife, blaster;
        [SerializeField] private bool useBlaster;
        [UnityEngine.Serialization.FormerlySerializedAs("useDevelopmentSeed")]
        [SerializeField] private bool useFixedSeed;
        [UnityEngine.Serialization.FormerlySerializedAs("developmentSeed")]
        [SerializeField] private uint fixedSeed = 42;
        private ApplicationFlow flow;
        private readonly NetworkPlayerRig[] players = new NetworkPlayerRig[2];
        private readonly Dictionary<(int, long), CombatProjectile> projectileViews = new Dictionary<(int, long), CombatProjectile>();
        private NetworkPlayerCommands commands;
        private ulong? remoteSender;
        private bool initializedCatalog, stopped, stopRequested, roomReady, inputRoomReady, priorBackground;
        private double nextInput, nextSnapshot, entryStarted;
        private uint epoch;
        private ulong tick, lastTick;
        private RunRoomId loadedRoom;
        private RunSessionDescriptor descriptor;
        private SessionStateHeader lastHeader;
        private WeaponConfiguration weapon;
        public SessionRole Mode { get; private set; }
        public int LocalPlayer => Mode == SessionRole.Host ? 1 : 2;
        public NetworkManager Network => network;
        public DungeonGraph Run { get; private set; }
        public string Fingerprint { get; private set; }
        public RunProgression Progression { get; private set; }
        public SessionAuthority Authority { get; private set; }
        public ExitReadiness Readiness { get; } = new ExitReadiness();
        public IReadOnlyList<NetworkPlayerRig> Players => players;
        public RoomLoader Rooms => rooms;
        public PlayerCameraFollower Follower => follower;
        public bool RoomReady => roomReady;
        public uint Epoch => epoch;
        public RunRoomId CurrentRoom => Mode == SessionRole.Host ? Progression.CurrentRoomId : new RunRoomId(lastHeader.CurrentRoom);
        public int ReplicaProjectileCount => projectileViews.Count;
        public IPlayerCommands LocalCommands => commands;
        // Integration tests may supply intentions at the same boundary instead of polling devices.
        public bool PollLocalDevices { get; set; } = true;
        public string Failure { get; private set; }
        public string WeaponName => weapon?.Id ?? "Waiting";
        public event Action Changed;

        public void Bind(SessionRole mode, ApplicationFlow localFlow, string address = "127.0.0.1", ushort port = 7777, ulong? hostSeed = null)
        {
            if (flow != null || mode == SessionRole.None) throw new InvalidOperationException("Invalid session binding.");
            Mode = mode; flow = localFlow; priorBackground = Application.runInBackground; Application.runInBackground = true;
            weapon = (useBlaster || (Debug.isDebugBuild && SessionDiagnostics.Option("--session-weapon") == "blaster") ? blaster : knife).CreateConfiguration();
            transport.SetConnectionData(address, port, mode == SessionRole.Host ? address : null);
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.EnableSceneManagement = false;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            network.NetworkConfig.PlayerPrefab = null;
            network.NetworkConfig.TickRate = 30;
            network.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes("SpaceCrawlerSession2");
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
            network.OnTransportFailure += TransportFailed;
            if (mode == SessionRole.Host)
            {
                Run = new RunGenerator().Generate(hostSeed ?? (useFixedSeed ? fixedSeed : CreateSeed()), generation.CreateConfiguration());
                Fingerprint = GraphFingerprint.Compute(Run);
                Progression = new RunProgression(Run, new PlayerIdentity(1));
                Progression.SetGameplayActive(true);
                CreatePlayers();
                Authority = new SessionAuthority(NetworkManager.ServerClientId, players[0].Simulation, players[1].Simulation,
                    (i, intent) => players[i].Combat.RequestAttack(intent));
                epoch = 1;
                LoadHostRoom(Run.StartRoomId);
                players[1].gameObject.SetActive(false);
            }
            var started = mode == SessionRole.Host ? network.StartHost() : network.StartClient();
            if (!started) throw new InvalidOperationException("Could not start the direct session connection.");
            network.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            network.CustomMessagingManager.RegisterNamedMessageHandler(DescriptorMessage, ReceiveDescriptor);
            network.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveAck);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            Debug.Log($"[Session] role={Mode} localPlayer={LocalPlayer} endpoint={address}:{port}; waiting for two-player room agreement", this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SessionDiagnostics.Option("--session-validation") == "run") gameObject.AddComponent<SessionValidationDriver>().Bind(this, flow);
#endif
        }

        private static ulong CreateSeed()
        {
            var bytes = new byte[8]; using (var source = RandomNumberGenerator.Create()) source.GetBytes(bytes);
            ulong value = 0; for (var i = 0; i < bytes.Length; i++) value |= (ulong)bytes[i] << (i * 8); return value;
        }

        private void CreatePlayers()
        {
            for (var i = 0; i < 2; i++)
            {
                players[i] = Instantiate(playerPrefab, transform);
                players[i].Bind(new PlayerIdentity(i + 1), Mode == SessionRole.Host, i + 1 == LocalPlayer, weapon);
            }
            if (Mode == SessionRole.Host)
            {
                foreach (var a in players[0].Colliders) foreach (var b in players[1].Colliders) Physics2D.IgnoreCollision(a, b, true);
                foreach (var rig in players) foreach (var other in players) rig.Combat.IgnorePlayerColliders(other.Colliders);
            }
            var local = players[LocalPlayer - 1];
            follower.Bind(local.Motor.transform);
            commands = new NetworkPlayerCommands(() => local.State, () => epoch, SendInput);
            local.Input.SetRoomAvailable(false);
            local.Input.Bind(commands, flow);
        }

        public void SetUiGate(Func<Vector2, bool> gate)
        { if (players[LocalPlayer - 1] != null) players[LocalPlayer - 1].Input.SetUiGate(gate); uiGate = gate; }
        private Func<Vector2, bool> uiGate;

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = Encoding.ASCII.GetString(request.Payload) == "SpaceCrawlerSession2"
                && (request.ClientNetworkId == NetworkManager.ServerClientId || network.ConnectedClientsIds.Count < 2);
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (!response.Approved) response.Reason = "The local session requires one host and one client with the same protocol.";
        }

        private void Connected(ulong sender)
        {
            if (Mode != SessionRole.Host || sender == NetworkManager.ServerClientId) return;
            if (!Authority.Connect(sender)) { network.DisconnectClient(sender); return; }
            remoteSender = sender;
            players[1].gameObject.SetActive(true);
            // IgnoreCollision state can reset when a collider is deactivated.
            foreach (var a in players[0].Colliders) foreach (var b in players[1].Colliders) Physics2D.IgnoreCollision(a, b, true);
            Readiness.Reset(loadedRoom, Progression.ConnectionsFrom(loadedRoom), true);
            entryStarted = Time.realtimeSinceStartupAsDouble;
            SendDescriptor();
            Debug.Log($"[Session] sender={sender} mapped to PlayerIdentity 2; host sender=0 maps to 1", this);
        }

        private void LoadHostRoom(RunRoomId destination)
        {
            SetRoomReady(false);
            Authority?.SetRunning(false, epoch);
            foreach (var rig in players) { rig.Simulation.SetActive(false); rig.ClearRoom(); rig.Motor.Body.simulated = false; }
            if (!initializedCatalog) { rooms.InitializeCatalog(Run, generation, true); initializedCatalog = true; }
            rooms.LoadSharedRoom(Run, destination, Progression.ConnectionsFrom(destination), ObserveExit);
            for (var i = 0; i < 2; i++)
            {
                players[i].Combat.RegisterRoomTargets(rooms.CurrentRoom.Targets, i == 0);
                var anchor = i == 0 ? rooms.CurrentRoom.PlayerOneSpawnAnchor : rooms.CurrentRoom.PlayerTwoSpawnAnchor;
                players[i].Motor.PlaceAt(anchor.position); players[i].Motor.Body.simulated = true;
            }
            loadedRoom = destination;
            Physics2D.SyncTransforms(); follower.Follow();
            Readiness.Reset(destination, Progression.ConnectionsFrom(destination), Authority != null && Authority.BothConnected);
            entryStarted = Time.realtimeSinceStartupAsDouble;
        }

        private void ObserveExit(RoomConnection edge, Collider2D collider, bool present)
        {
            if (!roomReady || Authority == null || !Authority.BothConnected || collider == null) return;
            for (var i = 0; i < 2; i++)
                if (collider.attachedRigidbody == players[i].Motor.Body)
                    Readiness.SetReady(new ExitIntent(new PlayerIdentity(i + 1), edge.From, edge.To), present);
        }

        private void BeginTransition(RunRoomId destination)
        {
            if (!Progression.TryRequestExit(new ExitIntent(Progression.Player, Progression.CurrentRoomId, destination))) return;
            epoch++;
            if (!Progression.TryBeginAcceptedEntry()) { Fail("Accepted shared room entry failed."); return; }
            LoadHostRoom(destination);
            SendDescriptor();
        }

        private void SendDescriptor()
        {
            if (!remoteSender.HasValue) return;
            descriptor = new RunSessionDescriptor { generationVersion = Run.GenerationVersion, seed = Run.Seed.ToString(), fingerprint = Fingerprint,
                epoch = epoch, currentRoom = Progression.CurrentRoomId.Value, destination = loadedRoom.Value,
                state = (int)Progression.State, weapon = weapon.Id };
            SendJson(DescriptorMessage, remoteSender.Value, JsonUtility.ToJson(descriptor));
        }

        private void ReceiveDescriptor(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Client || sender != NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out string json);
                var incoming = JsonUtility.FromJson<RunSessionDescriptor>(json);
                if (incoming.protocol != 2 || incoming.epoch <= epoch) return;
                var graph = Run ?? new RunGenerator().Generate(ulong.Parse(incoming.seed), generation.CreateConfiguration(), incoming.generationVersion);
                if (graph.Seed.ToString() != incoming.seed || graph.GenerationVersion != incoming.generationVersion || GraphFingerprint.Compute(graph) != incoming.fingerprint)
                    throw new InvalidOperationException("Graph fingerprint/descriptor mismatch.");
                if (incoming.weapon != knife.CreateConfiguration().Id && incoming.weapon != blaster.CreateConfiguration().Id)
                    throw new InvalidOperationException("Unknown session weapon definition.");
                if (Run != null && incoming.weapon != weapon.Id) throw new InvalidOperationException("Equipment cannot change during this session.");
                Run = graph; Fingerprint = incoming.fingerprint; descriptor = incoming; epoch = incoming.epoch;
                weapon = (incoming.weapon == knife.CreateConfiguration().Id ? knife : blaster).CreateConfiguration();
                if (players[0] == null) { CreatePlayers(); SetUiGate(uiGate); }
                SetRoomReady(false); ClearReplicaProjectiles();
                foreach (var rig in players) rig.ClearRoom();
                if (!initializedCatalog) { rooms.InitializeCatalog(Run, generation, true); initializedCatalog = true; }
                loadedRoom = new RunRoomId(incoming.destination);
                var choices = Run.Connections.Where(c => c.From.Equals(loadedRoom)).ToArray();
                rooms.LoadSharedRoom(Run, loadedRoom, choices, null);
                foreach (var target in rooms.CurrentRoom.Targets) target.PresentAuthoritativeHealth(target.InitialHealth, target.InitialHealth);
                SendJson(AckMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(new RoomLoadAcknowledgment { epoch = epoch, room = loadedRoom.Value, fingerprint = Fingerprint }));
                Debug.Log($"[Session] client verified seed={Run.Seed} version={Run.GenerationVersion} fingerprint={Fingerprint} loaded={loadedRoom} epoch={epoch}", this);
                Changed?.Invoke();
            }
            catch (Exception exception) { Fail("Rejected host descriptor: " + exception.Message); }
        }

        private void ReceiveAck(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Host || !remoteSender.HasValue || sender != remoteSender.Value) return;
            try
            {
                reader.ReadValueSafe(out string json); var ack = JsonUtility.FromJson<RoomLoadAcknowledgment>(json);
                if (ack.epoch != epoch || ack.room != loadedRoom.Value) return;
                if (ack.fingerprint != Fingerprint) { Fail("Client graph fingerprint did not agree."); return; }
                if (roomReady) return;
                if (!Progression.TryCompleteEntry(loadedRoom)) { Fail("Core rejected shared room completion."); return; }
                Authority.SetRunning(true, epoch); SetRoomReady(true);
                Debug.Log($"[Session] BOTH READY seed={Run.Seed} fingerprint={Fingerprint} room={Progression.CurrentRoomId} state={Progression.State} epoch={epoch} entered={Progression.RoomsEntered}", this);
                SendState();
            }
            catch (Exception exception) { Fail("Rejected room acknowledgement: " + exception.Message); }
        }

        private void SetRoomReady(bool ready)
        {
            roomReady = ready;
            if (inputRoomReady != ready && players[LocalPlayer - 1] != null)
            { inputRoomReady = ready; players[LocalPlayer - 1].Input.SetRoomAvailable(ready); }
            Changed?.Invoke();
        }

        private void SendInput(PlayerInputMessage input, bool reliable)
        {
            if (stopped) return;
            if (Mode == SessionRole.Host) { Authority?.Submit(NetworkManager.ServerClientId, input, Time.realtimeSinceStartupAsDouble); return; }
            if (!network.IsConnectedClient) return;
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(input);
                network.CustomMessagingManager.SendNamedMessage(InputMessage, NetworkManager.ServerClientId, writer,
                    reliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.UnreliableSequenced);
            }
        }

        private void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Host) return;
            try { reader.ReadValueSafe(out PlayerInputMessage input); Authority.Submit(sender, input, Time.realtimeSinceStartupAsDouble); }
            catch (Exception) { Debug.Log("[Session] Rejected malformed input packet.", this); }
        }

        private void SendJson(string name, ulong receiver, string json)
        {
            using (var writer = new FastBufferWriter(4096, Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                network.CustomMessagingManager.SendNamedMessage(name, receiver, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void SendState()
        {
            if (!remoteSender.HasValue || !network.IsListening) return;
            var choices = Progression.Choices;
            var header = new SessionStateHeader { Epoch = epoch, Tick = ++tick, CurrentRoom = Progression.CurrentRoomId.Value,
                State = (int)Progression.State, RoomsEntered = Progression.RoomsEntered, Running = roomReady,
                LeftReady = choices.Count > 0 ? Readiness.Count(choices[0].To) : 0,
                RightReady = choices.Count > 1 ? Readiness.Count(choices[1].To) : 0 };
            lastHeader = header;
            using (var writer = new FastBufferWriter(2048, Allocator.Temp))
            {
                writer.WriteValueSafe(header);
                for (var i = 0; i < 2; i++) writer.WriteValueSafe(players[i].Capture(Authority.InputEnabled(i)));
                writer.WriteValueSafe(rooms.CurrentRoom.Targets.Count);
                foreach (var target in rooms.CurrentRoom.Targets)
                    writer.WriteValueSafe(new TargetStateSnapshot { Identity = target.Identity.Value, Current = target.Health.Current, Maximum = target.Health.Maximum });
                var projectiles = players.Sum(p => p.Combat.Projectiles.Count);
                writer.WriteValueSafe(projectiles);
                foreach (var rig in players) foreach (var projectile in rig.Combat.Projectiles)
                    writer.WriteValueSafe(new ProjectileStateSnapshot { Owner = rig.State.Player.Value, Sequence = projectile.Execution.Sequence, Position = projectile.Position });
                network.CustomMessagingManager.SendNamedMessage(StateMessage, remoteSender.Value, writer, NetworkDelivery.UnreliableSequenced);
            }
            Changed?.Invoke();
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Client || sender != NetworkManager.ServerClientId || Run == null) return;
            try
            {
                reader.ReadValueSafe(out SessionStateHeader header);
                if (header.Epoch != epoch || header.Tick <= lastTick || header.CurrentRoom != loadedRoom.Value || !header.Running) return;
                lastTick = header.Tick; lastHeader = header;
                for (var i = 0; i < 2; i++) { reader.ReadValueSafe(out PlayerStateSnapshot state); players[i].Apply(state); }
                reader.ReadValueSafe(out int targets);
                if (targets != rooms.CurrentRoom.Targets.Count) throw new InvalidOperationException("Target descriptor count mismatch.");
                for (var i = 0; i < targets; i++)
                {
                    reader.ReadValueSafe(out TargetStateSnapshot target);
                    if (rooms.CurrentRoom.Targets[i].Identity.Value != target.Identity) throw new InvalidOperationException("Target identity mismatch.");
                    rooms.CurrentRoom.Targets[i].PresentAuthoritativeHealth(target.Current, target.Maximum);
                }
                reader.ReadValueSafe(out int projectileCount);
                if (projectileCount < 0 || projectileCount > 32) throw new InvalidOperationException("Invalid projectile count.");
                var present = new HashSet<(int, long)>();
                for (var i = 0; i < projectileCount; i++)
                {
                    reader.ReadValueSafe(out ProjectileStateSnapshot projectile);
                    if (projectile.Owner != 1 && projectile.Owner != 2) throw new InvalidOperationException("Unknown projectile owner.");
                    var key = (projectile.Owner, projectile.Sequence); present.Add(key);
                    if (!projectileViews.TryGetValue(key, out var view))
                    {
                        view = Instantiate(playerPrefab.Combat.ProjectilePrefab, transform); view.enabled = false;
                        projectileViews.Add(key, view);
                    }
                    view.transform.position = projectile.Position;
                }
                foreach (var key in projectileViews.Keys.Where(k => !present.Contains(k)).ToArray())
                { Destroy(projectileViews[key].gameObject); projectileViews.Remove(key); }
                SetRoomReady(true); Changed?.Invoke();
            }
            catch (Exception exception) { Fail("Rejected authoritative state: " + exception.Message); }
        }

        private void Update()
        {
            if (stopped || flow == null) return;
            if (stopRequested) { Stop(); flow.TryRequest(ApplicationCommand.ReturnToMainMenu); return; }
            var now = Time.realtimeSinceStartupAsDouble;
            var local = players[LocalPlayer - 1];
            if (PollLocalDevices && local != null) local.Input.SampleMovement();
            if (commands != null && now >= nextInput) { nextInput = now + 1.0 / 30; commands.SendMovement(); }
            if (Debug.isDebugBuild) SessionDiagnostics.Record(this, now);
            if (Mode != SessionRole.Host) return;
            Authority.Advance(now);
            if (roomReady && Readiness.TryGetSharedDestination(out var destination)) BeginTransition(destination);
            if (!roomReady && remoteSender.HasValue && now - entryStarted > 10) Fail("Room agreement timed out.");
        }

        private void LateUpdate()
        {
            if (stopped || players[0] == null) return;
            if (Mode == SessionRole.Host) foreach (var rig in players) rig.Motor.CaptureState();
            follower.Follow();
            var local = players[LocalPlayer - 1];
            if (PollLocalDevices)
            {
                local.Input.SampleAim(follower.GameplayCamera, local.Motor.transform.position);
                local.Input.SampleAttack();
            }
            foreach (var rig in players) rig.Present();
            if (Mode == SessionRole.Host && Time.realtimeSinceStartupAsDouble >= nextSnapshot)
            { nextSnapshot = Time.realtimeSinceStartupAsDouble + 0.05; SendState(); }
        }

        public string Describe()
        {
            var text = new StringBuilder($"Role={Mode}; localPlayer={LocalPlayer}; connected={network.IsConnectedClient}; ready={roomReady}; epoch={epoch}; failure={Failure}\n");
            if (Run == null) return text.ToString();
            text.AppendLine($"Seed={Run.Seed}; version={Run.GenerationVersion}; fingerprint={Fingerprint}; loaded={loadedRoom}; room={(Mode == SessionRole.Host ? Progression.CurrentRoomId.Value : lastHeader.CurrentRoom)}; state={(Mode == SessionRole.Host ? Progression.State : (RoomFlowState)lastHeader.State)}; entered={(Mode == SessionRole.Host ? Progression.RoomsEntered : lastHeader.RoomsEntered)}; roomChildren={rooms.RoomContainer.childCount}");
            var choices = Run.Connections.Where(c => c.From.Equals(loadedRoom)).ToArray();
            for (var i = 0; i < choices.Length; i++)
                text.AppendLine($"Exit {(choices.Length == 1 ? "CENTER" : i == 0 ? "LEFT" : "RIGHT")} -> {choices[i].To}: {(Mode == SessionRole.Host ? Readiness.Count(choices[i].To) : i == 0 ? lastHeader.LeftReady : lastHeader.RightReady)}/2 ready");
            if (Run.GetRoom(loadedRoom).IsTerminal) text.AppendLine("Terminal room reached. No forward exits.");
            foreach (var rig in players)
            {
                var state = rig.State;
                text.AppendLine($"P{state.Player.Value}: position={state.Position}; velocity={state.Velocity}; aim={state.AimDirection}; attacks={(Mode == SessionRole.Host ? rig.Combat.Simulation.AcceptedAttackCount : rig.LastSnapshot.Attacks)}; cooldown={(Mode == SessionRole.Host ? rig.Combat.Simulation.CooldownRemaining : rig.LastSnapshot.Cooldown)}; input={(Mode == SessionRole.Host ? Authority.InputEnabled(state.Player.Value - 1) : rig.LastSnapshot.InputEnabled)}");
            }
            if (rooms.CurrentRoom != null) foreach (var target in rooms.CurrentRoom.Targets)
                text.AppendLine($"Target {target.Identity.Value}: health={(Mode == SessionRole.Host ? target.Health.Current : target.DisplayedHealth)}");
            text.AppendLine($"Projectiles={(Mode == SessionRole.Host ? players.Sum(p => p.Combat.Projectiles.Count) : projectileViews.Count)}; localPaused={flow.State == ApplicationState.Paused}; camera={follower.transform.position}; rotation={follower.transform.eulerAngles}");
            if (Authority != null) text.AppendLine($"OwnershipRejected={Authority.RejectedOwnership}; staleRejected={Authority.RejectedStale}; inputTimeouts={Authority.TimedOut}");
            return text.ToString();
        }

        private void ClearReplicaProjectiles()
        { foreach (var view in projectileViews.Values) if (view != null) Destroy(view.gameObject); projectileViews.Clear(); }
        private void Disconnected(ulong sender)
        {
            if (stopped) return;
            // A refused third connection is not the accepted partner leaving this run.
            if (Mode == SessionRole.Host && (!remoteSender.HasValue || sender != remoteSender.Value)) return;
            Authority?.Disconnect(); Readiness.Disconnect();
            if (players[1] != null) players[1].gameObject.SetActive(false);
            Fail("Peer disconnected; two-player session stopped safely.");
        }
        private void TransportFailed() => Fail("Direct transport failed.");
        private void Fail(string reason)
        { if (stopRequested || stopped) return; Failure = reason; stopRequested = true; Debug.Log("[Session] " + reason, this); Changed?.Invoke(); }
        public void Stop()
        {
            if (stopped) return;
            if (flow == null) return;
            stopped = true; Authority?.Disconnect(); Readiness.Disconnect();
            foreach (var rig in players) if (rig != null) { rig.Input.SetRoomAvailable(false); rig.ClearRoom(); }
            ClearReplicaProjectiles();
            network.OnClientConnectedCallback -= Connected; network.OnClientDisconnectCallback -= Disconnected;
            network.OnTransportFailure -= TransportFailed;
            if (network.CustomMessagingManager != null)
                foreach (var name in new[] { InputMessage, DescriptorMessage, AckMessage, StateMessage }) network.CustomMessagingManager.UnregisterNamedMessageHandler(name);
            if (network.IsListening) network.Shutdown();
            Application.runInBackground = priorBackground;
        }
        private void OnDestroy() => Stop();
    }
}
