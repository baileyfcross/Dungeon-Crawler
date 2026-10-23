using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SpaceCrawler.Core;
using SpaceCrawler.Platform;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Two-player session adapter: sender-authenticated requests in, authoritative state out.</summary>
    public sealed class MultiplayerSession : MonoBehaviour
    {
        private const string InputMessage = "session.input.v8", DescriptorMessage = "session.run.v8",
            AckMessage = "session.room-ready.v8", StateMessage = "session.state.v8",
            EndMessage = "session.end.v8", RewardAckMessage = "session.reward-ready.v8";
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
        private RunSequence sequence;
        private EnemyCombatSystem enemies;
        private RunWorldPersistence persistence;
        private int arrivalPort = -1;
        private long savedAttackCount;
        private Action<RunCheckpoint> commitCheckpoint;
        private Action<RunEndReceipt> endRun;
        private Func<Guid, IReadOnlyList<RunEndReceipt>> pendingRewards;
        private Action<Guid, Guid> acknowledgeReward;
        private Guid profileId, peerProfileId, runId;
        private double nextRewards;
        public RunLife Life { get; private set; }
        public long RunXp => Mode == SessionRole.Host ? Life?.RunXp ?? 0 : lastHeader.RunXp;
        public bool RunEnded { get; private set; }
        public RunCompletion Completion { get; private set; }
        public RunEndReceipt Outcome { get; private set; }
        private bool completionPeerAcknowledged, completedPeerDisconnected;
        private RunEndReceipt pendingOutcome;
        public bool CanLeaveCompletion => Mode == SessionRole.Client || completionPeerAcknowledged || completedPeerDisconnected;
        public string CompletionHint => (Mode == SessionRole.Host ? Completion?.Available == true : lastHeader.CompletionAvailable)
            ? $"RUN EXIT — both players alive and near the gold marker. Press E / gamepad South to confirm. {(Mode == SessionRole.Host ? Completion.Confirmations : lastHeader.CompletionConfirmations)} / 2 ready. Backtracking remains available." : string.Empty;
        public string HealthFailure { get; private set; }
        public PlayerVitals Vitals(int slot) => players[slot].Vitals;
        public RunSequence Sequence => Mode == SessionRole.Host ? sequence : null;
        public int LevelNumber => Mode == SessionRole.Host ? sequence?.LevelNumber ?? 1 : descriptor?.level ?? 1;
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
        public string WeaponDisplayName => weapon == null ? "Waiting" : ResolveWeapon(weapon.Id).DisplayName;
        public event Action Changed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int EnemyFixtureCount { get; set; }
#endif

        private void InitializeEncounter(bool authoritative, bool terminal)
        {
            IReadOnlyList<EnemySpawn> spawns = terminal ? Array.Empty<EnemySpawn>() : null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (EnemyFixtureCount > 0 || SessionDiagnostics.Option("--session-validation") == "enemies")
                spawns = SessionValidationDriver.CreateEnemySpawns(rooms.CurrentRoom, EnemyFixtureCount > 0 ? EnemyFixtureCount : 1);
#endif
            rooms.CurrentRoom.InitializeEncounter(authoritative, epoch, spawns);
        }

        private SteamSessionConnection steam;
        public void ConfigureSteam(SteamSessionConnection connection)
        { if(flow!=null || connection==null)throw new InvalidOperationException("Configure Steam before binding the session.");steam=connection; }

        public void Bind(SessionRole mode, ApplicationFlow localFlow, string address = "127.0.0.1", ushort port = 7777, ulong? hostSeed = null, bool useLevelSequence = false,
            RunCheckpoint checkpoint = null, Action<RunCheckpoint> save = null, Guid? profileId = null,
            Action<RunEndReceipt> finish = null, Func<Guid, IReadOnlyList<RunEndReceipt>> pendingRewards = null, Action<Guid, Guid> acknowledgeReward = null)
        {
            if (flow != null || mode == SessionRole.None) throw new InvalidOperationException("Invalid session binding.");
            Mode = mode; flow = localFlow; priorBackground = Application.runInBackground; Application.runInBackground = true;
            weapon = (useBlaster || (Debug.isDebugBuild && SessionDiagnostics.Option("--session-weapon") == "blaster") ? blaster : knife).CreateConfiguration();
            commitCheckpoint = mode == SessionRole.Host ? save : null;
            persistence = new RunWorldPersistence(checkpoint?.World);
            this.profileId = profileId ?? (checkpoint != null && checkpoint.Life.Profiles[0] != Guid.Empty ? checkpoint.Life.Profiles[0] : Guid.NewGuid());
            endRun = finish; this.pendingRewards = pendingRewards; this.acknowledgeReward = acknowledgeReward;
            if (checkpoint != null)
            {
                if (mode != SessionRole.Host || checkpoint.Mode != SavedSessionMode.Cooperative
                    || checkpoint.EquipmentIds[0] != checkpoint.EquipmentIds[1]) throw new InvalidOperationException("Saved cooperative session is incompatible.");
                weapon = ResolveWeapon(checkpoint.EquipmentIds[0]).CreateConfiguration();
            }
            if(steam==null)
            { transport.SetConnectionData(address,port,mode==SessionRole.Host?address:null);network.NetworkConfig.NetworkTransport=transport; }
            else network.NetworkConfig.NetworkTransport=steam.CreateTransport(network.gameObject);
            network.NetworkConfig.EnableSceneManagement = false;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            network.NetworkConfig.PlayerPrefab = null;
            network.NetworkConfig.TickRate = 30;
            network.NetworkConfig.ConnectionData = steam==null ? Encoding.ASCII.GetBytes("SpaceCrawlerSession8") : steam.Payload(this.profileId);
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
            network.OnTransportFailure += TransportFailed;
            if (mode == SessionRole.Host)
            {
                Life = new RunLife(checkpoint?.Life ?? RunLifeSnapshot.Fresh(2)); Life.BindProfile(0, this.profileId); runId = Life.RunId;
                var seed = hostSeed ?? (useFixedSeed ? fixedSeed : CreateSeed());
                sequence = checkpoint != null ? RunSequence.Restore(checkpoint, generation.CreateConfiguration())
                    : useLevelSequence ? new RunSequence(seed, generation.CreateConfiguration()) : null;
                Run = sequence?.CurrentGraph ?? new RunGenerator().Generate(seed, generation.CreateConfiguration());
                Fingerprint = GraphFingerprint.Compute(Run);
                Progression = checkpoint == null ? new RunProgression(Run, new PlayerIdentity(1))
                    : RunProgression.Restore(Run, new PlayerIdentity(1), checkpoint.RoomVisits);
                Progression.SetGameplayActive(true);
                CreatePlayers();
                enemies = new EnemyCombatSystem(transform, i => players[i].Motor.Body.position, () => Life,
                    (player, attack, amount) => DamagePlayer(player, attack, amount));
                Completion = new RunCompletion(Life);
                ApplyLife(); Life.Changed += LifeChanged;
                Authority = new SessionAuthority(NetworkManager.ServerClientId, players[0].Simulation, players[1].Simulation,
                    (i, intent) => players[i].Combat.RequestAttack(intent), (i, held) => Interact(new PlayerIdentity(i + 1), held));
                epoch = 1;
                LoadHostRoom(Progression.CurrentRoomId);
                players[1].gameObject.SetActive(false);
            }
            var started = mode == SessionRole.Host ? network.StartHost() : network.StartClient();
            if (!started) throw new InvalidOperationException("Could not start the multiplayer connection.");
            network.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            network.CustomMessagingManager.RegisterNamedMessageHandler(DescriptorMessage, ReceiveDescriptor);
            network.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveAck);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            network.CustomMessagingManager.RegisterNamedMessageHandler(EndMessage, ReceiveEnd);
            network.CustomMessagingManager.RegisterNamedMessageHandler(RewardAckMessage, ReceiveRewardAck);
            Debug.Log($"[Session] role={Mode} localPlayer={LocalPlayer} endpoint={(steam==null?address+":"+port:"Steam P2P")}; waiting for two-player room agreement", this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var validation = SessionDiagnostics.Option("--session-validation");
            if (validation == "run" || validation == "checkpoint" || validation == "resume" || validation == "enemies"
                || validation == "traversal" || validation == "traversal-resume" || validation == "health" || validation == "health-checkpoint" || validation == "health-resume" || validation == "melee") gameObject.AddComponent<SessionValidationDriver>().Bind(this, flow);
#endif
        }

        private static ulong CreateSeed()
        {
            var bytes = new byte[8]; using (var source = RandomNumberGenerator.Create()) source.GetBytes(bytes);
            ulong value = 0; for (var i = 0; i < bytes.Length; i++) value |= (ulong)bytes[i] << (i * 8); return value;
        }

        public RunSequence ValidateCheckpoint(RunCheckpoint checkpoint)
        {
            foreach (var id in checkpoint.EquipmentIds) ResolveWeapon(id);
            if (checkpoint.Mode == SavedSessionMode.Cooperative && checkpoint.EquipmentIds[0] != checkpoint.EquipmentIds[1])
                throw new InvalidOperationException("Saved cooperative equipment is not supported.");
            return RunSequence.Restore(checkpoint, generation.CreateConfiguration());
        }

        public WeaponDefinition ResolveWeapon(string id)
        {
            if (knife != null && knife.CreateConfiguration().Id == id) return knife;
            if (blaster != null && blaster.CreateConfiguration().Id == id) return blaster;
            throw new InvalidOperationException("Saved equipment is not installed.");
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
            string reason="The session requires one host and one client with the same protocol.";
            bool capacity=request.ClientNetworkId==NetworkManager.ServerClientId || network.ConnectedClientsIds.Count<2;
            bool valid=steam==null ? Encoding.ASCII.GetString(request.Payload)=="SpaceCrawlerSession8"
                : request.ClientNetworkId==NetworkManager.ServerClientId || steam.Approve(network.GetTransportIdFromClientId(request.ClientNetworkId),request.Payload,out reason);
            response.Approved=capacity && valid;
            response.CreatePlayerObject=false;response.Pending=false;
            if(!response.Approved)response.Reason=reason;
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
            var entryEpoch = epoch;
            rooms.LoadSharedRoom(Run, destination, Progression.ConnectionsFrom(destination),
                (edge, collider, present) => { if (epoch == entryEpoch) ObserveExit(edge, collider, present); });
            InitializeEncounter(true, Run.GetRoom(destination).IsTerminal);
            rooms.CurrentRoom.ShowRunExit(sequence != null && LevelNumber == RunSequence.LevelCount && Run.GetRoom(destination).IsTerminal);
            persistence.RestoreRoom(LevelNumber,destination,rooms.CurrentRoom,() => TrySave());
            for (var i = 0; i < 2; i++)
            {
                players[i].Combat.RegisterRoomTargets(rooms.CurrentRoom.CombatTargets, i == 0);
                var anchor = i == 0 ? rooms.CurrentRoom.PlayerOneSpawnAnchor : rooms.CurrentRoom.PlayerTwoSpawnAnchor;
                players[i].Motor.Body.simulated = true;
            }
            persistence.PlacePlayers(rooms.CurrentRoom,arrivalPort,players.Select(p => p.Motor).ToArray(),players.Select(p => p.Simulation).ToArray(),players.Select(p => p.Combat).ToArray());
            arrivalPort = -1;
            loadedRoom = destination;
            Physics2D.SyncTransforms(); follower.Follow();
            Readiness.Reset(destination, Progression.ConnectionsFrom(destination), Authority != null && Authority.BothConnected);
            entryStarted = Time.realtimeSinceStartupAsDouble;
        }

        private void ObserveCompletion()
        {
            if (Mode != SessionRole.Host || Completion == null || rooms.CurrentRoom == null) return;
            var anchor = rooms.CurrentRoom.RunExitAnchor; var exit = anchor != null ? anchor.position : Vector3.zero;
            Completion.Observe(LevelNumber,sequence != null,Progression,new System.Numerics.Vector2(exit.x,exit.y),
                players.Select(p => new System.Numerics.Vector2(p.Motor.Body.position.x,p.Motor.Body.position.y)).ToArray(),
                new[] { roomReady && Authority.InputEnabled(0) && !RunEnded,roomReady && Authority.InputEnabled(1) && !RunEnded });
        }
        private void Interact(PlayerIdentity player,bool held)
        {
            Life.SetReviveHeld(player,held); ObserveCompletion(); Completion.Interact(player,held);
        }

        private void ObserveExit(RoomConnection edge, Collider2D collider, bool present)
        {
            if (!roomReady || RunEnded || Authority == null || !Authority.BothConnected || collider == null) return;
            for (var i = 0; i < 2; i++)
                if (collider.attachedRigidbody == players[i].Motor.Body && Life[i].State == PlayerLifeState.Alive)
                    Readiness.SetReady(new ExitIntent(new PlayerIdentity(i + 1), edge.From, edge.To), present);
        }

        private void BeginTransition(RunRoomId destination)
        {
            if (!TrySave()) return;
            if (!Progression.TryRequestExit(new ExitIntent(Progression.Player, Progression.CurrentRoomId, destination))) return;
            arrivalPort = Progression.Choices.First(e => e.To.Equals(destination)).ToPort;
            epoch++;
            if (!Progression.TryBeginAcceptedEntry()) { Fail("Accepted shared room entry failed."); return; }
            if (sequence != null && Run.GetRoom(destination).IsTerminal && LevelNumber < RunSequence.LevelCount)
            {
                if (!Progression.TryCompleteEntry(destination) || !sequence.TryAdvance(Progression))
                { Fail("Accepted level transition failed."); return; }
                Run = sequence.CurrentGraph; Fingerprint = GraphFingerprint.Compute(Run);
                Progression = new RunProgression(Run, new PlayerIdentity(1));
                Progression.SetGameplayActive(true);
                destination = Run.StartRoomId; arrivalPort = -1;
            }
            LoadHostRoom(destination);
            SendDescriptor();
        }

        private void SendDescriptor()
        {
            if (!remoteSender.HasValue) return;
            descriptor = new RunSessionDescriptor { generationVersion = Run.GenerationVersion, seed = Run.Seed.ToString(), fingerprint = Fingerprint,
                runId = runId.ToString("N"),
                epoch = epoch, currentRoom = Progression.CurrentRoomId.Value, destination = loadedRoom.Value,
                state = (int)Progression.State, weapon = weapon.Id, level = LevelNumber,
                levels = sequence != null, sequenceVersion = sequence != null ? sequence.Version : 0,
                runSeed = (sequence?.Seed ?? Run.Seed).ToString() };
            SendJson(DescriptorMessage, remoteSender.Value, JsonUtility.ToJson(descriptor));
        }

        private void ReceiveDescriptor(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Client || sender != NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out string json);
                var incoming = JsonUtility.FromJson<RunSessionDescriptor>(json);
                if (incoming.protocol != 8 || incoming.epoch <= epoch) return;
                var incomingId = Guid.ParseExact(incoming.runId, "N");
                if (incomingId == Guid.Empty || (runId != Guid.Empty && runId != incomingId)) throw new InvalidOperationException("Run identity mismatch.");
                runId = incomingId;
                if (incoming.level < 1 || incoming.level > (incoming.levels ? RunSequence.LevelCount : 1))
                    throw new InvalidOperationException("Level descriptor mismatch.");
                if (Run != null && (incoming.levels != descriptor.levels || incoming.runSeed != descriptor.runSeed
                    || incoming.level < descriptor.level || incoming.level > descriptor.level + 1))
                    throw new InvalidOperationException("Run sequence descriptor mismatch.");
                if (incoming.levels && sequence == null)
                    sequence = new RunSequence(ulong.Parse(incoming.runSeed), generation.CreateConfiguration(), incoming.sequenceVersion);
                if (incoming.levels && incoming.sequenceVersion != sequence.Version)
                    throw new InvalidOperationException("Run sequence version mismatch.");
                if (Run != null && incoming.level > descriptor.level && incoming.destination != 0)
                    throw new InvalidOperationException("Next level must begin in its starting room.");
                var graph = incoming.levels ? sequence.Levels[incoming.level - 1]
                    : Run ?? new RunGenerator().Generate(ulong.Parse(incoming.seed), generation.CreateConfiguration(), incoming.generationVersion);
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
                InitializeEncounter(false, Run.GetRoom(loadedRoom).IsTerminal);
                rooms.CurrentRoom.ShowRunExit(incoming.levels && LevelNumber == RunSequence.LevelCount && Run.GetRoom(loadedRoom).IsTerminal);
                foreach (var target in rooms.CurrentRoom.Targets) target.PresentAuthoritativeHealth(target.InitialHealth, target.InitialHealth);
                SendJson(AckMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(new RoomLoadAcknowledgment { epoch = epoch, room = loadedRoom.Value, level = LevelNumber, fingerprint = Fingerprint, profileId = profileId.ToString("N") }));
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
                if (ack.epoch != epoch || ack.room != loadedRoom.Value || ack.level != LevelNumber) return;
                if (ack.fingerprint != Fingerprint) { Fail("Client graph fingerprint did not agree."); return; }
                peerProfileId = Guid.ParseExact(ack.profileId, "N");
                if (peerProfileId == profileId) throw new InvalidOperationException("Two separate player profiles are required.");
                if(steam!=null)steam.ValidateProfile(network.GetTransportIdFromClientId(sender),peerProfileId);
                Life.BindProfile(1, peerProfileId);
                if(steam!=null)steam.BindParticipants(Life.RunId,profileId,network.GetTransportIdFromClientId(sender),peerProfileId);
                if (roomReady) return;
                if (!Progression.TryCompleteEntry(loadedRoom)) { Fail("Core rejected shared room completion."); return; }
                SaveCurrent();
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
                RightReady = choices.Count > 1 ? Readiness.Count(choices[1].To) : 0,
                ThirdReady = choices.Count > 2 ? Readiness.Count(choices[2].To) : 0, FourthReady = choices.Count > 3 ? Readiness.Count(choices[3].To) : 0, Level = LevelNumber, RunXp = Life.RunXp, Ending = Life.Ended,
                CompletionAvailable = Completion.Available, CompletionConfirmations = Completion.Confirmations };
            lastHeader = header;
            using (var writer = new FastBufferWriter(8192, Allocator.Temp))
            {
                writer.WriteValueSafe(header);
                for (var i = 0; i < 2; i++) writer.WriteValueSafe(players[i].Capture(Authority.InputEnabled(i)));
                writer.WriteValueSafe(rooms.CurrentRoom.Targets.Count);
                foreach (var target in rooms.CurrentRoom.Targets)
                    writer.WriteValueSafe(new TargetStateSnapshot { Identity = target.Identity.Value, Current = target.Health.Current, Maximum = target.Health.Maximum });
                writer.WriteValueSafe(rooms.CurrentRoom.Encounter.Enemies.Count);
                foreach (var enemy in rooms.CurrentRoom.Encounter.Enemies)
                    writer.WriteValueSafe(new EnemyStateSnapshot { Encounter = enemy.Identity.Encounter, Spawn = enemy.Identity.Spawn,
                        Restored = enemy.Restored, Revision = enemy.Revision, Current = enemy.Target.Health.Current, Maximum = enemy.Target.Health.Maximum,
                        Lifecycle = (int)enemy.Lifecycle, Aggro = enemy.AggroPlayer, Position = enemy.transform.position,
                        Behaviour = (int)enemy.BehaviourState, Facing = enemy.Facing, WindupProgress = enemy.WindupProgress, StrikeSequence = enemy.StrikeSequence });
                var projectiles = players.Sum(p => p.Combat.Projectiles.Count);
                writer.WriteValueSafe(projectiles);
                foreach (var rig in players) foreach (var projectile in rig.Combat.Projectiles)
                    writer.WriteValueSafe(new ProjectileStateSnapshot { Owner = rig.State.Player.Value, Sequence = projectile.Execution.Sequence, Position = projectile.Position });
                network.CustomMessagingManager.SendNamedMessage(StateMessage, remoteSender.Value, writer,
                    writer.Length > 1200 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.UnreliableSequenced);
            }
            Changed?.Invoke();
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Client || sender != NetworkManager.ServerClientId || Run == null) return;
            try
            {
                reader.ReadValueSafe(out SessionStateHeader header);
                if (header.Epoch != epoch || header.Level != LevelNumber || header.Tick <= lastTick || header.CurrentRoom != loadedRoom.Value || !header.Running) return;
                lastTick = header.Tick; lastHeader = header;
                if (header.Ending && !RunEnded && (flow.State == ApplicationState.Gameplay || flow.State == ApplicationState.Paused)) flow.BeginRunEnd();
                if (header.RunXp < 0 || header.RunXp > long.MaxValue / 100) throw new InvalidOperationException("Invalid run experience.");
                for (var i = 0; i < 2; i++) { reader.ReadValueSafe(out PlayerStateSnapshot state); players[i].Apply(state); }
                reader.ReadValueSafe(out int targets);
                if (targets != rooms.CurrentRoom.Targets.Count) throw new InvalidOperationException("Target descriptor count mismatch.");
                for (var i = 0; i < targets; i++)
                {
                    reader.ReadValueSafe(out TargetStateSnapshot target);
                    if (rooms.CurrentRoom.Targets[i].Identity.Value != target.Identity) throw new InvalidOperationException("Target identity mismatch.");
                    rooms.CurrentRoom.Targets[i].PresentAuthoritativeHealth(target.Current, target.Maximum);
                }
                reader.ReadValueSafe(out int enemyCount);
                var enemies = rooms.CurrentRoom.Encounter.Enemies;
                if (enemyCount != enemies.Count) throw new InvalidOperationException("Enemy descriptor count mismatch.");
                for (var i = 0; i < enemyCount; i++)
                {
                    reader.ReadValueSafe(out EnemyStateSnapshot enemy);
                    if (enemy.Encounter != epoch || enemy.Spawn != enemies[i].Identity.Spawn
                        || float.IsNaN(enemy.Position.sqrMagnitude) || float.IsInfinity(enemy.Position.sqrMagnitude))
                        throw new InvalidOperationException("Enemy identity or position mismatch.");
                    enemies[i].ApplySnapshot(new EnemyIdentity(enemy.Encounter, enemy.Spawn), (EnemyLifecycle)enemy.Lifecycle,
                        enemy.Current, enemy.Maximum, enemy.Revision, enemy.Aggro, enemy.Restored);
                    enemies[i].transform.position = enemy.Position;
                    enemies[i].ApplyBehaviourSnapshot(enemy.Behaviour, enemy.Facing, enemy.WindupProgress, enemy.StrikeSequence);
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
            if (flow == null) return;
            RetryPendingReceipt();
            if (stopped) { if (Mode == SessionRole.Host && Life?.Ended == true && !RunEnded) LifeChanged(); return; }
            if (stopRequested) { Stop(); flow.TryRequest(ApplicationCommand.ReturnToMainMenu); return; }
            var now = Time.realtimeSinceStartupAsDouble;
            if (Mode == SessionRole.Host && persistence.AutosaveDue(Time.unscaledTime)) TrySave();
            var local = players[LocalPlayer - 1];
            if (PollLocalDevices && local != null) local.Input.SampleMovement();
            if (commands != null && now >= nextInput) { nextInput = now + 1.0 / 30; commands.SendMovement(); }
            if (Debug.isDebugBuild) SessionDiagnostics.Record(this, now);
            if (Mode != SessionRole.Host) return;
            Authority.Advance(now);
            ObserveCompletion();
            if (Life.Ended && !RunEnded) LifeChanged();
            if (roomReady && !RunEnded)
            {
                Life.AdvanceRevival(Time.deltaTime, players.Select(p => p.State.Position).ToArray(), new[] { Authority.InputEnabled(0), Authority.InputEnabled(1) });
                for (var i = 0; i < 2; i++) players[i].ReviveProgress = Life.ReviveProgress(i);
                if (Life[0].State == PlayerLifeState.Alive && Life[1].State == PlayerLifeState.Alive
                    && Readiness.TryGetSharedDestination(out var destination)) BeginTransition(destination);
            }
            if (!roomReady && !RunEnded && !Life.Ended && remoteSender.HasValue && now - entryStarted > 10) Fail("Room agreement timed out.");
            if (peerProfileId != Guid.Empty && remoteSender.HasValue && now >= nextRewards)
            {
                nextRewards = now + 1;
                try { if (pendingRewards != null) foreach (var receipt in pendingRewards(peerProfileId).Take(8)) SendReceipt(receipt); }
                catch (IOException) { HealthFailure = "Pending profile rewards could not be read. Retrying."; }
            }
        }

        private void FixedUpdate()
        {
            if (Mode == SessionRole.Host && players[0] != null)
                enemies?.Advance(rooms.CurrentRoom, roomReady && !stopped && !stopRequested && !RunEnded && !Life.Ended, Time.fixedDeltaTime);
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
            if (Mode == SessionRole.Host && savedAttackCount != players.Sum(p => p.Combat.Simulation.AcceptedAttackCount))
            { savedAttackCount = players.Sum(p => p.Combat.Simulation.AcceptedAttackCount); TrySave(); }
            if (Mode == SessionRole.Host && Time.realtimeSinceStartupAsDouble >= nextSnapshot)
            { nextSnapshot = Time.realtimeSinceStartupAsDouble + 0.05; SendState(); }
        }

        public string PlayerSummary()
        {
            if(!string.IsNullOrEmpty(Failure))return "Connection interrupted. Return to Main Menu. The host keeps the last saved run.";
            if(!roomReady)return Mode==SessionRole.Host ? "Waiting for your teammate. Invite a friend to begin." : "Joining the host's run...";
            int entered=Mode==SessionRole.Host ? Progression.RoomsEntered : lastHeader.RoomsEntered;
            return $"Level {LevelNumber} / {RunSequence.LevelCount}  ·  Rooms entered {entered}\nBoth players must stand at the same doorway to travel.";
        }

        public string Describe()
        {
            var text = new StringBuilder($"Role={Mode}; localPlayer={LocalPlayer}; connected={network.IsConnectedClient}; ready={roomReady}; epoch={epoch}; failure={Failure}\n");
            if (Run == null) return text.ToString();
            if (!roomReady) return text.AppendLine("Loading the shared room...").ToString();
            text.AppendLine($"Level {LevelNumber} / {RunSequence.LevelCount}; runSeed={sequence?.Seed ?? Run.Seed}; rooms={Run.Nodes.Count}");
            text.AppendLine($"Seed={Run.Seed}; version={Run.GenerationVersion}; fingerprint={Fingerprint}; loaded={loadedRoom}; room={(Mode == SessionRole.Host ? Progression.CurrentRoomId.Value : lastHeader.CurrentRoom)}; state={(Mode == SessionRole.Host ? Progression.State : (RoomFlowState)lastHeader.State)}; entered={(Mode == SessionRole.Host ? Progression.RoomsEntered : lastHeader.RoomsEntered)}; roomChildren={rooms.RoomContainer.childCount}");
            var choices = Run.Connections.Where(c => c.From.Equals(loadedRoom)).ToArray();
            for (var i = 0; i < choices.Length; i++)
                text.AppendLine($"Exit {(choices[i].FromPort >= 0 ? AuthoredRoom.PortName(choices[i].FromPort) : choices.Length == 1 ? "CENTER" : i == 0 ? "LEFT" : "RIGHT")} -> {choices[i].To}: {(Mode == SessionRole.Host ? Readiness.Count(choices[i].To) : i == 0 ? lastHeader.LeftReady : i == 1 ? lastHeader.RightReady : i == 2 ? lastHeader.ThirdReady : lastHeader.FourthReady)}/2 ready");
            if (Run.GetRoom(loadedRoom).IsTerminal) text.AppendLine(Run.GenerationVersion == 1 ? "Terminal room reached. No forward exits." : "Terminal room reached. Connected rooms remain available.");
            foreach (var rig in players)
            {
                var state = rig.State;
                text.AppendLine($"P{state.Player.Value}: position={state.Position}; velocity={state.Velocity}; aim={state.AimDirection}; attacks={(Mode == SessionRole.Host ? rig.Combat.Simulation.AcceptedAttackCount : rig.LastSnapshot.Attacks)}; cooldown={(Mode == SessionRole.Host ? rig.Combat.Simulation.CooldownRemaining : rig.LastSnapshot.Cooldown)}; input={(Mode == SessionRole.Host ? Authority.InputEnabled(state.Player.Value - 1) : rig.LastSnapshot.InputEnabled)}");
            }
            if (rooms.CurrentRoom != null) foreach (var target in rooms.CurrentRoom.Targets)
                text.AppendLine($"Target {target.Identity.Value}: health={(Mode == SessionRole.Host ? target.Health.Current : target.DisplayedHealth)}");
            if (rooms.CurrentRoom?.Encounter != null) foreach (var enemy in rooms.CurrentRoom.Encounter.Enemies)
                text.AppendLine($"Enemy {enemy.Identity.Encounter}:{enemy.Identity.Spawn}: health={enemy.Target.DisplayedHealth}; lifecycle={enemy.Lifecycle}; revision={enemy.Revision}; deaths={enemy.DeathEvents}; simulation={enemy.Simulation != null}");
            text.AppendLine($"Projectiles={(Mode == SessionRole.Host ? players.Sum(p => p.Combat.Projectiles.Count) : projectileViews.Count)}; localPaused={flow.State == ApplicationState.Paused}; camera={follower.transform.position}; rotation={follower.transform.eulerAngles}");
            if (Authority != null) text.AppendLine($"OwnershipRejected={Authority.RejectedOwnership}; staleRejected={Authority.RejectedStale}; inputTimeouts={Authority.TimedOut}");
            for (var i = 0; i < 2; i++) text.AppendLine($"P{i + 1} health={players[i].Vitals.Current}/{players[i].Vitals.Maximum}; life={players[i].Vitals.State}; revive={players[i].ReviveProgress:F2}");
            text.AppendLine($"RunID={runId:N}; RunXP={RunXp}; ended={RunEnded}; healthFailure={HealthFailure}");
            if (persistence?.RecoveryMessage != null) text.AppendLine(persistence.RecoveryMessage);
            return text.ToString();
        }

        private void ClearReplicaProjectiles()
        { foreach (var view in projectileViews.Values) if (view != null) Destroy(view.gameObject); projectileViews.Clear(); }
        private void Disconnected(ulong sender)
        {
            if (stopped) return;
            // A refused third connection cannot interrupt the accepted partner.
            if (Mode == SessionRole.Host && (!remoteSender.HasValue || sender != remoteSender.Value)) return;
            if (Outcome?.Reason == RunEndReason.Completed || Life?.EndReason == RunEndReason.Completed || pendingOutcome?.Reason == RunEndReason.Completed)
            { completedPeerDisconnected = true; Stop(); Changed?.Invoke(); return; }
            if (Mode == SessionRole.Client && pendingOutcome == null && !RunEnded) flow.CancelPendingRunEnd();
            Authority?.Disconnect(); Readiness.Disconnect();
            if (players[1] != null) players[1].gameObject.SetActive(false);
            Fail("Peer disconnected; two-player session stopped safely.");
        }
        public void EndExternalSession(string reason)
        {
            if(stopped)return;
            if(Mode==SessionRole.Client || remoteSender.HasValue)Disconnected(Mode==SessionRole.Host?remoteSender.Value:NetworkManager.ServerClientId);
            else Fail(reason);
        }
        public void ReleaseSteamTransport()
        { if(steam!=null && network!=null)network.NetworkConfig.NetworkTransport.Shutdown(); }
        private void TransportFailed() => Fail(steam==null?"Direct transport failed.":"Steam transport failed.");
        private void Fail(string reason)
        { if (stopRequested || stopped) return; Failure = reason; stopRequested = true; Debug.Log("[Session] " + reason, this); Changed?.Invoke(); }
        public void Stop()
        {
            if (stopped) return;
            if (flow == null) return;
            TrySave();
            stopped = true; Authority?.Disconnect(); Readiness.Disconnect();
            foreach (var rig in players) if (rig != null) { rig.Input.SetRoomAvailable(false); rig.ClearRoom(); }
            ClearReplicaProjectiles();
            network.OnClientConnectedCallback -= Connected; network.OnClientDisconnectCallback -= Disconnected;
            network.OnTransportFailure -= TransportFailed;
            if (network.CustomMessagingManager != null)
                foreach (var name in new[] { InputMessage, DescriptorMessage, AckMessage, StateMessage, EndMessage, RewardAckMessage }) network.CustomMessagingManager.UnregisterNamedMessageHandler(name);
            if (network.IsListening) network.Shutdown();
            Application.runInBackground = priorBackground;
        }
        private bool TrySave()
        {
            try { SaveCurrent(); return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Progress could not be saved. Check storage availability."; Changed?.Invoke(); return false; }
        }
        private void OnApplicationPause(bool paused) { if (paused && Life != null) TrySave(); }
        private void OnApplicationQuit() { if (Life != null) TrySave(); }
        public void SaveCurrent()
        {
            if (Mode != SessionRole.Host || Life == null || Life.Ended || Progression == null || rooms.CurrentRoom == null
                || (Progression.State != RoomFlowState.Playing && Progression.State != RoomFlowState.Terminal)) return;
            var world = persistence.Capture(LevelNumber,Progression.CurrentRoomId,rooms.CurrentRoom,players.Select(p => p.Motor).ToArray(),players.Select(p => p.Simulation).ToArray(),players.Select(p => p.Combat).ToArray());
            if (commitCheckpoint != null && sequence != null)
                persistence.Commit(RunCheckpoint.Capture(sequence,Progression,SavedSessionMode.Cooperative,new[] {weapon.Id,weapon.Id},Life.Capture(),world),commitCheckpoint);
        }

        public bool DamagePlayer(PlayerIdentity player, Guid attackId, int amount)
            => !stopped && !RunEnded && Mode == SessionRole.Host && roomReady && Life.TryDamage(player, attackId, amount);
        private void ApplyLife()
        {
            if (Life == null || players[0] == null) return;
            for (var i = 0; i < 2; i++)
            {
                players[i].PresentVitals(Life[i]);
                if (Life.Ended) { players[i].Simulation.SetLifeActive(false); players[i].Motor.Stop(); players[i].Combat.ClearRoom(); }
                if (Life[i].State != PlayerLifeState.Alive && Progression != null)
                    foreach (var edge in Progression.ConnectionsFrom(loadedRoom))
                        Readiness.SetReady(new ExitIntent(new PlayerIdentity(i + 1), loadedRoom, edge.To), false);
            }
        }
        private void LifeChanged()
        {
            ApplyLife();
            try
            {
                if (!Life.Ended) SaveCurrent();
                else if (!RunEnded)
                {
                    flow.BeginRunEnd(); Authority?.SetRunning(false, epoch);
                    Outcome = Outcome ?? Life.CreateReceipt(Life.EndReason.Value); endRun?.Invoke(Outcome);
                    RunEnded = true; SendState(); SendReceipt(Outcome);
                    flow.TryRequest(Outcome.Reason == RunEndReason.Completed ? ApplicationCommand.CompleteRun : ApplicationCommand.EndRun);
                }
                HealthFailure = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Progress could not be saved. Check storage availability. Retrying run completion."; }
            Changed?.Invoke();
        }
        private void SendReceipt(RunEndReceipt receipt)
        {
            if (!remoteSender.HasValue || !network.IsListening) return;
            SendJson(EndMessage, remoteSender.Value, JsonUtility.ToJson(new RunEndMessage { runId = receipt.RunId.ToString("N"), xp = receipt.RunXp,
                reason = (int)receipt.Reason, percent = receipt.Percent, profiles = receipt.Profiles.Select(p => p.ToString("N")).ToArray() }));
        }
        private void ReceiveEnd(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Client || sender != NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out string json); var message = JsonUtility.FromJson<RunEndMessage>(json);
                var receipt = new RunEndReceipt(Guid.ParseExact(message.runId, "N"), message.xp, (RunEndReason)message.reason,
                    message.profiles.Select(p => Guid.ParseExact(p, "N")).ToArray(), message.percent);
                if (!receipt.Profiles.Contains(profileId)) throw new InvalidOperationException("Reward belongs to another profile.");
                if (receipt.RunId == runId && !RunEnded) pendingOutcome = receipt;
                ApplyReceipt(receipt);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Profile reward could not be saved. Waiting for retry."; Changed?.Invoke(); }
            catch (Exception exception) { Fail("Rejected run receipt: " + exception.Message); }
        }
        private void ApplyReceipt(RunEndReceipt receipt)
        {
            if (receipt.RunId == runId && !RunEnded && (flow.State == ApplicationState.Gameplay || flow.State == ApplicationState.Paused)) flow.BeginRunEnd();
            endRun?.Invoke(receipt);
            HealthFailure = null;
            if (network.IsListening) SendJson(RewardAckMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(new RewardAcknowledgment { runId = receipt.RunId.ToString("N"), profileId = profileId.ToString("N") }));
            if (receipt.RunId == runId && !RunEnded)
            {
                RunEnded = true; pendingOutcome = null;
                Outcome = receipt;
                if (receipt.Reason == RunEndReason.Loss)
                    for (var i = 0; i < 2; i++) players[i].PresentVitals(new PlayerVitals(0, players[i].Vitals.Maximum, PlayerLifeState.Downed));
                flow.TryRequest(receipt.Reason == RunEndReason.Completed ? ApplicationCommand.CompleteRun : ApplicationCommand.EndRun); Changed?.Invoke();
            }
        }
        private void RetryPendingReceipt()
        {
            if (Mode != SessionRole.Client || pendingOutcome == null || RunEnded) return;
            try { ApplyReceipt(pendingOutcome); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { HealthFailure = "Profile reward could not be saved. Retrying locally."; Changed?.Invoke(); }
        }
        private void ReceiveRewardAck(ulong sender, FastBufferReader reader)
        {
            if (Mode != SessionRole.Host || !remoteSender.HasValue || sender != remoteSender.Value) return;
            try
            {
                reader.ReadValueSafe(out string json); var ack = JsonUtility.FromJson<RewardAcknowledgment>(json);
                var peer = Guid.ParseExact(ack.profileId, "N"); if (peer != peerProfileId) return;
                var acknowledgedRun = Guid.ParseExact(ack.runId, "N");
                acknowledgeReward?.Invoke(acknowledgedRun, peer);
                if (Outcome?.Reason == RunEndReason.Completed && acknowledgedRun == runId) { completionPeerAcknowledged = true; Changed?.Invoke(); }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is FormatException)
            { Debug.Log("[Session] Reward acknowledgement retained for retry.", this); }
        }
        private void OnDestroy() { enemies?.Dispose(); if (Life != null) Life.Changed -= LifeChanged; Stop(); }
    }
}
