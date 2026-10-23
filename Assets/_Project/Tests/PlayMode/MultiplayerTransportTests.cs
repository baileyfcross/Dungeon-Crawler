#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Tests.PlayMode
{
    // These exercise real NGO/UTP endpoints in one test process. Separate OS-process proofs
    // are additionally required and recorded in Logs/Validation, not inferred from this suite.
    public sealed class MultiplayerTransportTests
    {
        private MultiplayerSession host, client, refused;
        private ApplicationScreen transportScreen;
        private ApplicationFlow hostFlow, clientFlow;
        private bool previousBackground;
        private RunCheckpoint latestCheckpoint;
        private static ushort port = 17900;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousBackground = Application.runInBackground;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity", LoadSceneMode.Single);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (transportScreen != null) Object.Destroy(transportScreen.gameObject);
            if (client != null) client.Stop();
            if (refused != null) refused.Stop();
            if (host != null) host.Stop();
            yield return Frames(5);
            if (client != null) Object.Destroy(client.gameObject);
            if (refused != null) Object.Destroy(refused.gameObject);
            if (host != null) Object.Destroy(host.gameObject);
            yield return Frames(3);
            Application.runInBackground = previousBackground;
        }
        private static ApplicationFlow Playing()
        {
            var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay); return flow;
        }
        private IEnumerator StartPair(bool blaster = false, bool levels = false, int enemies = 0, bool melee = false, bool combatFixture = true, RunCheckpoint checkpoint = null, Action<RunEndReceipt> finishHost = null, Action<RunEndReceipt> finishClient = null, Func<Guid,System.Collections.Generic.IReadOnlyList<RunEndReceipt>> pending = null, Action<Guid,Guid> acknowledge = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab");
            host = Object.Instantiate(prefab); client = Object.Instantiate(prefab);
            host.Rooms.UseCombatFixture = combatFixture; client.Rooms.UseCombatFixture = combatFixture;
            host.EnemyFixtureCount = enemies; client.EnemyFixtureCount = enemies;
            // One rendered view/audio listener in this test process; both follower states still update.
            client.Follower.GameplayCamera.enabled = false;
            client.Follower.GetComponent<AudioListener>().enabled = false;
            if (blaster)
            {
                var data = new SerializedObject(host); data.FindProperty("useBlaster").boolValue = true; data.ApplyModifiedPropertiesWithoutUndo();
            }
            host.PollLocalDevices = false; client.PollLocalDevices = false;
            hostFlow = Playing(); clientFlow = Playing(); var endpoint = ++port;
            host.Bind(SessionRole.Host, hostFlow, port: endpoint, hostSeed: 42, useLevelSequence: levels, checkpoint: checkpoint, save: value => latestCheckpoint = value, finish:finishHost, pendingRewards:pending, acknowledgeReward:acknowledge);
            if (melee) host.Rooms.CurrentRoom.Encounter.Enemies[0].ConfigureMelee(
                AssetDatabase.LoadAssetAtPath<MeleeEnemyDefinition>("Assets/_Project/Data/Enemies/MeleeEnemy.asset"));
            // A competing seed argument is deliberately ignored for the client role.
            client.Bind(SessionRole.Client, clientFlow, port: endpoint, hostSeed: 999, profileId: checkpoint?.Life.Profiles[1], finish:finishClient);
            yield return WaitFor(() => host.RoomReady && client.RoomReady, "two-peer room handshake");
            if (melee) client.Rooms.CurrentRoom.Encounter.Enemies[0].ConfigureMelee(
                AssetDatabase.LoadAssetAtPath<MeleeEnemyDefinition>("Assets/_Project/Data/Enemies/MeleeEnemy.asset"));
            host.Players[0].Input.HandleFocus(true); client.Players[1].Input.HandleFocus(true);
            hostFlow.TryRequest(ApplicationCommand.Resume); clientFlow.TryRequest(ApplicationCommand.Resume);
            yield return Frames(3);
            Assert.That(host.Failure ?? client.Failure, Is.Null);
            Assert.That(host.LocalPlayer, Is.EqualTo(1)); Assert.That(client.LocalPlayer, Is.EqualTo(2));
            Assert.That(host.Run.Seed, Is.EqualTo(checkpoint?.Seed ?? 42UL)); Assert.That(client.Run.Seed, Is.EqualTo(host.Run.Seed));
            Assert.That(client.Fingerprint, Is.EqualTo(host.Fingerprint)); Assert.That(client.Progression, Is.Null);
            Assert.That(client.Authority, Is.Null); Assert.That(host.Network.ConnectedClientsIds.Count, Is.EqualTo(2));
            CheckRoomAndSpawns();
        }
        private void CheckRoomAndSpawns()
        {
            Assert.That(client.CurrentRoom, Is.EqualTo(host.CurrentRoom));
            Assert.That(host.Rooms.RoomContainer.childCount, Is.EqualTo(1)); Assert.That(client.Rooms.RoomContainer.childCount, Is.EqualTo(1));
            var room = host.Rooms.CurrentRoom;
            if (host.Run.GenerationVersion == 1)
            {
                Assert.That(Vector2.Distance(host.Players[0].Motor.Body.position, room.PlayerOneSpawnAnchor.position), Is.LessThan(.01));
                Assert.That(Vector2.Distance(host.Players[1].Motor.Body.position, room.PlayerTwoSpawnAnchor.position), Is.LessThan(.01));
            }
            else foreach (var rig in host.Players) Assert.That(room.IsSafePosition(rig.Motor.Body.position),Is.True);
            Assert.That(Vector2.Distance(host.Players[0].Motor.Body.position, host.Players[1].Motor.Body.position), Is.GreaterThan(1.2));
            Assert.That(Physics2D.GetIgnoreCollision(host.Players[0].Colliders[0], host.Players[1].Colliders[0]), Is.True);
            foreach (var rig in client.Players) { Assert.That(rig.Simulation, Is.Null); Assert.That(rig.Motor.Body.simulated, Is.False); }
            foreach (var rig in host.Players) Assert.That(rig.Combat.Targets, Is.EquivalentTo(room.CombatTargets));
        }
        private static void Move(MultiplayerSession endpoint, CoreVector direction)
            => Assert.That(endpoint.LocalCommands.Move(new MoveIntent(new PlayerIdentity(endpoint.LocalPlayer), direction)), Is.True);
        private static void Aim(MultiplayerSession endpoint, CoreVector direction)
            => Assert.That(endpoint.LocalCommands.Aim(new AimIntent(new PlayerIdentity(endpoint.LocalPlayer), direction)), Is.True);
        private static void Attack(MultiplayerSession endpoint)
            => Assert.That(endpoint.LocalCommands.Attack(new AttackIntent(new PlayerIdentity(endpoint.LocalPlayer), endpoint.LocalCommands.State.AimDirection)), Is.True);

        [UnityTest]
        public IEnumerator SessionAdmissionRejectsPreviousBuildWithoutChangingPlayerOwnership()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab");
            host = Object.Instantiate(prefab);
            host.PollLocalDevices = false;
            host.Bind(SessionRole.Host, Playing(), port: ++port, hostSeed: 42);
            yield return Frames(3);
            var request = new NetworkManager.ConnectionApprovalRequest
            {
                ClientNetworkId = 99,
                // Historical wire marker is intentionally retained only as a rejection test.
                Payload = System.Text.Encoding.ASCII.GetBytes("SpaceCrawlerSpike1")
            };
            var rejected = new NetworkManager.ConnectionApprovalResponse();
            host.Network.ConnectionApprovalCallback(request, rejected);
            Assert.That(rejected.Approved, Is.False);
            Assert.That(rejected.Reason, Is.Not.Empty);
            Assert.That(host.Authority.BothConnected, Is.False);

            request.Payload = System.Text.Encoding.ASCII.GetBytes("SpaceCrawlerSession3");
            var oldProtocol = new NetworkManager.ConnectionApprovalResponse();
            host.Network.ConnectionApprovalCallback(request, oldProtocol);
            Assert.That(oldProtocol.Approved, Is.False);
            request.Payload = System.Text.Encoding.ASCII.GetBytes("SpaceCrawlerSession8");
            var accepted = new NetworkManager.ConnectionApprovalResponse();
            host.Network.ConnectionApprovalCallback(request, accepted);
            Assert.That(accepted.Approved, Is.True);
            Assert.That(host.Authority.BothConnected, Is.False, "Approval alone must not assign an authoritative player.");
        }

        [UnityTest]
        public IEnumerator EnemyHealthDeathAndDespawnReplicateWithoutClientSimulation()
        {
            yield return StartPair(enemies: 1);
            var enemy = host.Rooms.CurrentRoom.Encounter.Enemies[0];
            var replica = client.Rooms.CurrentRoom.Encounter.Enemies[0];
            Assert.That(replica.Simulation, Is.Null); Assert.That(replica.Target.Health, Is.Null);
            Assert.That(replica.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(2), 1, replica.Target.Identity, 100), out _), Is.False);
            host.Players[0].Motor.PlaceAt(new Vector2(-1, 3.5f)); host.Players[1].Motor.PlaceAt(new Vector2(1, 3.5f));
            foreach (var rig in host.Players) rig.Motor.CaptureState();
            yield return Frames(8);
            Aim(host, new CoreVector(1, 1.5f)); Aim(client, new CoreVector(-1, 1.5f));
            yield return Frames(4); Attack(host); Attack(client);
            yield return WaitFor(() => enemy.Target.Health.Current == 50 && replica.Target.DisplayedHealth == 50, "shared enemy damage");
            Assert.That(enemy.Simulation.TrySetAggroTarget(new PlayerIdentity(2)), Is.True);
            yield return WaitFor(() => replica.AggroPlayer == 2, "aggro presentation");
            yield return new WaitForSeconds(.6f); Attack(host);
            yield return new WaitForSeconds(.6f); Attack(host);
            yield return WaitFor(() => replica.Lifecycle == EnemyLifecycle.Dead, "enemy death");
            Assert.That(enemy.DeathEvents, Is.EqualTo(1)); Assert.That(replica.DeathEvents, Is.EqualTo(1));
            Assert.That(replica.Target.HitColliders.All(c => !c.enabled), Is.True);
            Assert.That(enemy.Simulation.Despawn(), Is.True);
            yield return WaitFor(() => replica.Lifecycle == EnemyLifecycle.Despawned, "enemy despawn");
            Assert.That(replica.gameObject.activeSelf, Is.False);
            Assert.That(host.Failure ?? client.Failure, Is.Null);
        }

        [UnityTest]
        public IEnumerator FullEncounterReplicatesUsingFragmentedSnapshots()
        {
            yield return StartPair(enemies: 32);
            Assert.That(host.Rooms.CurrentRoom.Encounter.Enemies.Count, Is.EqualTo(32));
            Assert.That(client.Rooms.CurrentRoom.Encounter.Enemies.Count, Is.EqualTo(32));
            var enemy = host.Rooms.CurrentRoom.Encounter.Enemies[31];
            var replica = client.Rooms.CurrentRoom.Encounter.Enemies[31];
            Assert.That(enemy.Simulation.TrySetAggroTarget(new PlayerIdentity(2)), Is.True);
            yield return WaitFor(() => replica.AggroPlayer == 2, "large enemy snapshot replication");
            Assert.That(replica.Revision, Is.EqualTo(enemy.Revision));
            Assert.That(host.Failure ?? client.Failure, Is.Null);
        }

        [UnityTest]
        public IEnumerator SharedLevelBoundariesResetRoomsWithoutReplacingPlayersOrEquipment()
        {
            yield return StartPair(levels: true);
            transportScreen=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ApplicationScreen>("Assets/_Project/Prefabs/UI/GameplayScreen.prefab"));
            transportScreen.Bind(hostFlow);transportScreen.BindMultiplayer(host);
            var originalPlayers = host.Players.ToArray();
            var weapon = host.Players[0].Combat.Simulation.EquippedWeapon;
            var lastEpoch = host.Epoch;
            while (host.LevelNumber < 3 || !host.Progression.IsTerminal)
            {
                Assert.That(host.Run.Nodes.Count, Is.InRange(10, 25));
                var level = host.LevelNumber;
                var edge = host.Run.Route(host.CurrentRoom,host.Run.TerminalRoomId)[0];
                var destination = edge.To;
                var nextLevel = host.Run.GetRoom(destination).IsTerminal && level < 3;
                var exit = host.Rooms.CurrentRoom.ExitFor(edge,host.Progression.Choices);
                host.Players[0].Motor.PlaceAt(exit.transform.position);
                host.Players[1].Motor.PlaceAt(exit.transform.position + Vector3.right * .2f);
                yield return WaitFor(() => host.RoomReady && client.RoomReady && host.Epoch > lastEpoch
                    && client.Epoch == host.Epoch && client.LevelNumber == (nextLevel ? level + 1 : level)
                    && client.CurrentRoom.Equals(nextLevel ? new RunRoomId(0) : destination), "shared level/room agreement");
                lastEpoch = host.Epoch;
                yield return Frames(3);
                Assert.That(host.LevelNumber, Is.EqualTo(client.LevelNumber));
                Assert.That(host.Players, Is.EqualTo(originalPlayers));
                Assert.That(host.Players[0].Combat.Simulation.EquippedWeapon, Is.SameAs(weapon));
                Assert.That(host.Fingerprint, Is.EqualTo(client.Fingerprint));
                CheckRoomAndSpawns();
                if (nextLevel)
                {
                    Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1));
                    var rejected = host.Authority.RejectedStale;
                    SendClient("session.input.v8", new PlayerInputMessage { Epoch = lastEpoch - 1, ClaimedPlayer = 2,
                        Sequence = 9999, AttackSequence = 9999, Active = true, Move = Vector2.up, Aim = Vector2.up });
                    SendJson(client.Network, host.Network.LocalClientId, "session.room-ready.v8", JsonUtility.ToJson(
                        new RoomLoadAcknowledgment { epoch = lastEpoch, level = level, room = 0, fingerprint = host.Fingerprint }));
                    yield return WaitFor(() => host.Authority.RejectedStale > rejected, "prior-level input rejected");
                    Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1), "Old-level acknowledgment cannot commit entry again.");
                }
            }
            Assert.That(host.LevelNumber, Is.EqualTo(3));
            Assert.That(host.RoomReady && client.RoomReady, Is.True);
            if (host.Run.GenerationVersion == 1) Assert.That(host.Progression.Choices,Is.Empty);
            else Assert.That(host.Progression.Choices.Count, Is.InRange(1,4));
        }

        [UnityTest]
        public IEnumerator SenderAuthenticatedMovementAimCameraAndKnifeDamageReplicate()
        {
            yield return StartPair();
            refused = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab"));
            refused.Follower.GameplayCamera.enabled = false; refused.Follower.GetComponent<AudioListener>().enabled = false;
            refused.PollLocalDevices = false;
            refused.Bind(SessionRole.Client, Playing(), port: port);
            yield return WaitFor(() => refused.Failure != null, "extra connection refused");
            yield return Frames(4);
            Assert.That(host.Failure, Is.Null, "Refusing an extra sender must not disconnect the accepted partner.");
            Assert.That(host.Authority.BothConnected && host.RoomReady && client.RoomReady, Is.True);
            Assert.That(host.Network.ConnectedClientsIds.Count, Is.EqualTo(2));
            var oneStart = host.Players[0].State.Position; var twoStart = host.Players[1].State.Position;
            Move(host, -CoreVector.UnitX); Aim(host, CoreVector.UnitY);
            Move(client, CoreVector.UnitX); Aim(client, -CoreVector.UnitY);
            yield return new WaitForSeconds(.25f);
            Move(host, CoreVector.Zero); Move(client, CoreVector.Zero); yield return new WaitForSeconds(.15f);
            Assert.That(host.Players[0].State.Position.X, Is.LessThan(oneStart.X - .4));
            Assert.That(host.Players[1].State.Position.X, Is.GreaterThan(twoStart.X + .4));
            Assert.That(host.Players[0].State.AimDirection, Is.EqualTo(CoreVector.UnitY));
            Assert.That(host.Players[1].State.AimDirection, Is.EqualTo(-CoreVector.UnitY));
            for (var i = 0; i < 2; i++)
            {
                Assert.That(CoreVector.Distance(client.Players[i].State.Position, host.Players[i].State.Position), Is.LessThan(.02));
                Assert.That(client.Players[i].State.AimDirection, Is.EqualTo(host.Players[i].State.AimDirection));
            }
            // The follower tracks the presented Transform, not the Rigidbody's physics snapshot.
            // Physics/Transform synchronization can differ by floating-point rounding at rest.
            Assert.That(Vector2.Distance(host.Follower.transform.position, host.Players[0].Motor.transform.position),
                Is.LessThan(0.0001f), "Host camera follows its own presented player.");
            Assert.That(Vector2.Distance(client.Follower.transform.position, client.Players[1].Motor.transform.position),
                Is.LessThan(0.0001f), "Client camera follows its own presented player.");
            Assert.That(host.Follower.transform.eulerAngles, Is.EqualTo(Vector3.zero)); Assert.That(client.Follower.transform.eulerAngles, Is.EqualTo(Vector3.zero));

            var before = host.Authority.RejectedOwnership;
            SendClient("session.input.v8", new PlayerInputMessage { ClaimedPlayer = 1, Epoch = host.Epoch, Sequence = 9999,
                AttackSequence = 9999, Active = true, Move = Vector2.up, Aim = Vector2.left });
            yield return new WaitForSeconds(.15f);
            Assert.That(host.Authority.RejectedOwnership, Is.EqualTo(before + 1));
            Assert.That(host.Players[0].Simulation.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(host.Players[0].State.AimDirection, Is.EqualTo(CoreVector.UnitY));

            var target = host.Rooms.CurrentRoom.Targets[0]; var replica = client.Rooms.CurrentRoom.Targets[0];
            var position = (Vector2)target.transform.position - Vector2.up * 1.3f;
            host.Players[0].Motor.PlaceAt(position); host.Players[1].Motor.PlaceAt(position + Vector2.right * .1f);
            yield return Frames(3); Aim(host, CoreVector.UnitY); Aim(client, CoreVector.UnitY);
            Attack(host); Attack(host); Attack(client); Attack(client);
            yield return new WaitForSeconds(.18f);
            Assert.That(host.Players[0].Combat.Simulation.AcceptedAttackCount, Is.EqualTo(1));
            Assert.That(host.Players[1].Combat.Simulation.AcceptedAttackCount, Is.EqualTo(1));
            Assert.That(target.Health.Current, Is.EqualTo(50)); Assert.That(replica.DisplayedHealth, Is.EqualTo(50));
            Assert.That(replica.Health, Is.Null);
            Assert.Throws<InvalidOperationException>(() => replica.ReceiveDamage(new DamageRequest(new PlayerIdentity(2), 30, replica.Identity, 99), out _));
            Assert.That(client.Players[1].Combat.RequestAttack(new AttackIntent(new PlayerIdentity(2), CoreVector.UnitY)), Is.False);
            Assert.That(target.Health.Current, Is.EqualTo(50));
        }

        [UnityTest]
        public IEnumerator LocalPauseAndFocusNeutralizeOnlyOwnedInputAndDisconnectStopsSafely()
        {
            yield return StartPair();
            Move(client, CoreVector.UnitX); Aim(host, CoreVector.UnitY); Attack(host);
            hostFlow.TryRequest(ApplicationCommand.Pause);
            var initial = host.Players[1].State.Position;
            yield return new WaitForSeconds(.65f);
            Assert.That(host.Players[1].State.Position.X, Is.GreaterThan(initial.X + 1));
            Assert.That(host.Players[0].Simulation.IsActive, Is.True);
            Assert.That(host.Players[0].Combat.Simulation.CooldownRemaining, Is.Zero);
            Assert.That(host.Authority.InputEnabled(0), Is.False); Assert.That(host.Authority.InputEnabled(1), Is.True);
            hostFlow.TryRequest(ApplicationCommand.Resume); Move(host, -CoreVector.UnitX);
            client.Players[1].Input.HandleFocus(false);
            initial = host.Players[0].State.Position;
            yield return new WaitForSeconds(.2f);
            Assert.That(clientFlow.State, Is.EqualTo(ApplicationState.Paused));
            Assert.That(host.Players[1].Simulation.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(host.Players[0].State.Position.X, Is.LessThan(initial.X - .4));
            Assert.That(host.Authority.Running, Is.True);
            Move(host, CoreVector.Zero);
            host.Players[0].Motor.PlaceAt(host.Rooms.CurrentRoom.LeftExit.transform.position);
            yield return WaitFor(() => host.Readiness.Count(host.Progression.Choices[0].To) == 1, "host exit readiness");
            client.Stop(); yield return WaitFor(() => host.Failure != null, "disconnect notification"); yield return Frames(4);
            Assert.That(host.Authority.Running || host.Readiness.BothConnected, Is.False);
            Assert.That(host.Players[1].gameObject.activeSelf, Is.False);
            Assert.That(host.Players[0].Simulation.DesiredVelocity + host.Players[1].Simulation.DesiredVelocity, Is.EqualTo(CoreVector.Zero));
            Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1)); Assert.That(hostFlow.State, Is.EqualTo(ApplicationState.Loading));
        }

        [UnityTest]
        public IEnumerator PhysicalReadinessBothBranchesRejoinAndTerminalCleanRoomState()
        {
            yield return StartPair();
            var start = host.CurrentRoom; var left = host.Progression.Choices[0].To; var right = host.Progression.Choices[1].To;
            host.Players[0].Motor.PlaceAt(host.Rooms.CurrentRoom.LeftExit.transform.position);
            yield return WaitFor(() => host.Readiness.Count(left) == 1, "one player at left exit");
            yield return new WaitForSeconds(.15f); Assert.That(host.CurrentRoom, Is.EqualTo(start));
            host.Players[1].Motor.PlaceAt(host.Rooms.CurrentRoom.RightExit.transform.position);
            yield return WaitFor(() => host.Readiness.Count(right) == 1, "different exits");
            Assert.That(host.Readiness.Count(left), Is.EqualTo(1)); Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1));
            host.Players[1].Motor.PlaceAt(host.Rooms.CurrentRoom.PlayerTwoSpawnAnchor.position);
            yield return WaitFor(() => host.Readiness.Count(right) == 0, "leaving exit clears readiness");
            var expectedEntries = 1; var sawRejoin = false;
            while (!host.Progression.IsTerminal)
            {
                var index = expectedEntries % host.Progression.Choices.Count;
                var destination = host.Progression.Choices[index].To;
                sawRejoin |= host.Run.Connections.Count(e => e.To.Equals(destination)) == 2;
                var oldHost = host.Rooms.CurrentRoom; var oldClient = client.Rooms.CurrentRoom;
                var exit = host.Progression.Choices.Count == 1 ? oldHost.SingleExit : index == 0 ? oldHost.LeftExit : oldHost.RightExit;
                var expectedEpoch = host.Epoch + 1;
                host.Players[0].Motor.PlaceAt(exit.transform.position);
                host.Players[1].Motor.PlaceAt(exit.transform.position + Vector3.right * .2f);
                yield return WaitFor(() => host.Epoch == expectedEpoch && host.RoomReady && client.RoomReady && client.CurrentRoom.Equals(destination), "shared room entry");
                yield return Frames(3); CheckRoomAndSpawns();
                Assert.That(host.Progression.RoomsEntered, Is.EqualTo(++expectedEntries));
                Assert.That(oldHost == null && oldClient == null, Is.True);
                Assert.That(client.ReplicaProjectileCount, Is.Zero);
                foreach (var target in client.Rooms.CurrentRoom.Targets) Assert.That(target.DisplayedHealth, Is.EqualTo(100));
            }
            Assert.That(sawRejoin, Is.True); Assert.That(expectedEntries, Is.InRange(9, 11));
            if (host.Run.GenerationVersion == 1) Assert.That(host.Progression.Choices,Is.Empty);
            else Assert.That(host.Progression.Choices.Count, Is.InRange(1,4));
            Assert.That(new[] { host.Rooms.CurrentRoom.SingleExit, host.Rooms.CurrentRoom.LeftExit, host.Rooms.CurrentRoom.RightExit }.Any(e => e.gameObject.activeSelf), Is.False);
            Assert.That(client.Describe(), Does.Contain("Terminal room reached"));
            Move(client, CoreVector.UnitX); Aim(client, CoreVector.UnitY); Attack(client);
            yield return new WaitForSeconds(.15f); Assert.That(host.Players[1].Simulation.DesiredVelocity.X, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator BlasterProjectilesAreHostOnlyAndCleanupOnSharedEntry()
        {
            yield return StartPair(true); Assert.That(client.WeaponName, Is.EqualTo("weapon.blaster"));
            Assert.That(client.WeaponDisplayName, Is.EqualTo("Blaster"));
            // Keep the fixture's shot origin clear of the authored south wall as well as the target.
            var target = host.Rooms.CurrentRoom.Targets[0]; var position = (Vector2)target.transform.position - Vector2.up * 1.3f;
            host.Players[0].Motor.PlaceAt(position); host.Players[1].Motor.PlaceAt(position + Vector2.right * .1f);
            yield return Frames(3); Aim(host, CoreVector.UnitY); Aim(client, CoreVector.UnitY); Attack(host); Attack(client);
            yield return WaitFor(() => target.Health.Current == 60 && client.Rooms.CurrentRoom.Targets[0].DisplayedHealth == 60, "two authoritative first impacts");
            Assert.That(client.Players.All(p => p.Combat.Simulation == null && p.Combat.Projectiles.Count == 0), Is.True);
            yield return new WaitForSeconds(.35f);
            var exit = host.Rooms.CurrentRoom.LeftExit; var destination = host.Progression.Choices[0].To;
            host.Players[0].Motor.PlaceAt(exit.transform.position); host.Players[1].Motor.PlaceAt(exit.transform.position + Vector3.right * .2f);
            Aim(host, -CoreVector.UnitY); Aim(client, -CoreVector.UnitY); Attack(host); Attack(client);
            yield return WaitFor(() => host.RoomReady && client.RoomReady && client.CurrentRoom.Equals(destination), "projectile room cleanup");
            yield return Frames(3);
            Assert.That(host.Players.Sum(p => p.Combat.Projectiles.Count), Is.Zero); Assert.That(client.ReplicaProjectileCount, Is.Zero);
            Assert.That(client.Rooms.CurrentRoom.Targets.All(t => t.DisplayedHealth == 100), Is.True);
        }

        [UnityTest]
        public IEnumerator ForgedClientRunMessagesCannotAdvanceHostAndBadGraphStopsClient()
        {
            yield return StartPair(); var initial = host.CurrentRoom;
            var forged = new RunSessionDescriptor { runId = host.Life.RunId.ToString("N"), epoch = host.Epoch + 1, seed = "999", fingerprint = "forged", destination = 10, generationVersion = 1, weapon = "weapon.knife" };
            SendJson(client.Network, host.Network.LocalClientId, "session.run.v8", JsonUtility.ToJson(forged));
            yield return new WaitForSeconds(.15f);
            Assert.That(host.Run.Seed, Is.EqualTo(42)); Assert.That(host.CurrentRoom, Is.EqualTo(initial)); Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1));
            // Fault injection from the real server transport, deliberately corrupting graph agreement.
            SendJson(host.Network, client.Network.LocalClientId, "session.run.v8", JsonUtility.ToJson(forged));
            yield return WaitFor(() => client.Failure != null, "mismatching descriptor rejection");
            Assert.That(client.Failure, Does.Contain("mismatch")); Assert.That(client.Run.Seed, Is.EqualTo(42));
        }

        [UnityTest]
        public IEnumerator MeleeEnemyRunsOnlyOnHostAndReplicatesWindupDamageAndDeath()
        {
            yield return StartPair(enemies: 1, melee: true);
            var enemy = host.Rooms.CurrentRoom.Encounter.Enemies[0]; var replica = client.Rooms.CurrentRoom.Encounter.Enemies[0];
            Assert.That(replica.Behaviour, Is.Null); Assert.That(replica.Simulation, Is.Null);
            var start = enemy.transform.position;
            yield return WaitFor(() => Vector3.Distance(enemy.transform.position, start) > 1, "host enemy pursuit");
            yield return WaitFor(() => replica.BehaviourState == EnemyBehaviourState.Windup && replica.WindupProgress > .05f, "replicated attack windup");
            Assert.That(Vector2.Distance(enemy.Facing, replica.Facing), Is.LessThan(.01));
            yield return WaitFor(() => host.Life[0].Current < 100 || host.Life[1].Current < 100, "enemy damage through host life model");
            yield return WaitFor(() => client.Vitals(0).Current == host.Life[0].Current && client.Vitals(1).Current == host.Life[1].Current, "replicated player health");
            Assert.That(host.Life[0].Current + host.Life[1].Current, Is.EqualTo(180));
            var victim = enemy.AggroPlayer;
            host.DamagePlayer(new PlayerIdentity(victim), Guid.NewGuid(), 100);
            yield return WaitFor(() => enemy.AggroPlayer == 3 - victim, "downed player excluded from targeting");
            enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1), 900, enemy.Target.Identity, 100), out _);
            yield return WaitFor(() => replica.Lifecycle == EnemyLifecycle.Dead, "replicated melee enemy death");
            Assert.That(replica.DeathEvents, Is.EqualTo(1)); Assert.That(host.RunXp, Is.Zero);
            Assert.That(host.Failure ?? client.Failure, Is.Null);
        }

        [UnityTest]
        public IEnumerator NormalCoopRoomsNeverSpawnStationaryCombatFixtures()
        {
            yield return StartPair(combatFixture: false);
            var route = SessionValidationDriver.RouteToEngine(host.Run);
            Assert.That(route, Is.Not.Null);
            foreach (var edge in route)
            {
                Assert.That(host.Rooms.CurrentRoom.Targets, Is.Empty); Assert.That(client.Rooms.CurrentRoom.Targets, Is.Empty);
                var choices = host.Progression.Choices;
                var index = choices.ToList().FindIndex(choice => choice.To.Equals(edge.To));
                var exit = choices.Count == 1 ? host.Rooms.CurrentRoom.SingleExit : index == 0 ? host.Rooms.CurrentRoom.LeftExit : host.Rooms.CurrentRoom.RightExit;
                host.Players[0].Motor.PlaceAt(exit.transform.position + Vector3.left * .2f);
                host.Players[1].Motor.PlaceAt(exit.transform.position + Vector3.right * .2f);
                yield return WaitFor(() => host.CurrentRoom.Equals(edge.To) && client.CurrentRoom.Equals(edge.To) && host.RoomReady && client.RoomReady, "normal room transition");
            }
            Assert.That(host.Rooms.CurrentRoom.Targets, Is.Empty); Assert.That(client.Rooms.CurrentRoom.Targets, Is.Empty);
            var enemy = host.Rooms.CurrentRoom.Encounter.Enemies.Single();
            Assert.That(enemy.Behaviour, Is.Not.Null);
            Assert.That(client.Rooms.CurrentRoom.Encounter.Enemies.Single().Behaviour, Is.Null);
            Assert.That(host.Rooms.CurrentRoom.GetComponentsInChildren<DamageableTarget>(true).Length, Is.EqualTo(1));
            Assert.That(client.Rooms.CurrentRoom.GetComponentsInChildren<DamageableTarget>(true).Length, Is.EqualTo(1));
            Assert.That(host.Failure ?? client.Failure, Is.Null);
        }

        [UnityTest]
        public IEnumerator SharedBacktrackingAndContinuePreserveEnemiesPlayerPosesAndDownedState()
        {
            yield return StartPair(levels:true,combatFixture:false);
            var route = SessionValidationDriver.RouteToEngine(host.Run); Assert.That(route,Is.Not.Null);
            foreach (var edge in route) yield return Travel(edge);
            var encounterRoom = host.CurrentRoom;
            var enemy = host.Rooms.CurrentRoom.Encounter.Enemies.Single();
            enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),1000,enemy.Target.Identity,100),out _);
            yield return WaitFor(() => client.Rooms.CurrentRoom.Encounter.Enemies.Single().Lifecycle == EnemyLifecycle.Dead,"enemy death");
            var outgoing = host.Progression.Choices.First(e => !host.Run.GetRoom(e.To).IsTerminal);
            yield return Travel(outgoing);
            var incoming = host.Progression.Choices.Single(e => e.To.Equals(encounterRoom));
            yield return Travel(incoming);
            Assert.That(host.Rooms.CurrentRoom.Encounter.Enemies.Single().Target.Health.Current,Is.Zero);
            Assert.That(host.Rooms.CurrentRoom.Encounter.Enemies.Single().DeathEvents,Is.Zero,"restore must not emit a second death");
            Assert.That(Vector2.Distance(host.Players[0].Motor.Body.position,host.Rooms.CurrentRoom.Arrival(incoming.ToPort,0,2)),Is.LessThan(.02));
            host.Players[0].Motor.PlaceAt(new Vector2(-2,3)); host.Players[1].Motor.PlaceAt(new Vector2(2,3));
            host.Players[0].Simulation.RestoreAim(CoreVector.UnitY); host.Players[1].Simulation.RestoreAim(-CoreVector.UnitY);
            host.DamagePlayer(new PlayerIdentity(1),Guid.NewGuid(),23); host.DamagePlayer(new PlayerIdentity(2),Guid.NewGuid(),100);
            host.Players[0].Combat.Simulation.RestoreCooldown(.1f); host.SaveCurrent();
            var saved = RunCheckpointCodec.Decode(RunCheckpointCodec.Encode(latestCheckpoint));
            client.Stop(); host.Stop(); Object.Destroy(client.gameObject); Object.Destroy(host.gameObject); yield return Frames(5);
            yield return StartPair(levels:true,combatFixture:false,checkpoint:saved);
            Assert.That(host.CurrentRoom,Is.EqualTo(encounterRoom)); Assert.That(host.Life[0].Current,Is.EqualTo(77));
            Assert.That(host.Life[1].State,Is.EqualTo(PlayerLifeState.Downed)); Assert.That(client.Vitals(1).State,Is.EqualTo(PlayerLifeState.Downed));
            Assert.That(host.Players[0].Motor.Body.position,Is.EqualTo(new Vector2(-2,3))); Assert.That(host.Players[1].Motor.Body.position,Is.EqualTo(new Vector2(2,3)));
            Assert.That(host.Players[0].State.AimDirection,Is.EqualTo(CoreVector.UnitY)); Assert.That(host.Players[1].State.AimDirection,Is.EqualTo(-CoreVector.UnitY));
            Assert.That(host.Life.ReviveProgress(1),Is.Zero); Assert.That(host.Rooms.CurrentRoom.Encounter.Enemies.Single().Target.Health.Current,Is.Zero);
            Assert.That(client.Rooms.CurrentRoom.Encounter.Enemies.Single().Target.DisplayedHealth,Is.Zero);
            Assert.That(client.Rooms.CurrentRoom.Encounter.Enemies.Single().DeathEvents,Is.Zero,"initial dead replica does not replay death");
            Assert.That(host.Failure ?? client.Failure,Is.Null);
        }
        private IEnumerator Travel(RoomConnection edge)
        {
            var beforeEpoch = host.Epoch; var room = host.Rooms.CurrentRoom;
            var exit = room.ExitFor(edge,host.Progression.Choices);
            host.Players[0].Motor.PlaceAt(exit.transform.position); host.Players[1].Motor.PlaceAt(exit.transform.position);
            yield return WaitFor(() => host.Epoch > beforeEpoch && client.Epoch == host.Epoch && host.RoomReady && client.RoomReady,"reciprocal shared door");
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator CooperativeCompletionRequiresLivingPartyAndRetriesBothDurableRewards()
        {
            var folder=System.IO.Path.Combine("Logs","Validation","Completion","Peers",Guid.NewGuid().ToString("N"));
            var hostStore=new ProfileProgressStore(System.IO.Path.Combine(folder,"Host"));var clientStore=new ProfileProgressStore(System.IO.Path.Combine(folder,"Client"));
            var catalog=AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>("Assets/_Project/Data/Runs/DefaultRun.asset").CreateConfiguration();
            var sequence=new RunSequence(42,catalog);RunProgression final;
            while(true) {
                final=new RunProgression(sequence.CurrentGraph,new PlayerIdentity(1));final.SetGameplayActive(true);final.TryCompleteEntry(sequence.CurrentGraph.StartRoomId);
                foreach(var edge in sequence.CurrentGraph.Route(final.CurrentRoomId,sequence.CurrentGraph.TerminalRoomId))
                {final.TryRequestExit(new ExitIntent(final.Player,edge.From,edge.To));final.TryBeginAcceptedEntry();final.TryCompleteEntry(edge.To);}
                if(!sequence.TryAdvance(final))break;
            }
            var life=new RunLifeSnapshot(Guid.NewGuid(),250,new[]{PlayerVitals.Starting,PlayerVitals.Starting},new[]{hostStore.Profile.Id,clientStore.Profile.Id});
            var checkpoint=RunCheckpoint.Capture(sequence,final,SavedSessionMode.Cooperative,new[]{"weapon.knife","weapon.knife"},life);
            yield return StartPair(levels:true,combatFixture:false,checkpoint:checkpoint,finishHost:hostStore.EndRun,finishClient:clientStore.EndRun,pending:hostStore.PendingFor,acknowledge:hostStore.Acknowledge);
            var anchor=(Vector2)host.Rooms.CurrentRoom.RunExitAnchor.position;
            host.Players[0].Motor.PlaceAt(anchor+Vector2.left*.6f);host.Players[1].Motor.PlaceAt(anchor+Vector2.right*.6f);
            host.DamagePlayer(new PlayerIdentity(2),Guid.NewGuid(),100);
            yield return Frames(3);host.LocalCommands.Interact(new PlayerIdentity(1),false);host.LocalCommands.Interact(new PlayerIdentity(1),true);
            yield return Frames(3);Assert.That(host.RunEnded,Is.False);Assert.That(host.Completion.Confirmations,Is.Zero);
            yield return WaitFor(()=>host.Life[1].State==PlayerLifeState.Alive && client.Vitals(1).State==PlayerLifeState.Alive && host.Completion.PartyReady,"revived players regain authoritative input before fresh confirmation");
            host.LocalCommands.Interact(new PlayerIdentity(1),false);client.LocalCommands.Interact(new PlayerIdentity(2),false);
            yield return new WaitForSeconds(.2f); // Allow neutral input to be observed across real network ticks after revival.
            using(var hostLock=new System.IO.FileStream(System.IO.Path.Combine(folder,"Host","progress.lock"),System.IO.FileMode.OpenOrCreate,System.IO.FileAccess.Write,System.IO.FileShare.None))
            using(var clientLock=new System.IO.FileStream(System.IO.Path.Combine(folder,"Client","progress.lock"),System.IO.FileMode.OpenOrCreate,System.IO.FileAccess.Write,System.IO.FileShare.None))
            {
                host.LocalCommands.Interact(new PlayerIdentity(1),true);yield return WaitFor(()=>host.Completion.Confirmations==1,"host fresh confirmation acknowledged");Assert.That(host.Life.Ended,Is.False,"both must confirm");
                client.LocalCommands.Interact(new PlayerIdentity(2),true);
                yield return WaitFor(()=>host.Life.Ended && clientFlow.RunEndPending,"authoritative completion freezes both peers");
                Assert.That(hostFlow.RunEndPending,Is.True);Assert.That(hostStore.Profile.Experience,Is.Zero);Assert.That(host.RunEnded,Is.False);
                Assert.That(host.Players.All(p=>!p.Simulation.CanAct),Is.True);Assert.That(hostFlow.TryRequest(ApplicationCommand.ReturnToMainMenu),Is.False);
            }
            yield return WaitFor(()=>hostFlow.State==ApplicationState.Completed && clientFlow.State==ApplicationState.Completed,"durable completion on both profiles");
            yield return WaitFor(()=>host.CanLeaveCompletion,"peer reward acknowledgment");
            Assert.That(hostStore.Profile.Experience,Is.EqualTo(15));Assert.That(clientStore.Profile.Experience,Is.EqualTo(15));
            Assert.That(host.Outcome.Reason,Is.EqualTo(RunEndReason.Completed));Assert.That(client.Vitals(1).State,Is.EqualTo(PlayerLifeState.Alive));
            var receipt=host.Outcome;
            SendJson(host.Network,client.Network.LocalClientId,"session.end.v8",JsonUtility.ToJson(new RunEndMessage {runId=receipt.RunId.ToString("N"),xp=receipt.RunXp,reason=(int)receipt.Reason,percent=receipt.Percent,profiles=receipt.Profiles.Select(id=>id.ToString("N")).ToArray()}));
            yield return Frames(5);Assert.That(clientStore.Profile.Experience,Is.EqualTo(15));Assert.That(host.Life.TryComplete(),Is.False);
            client.Stop();yield return Frames(5);Assert.That(hostFlow.State,Is.EqualTo(ApplicationState.Completed),"completion panel survives peer departure");
        }

        [UnityTest]
        public IEnumerator ReceivedCompletionKeepsRetryingLocallyAfterHostDisconnects()
        {
            var folder=System.IO.Path.Combine("Logs","Validation","Completion","Disconnect",Guid.NewGuid().ToString("N"));
            var own=new ProfileProgressStore(System.IO.Path.Combine(folder,"Host"));var peer=new ProfileProgressStore(System.IO.Path.Combine(folder,"Client"));
            var sequence=new RunSequence(42,AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>("Assets/_Project/Data/Runs/DefaultRun.asset").CreateConfiguration());
            RunProgression final;
            while(true){final=new RunProgression(sequence.CurrentGraph,new PlayerIdentity(1));final.SetGameplayActive(true);final.TryCompleteEntry(sequence.CurrentGraph.StartRoomId);
                foreach(var edge in sequence.CurrentGraph.Route(final.CurrentRoomId,sequence.CurrentGraph.TerminalRoomId))
                {final.TryRequestExit(new ExitIntent(final.Player,edge.From,edge.To));final.TryBeginAcceptedEntry();final.TryCompleteEntry(edge.To);}if(!sequence.TryAdvance(final))break;}
            var life=new RunLifeSnapshot(Guid.NewGuid(),250,new[]{PlayerVitals.Starting,PlayerVitals.Starting},new[]{own.Profile.Id,peer.Profile.Id});
            var checkpoint=RunCheckpoint.Capture(sequence,final,SavedSessionMode.Cooperative,new[]{"weapon.knife","weapon.knife"},life);
            yield return StartPair(levels:true,combatFixture:false,checkpoint:checkpoint,finishHost:own.EndRun,finishClient:peer.EndRun,pending:own.PendingFor,acknowledge:own.Acknowledge);
            var anchor=(Vector2)host.Rooms.CurrentRoom.RunExitAnchor.position;
            host.Players[0].Motor.PlaceAt(anchor+Vector2.left*.6f);host.Players[1].Motor.PlaceAt(anchor+Vector2.right*.6f);
            yield return Frames(5);host.LocalCommands.Interact(new PlayerIdentity(1),false);client.LocalCommands.Interact(new PlayerIdentity(2),false);yield return Frames(5);
            using(new System.IO.FileStream(System.IO.Path.Combine(folder,"Client","progress.lock"),System.IO.FileMode.OpenOrCreate,System.IO.FileAccess.Write,System.IO.FileShare.None))
            {
                host.LocalCommands.Interact(new PlayerIdentity(1),true);client.LocalCommands.Interact(new PlayerIdentity(2),true);
                yield return WaitFor(()=>host.RunEnded && client.HealthFailure!=null && clientFlow.RunEndPending,"client retains a received receipt during storage failure");
                host.Stop();yield return Frames(5);Assert.That(clientFlow.RunEndPending,Is.True);Assert.That(peer.Profile.Experience,Is.Zero);
            }
            yield return WaitFor(()=>clientFlow.State==ApplicationState.Completed,"local receipt retry after host shutdown");
            Assert.That(peer.Profile.Experience,Is.EqualTo(15));Assert.That(peer.EndRunWithCredit(client.Outcome),Is.Zero);
            Assert.That(client.CanLeaveCompletion,Is.True);Assert.That(own.PendingFor(peer.Profile.Id).Count,Is.EqualTo(1),"host retains the unacknowledged journal");
        }

        [UnityTest]
        public IEnumerator DisconnectBeforeAnyReceiptReturnsClientSafelyWithoutInventingRewards()
        {
            int rewards=0;
            yield return StartPair(finishHost:_=>throw new System.IO.IOException("storage unavailable"),finishClient:_=>rewards++);
            // Trusted authority fault fixture: stop delivery before any end receipt is committed.
            host.Life.TryComplete();yield return WaitFor(()=>clientFlow.RunEndPending,"client observes finalization");
            host.Stop();yield return WaitFor(()=>clientFlow.State==ApplicationState.Loading,"receipt-free disconnect returns to menu");
            Assert.That(clientFlow.Destination,Is.EqualTo(ApplicationState.MainMenu));Assert.That(clientFlow.RunEndPending,Is.False);Assert.That(rewards,Is.Zero);
        }

        private void SendClient(string name, PlayerInputMessage input)
        {
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            { writer.WriteValueSafe(input); client.Network.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, writer); }
        }
        private static void SendJson(NetworkManager network, ulong receiver, string name, string json)
        {
            using (var writer = new FastBufferWriter(4096, Allocator.Temp))
            { writer.WriteValueSafe(json); network.CustomMessagingManager.SendNamedMessage(name, receiver, writer, NetworkDelivery.ReliableFragmentedSequenced); }
        }
        private static IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }
        private IEnumerator WaitFor(Func<bool> condition, string operation)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 8;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, operation + "\nHOST: " + host?.Describe() + "\nCLIENT: " + client?.Describe());
        }
    }
}
#endif
