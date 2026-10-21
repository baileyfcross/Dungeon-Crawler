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
        private ApplicationFlow hostFlow, clientFlow;
        private bool previousBackground;
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
        private IEnumerator StartPair(bool blaster = false, bool levels = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab");
            host = Object.Instantiate(prefab); client = Object.Instantiate(prefab);
            // One rendered view/audio listener in this test process; both follower states still update.
            client.Follower.GameplayCamera.enabled = false;
            client.Follower.GetComponent<AudioListener>().enabled = false;
            if (blaster)
            {
                var data = new SerializedObject(host); data.FindProperty("useBlaster").boolValue = true; data.ApplyModifiedPropertiesWithoutUndo();
            }
            host.PollLocalDevices = false; client.PollLocalDevices = false;
            hostFlow = Playing(); clientFlow = Playing(); var endpoint = ++port;
            host.Bind(SessionRole.Host, hostFlow, port: endpoint, hostSeed: 42, useLevelSequence: levels);
            // A competing seed argument is deliberately ignored for the client role.
            client.Bind(SessionRole.Client, clientFlow, port: endpoint, hostSeed: 999);
            yield return WaitFor(() => host.RoomReady && client.RoomReady, "two-peer room handshake");
            host.Players[0].Input.HandleFocus(true); client.Players[1].Input.HandleFocus(true);
            hostFlow.TryRequest(ApplicationCommand.Resume); clientFlow.TryRequest(ApplicationCommand.Resume);
            yield return Frames(3);
            Assert.That(host.Failure ?? client.Failure, Is.Null);
            Assert.That(host.LocalPlayer, Is.EqualTo(1)); Assert.That(client.LocalPlayer, Is.EqualTo(2));
            Assert.That(host.Run.Seed, Is.EqualTo(42)); Assert.That(client.Run.Seed, Is.EqualTo(host.Run.Seed));
            Assert.That(client.Fingerprint, Is.EqualTo(host.Fingerprint)); Assert.That(client.Progression, Is.Null);
            Assert.That(client.Authority, Is.Null); Assert.That(host.Network.ConnectedClientsIds.Count, Is.EqualTo(2));
            CheckRoomAndSpawns();
        }
        private void CheckRoomAndSpawns()
        {
            Assert.That(client.CurrentRoom, Is.EqualTo(host.CurrentRoom));
            Assert.That(host.Rooms.RoomContainer.childCount, Is.EqualTo(1)); Assert.That(client.Rooms.RoomContainer.childCount, Is.EqualTo(1));
            var room = host.Rooms.CurrentRoom;
            Assert.That(Vector2.Distance(host.Players[0].Motor.Body.position, room.PlayerOneSpawnAnchor.position), Is.LessThan(.01));
            Assert.That(Vector2.Distance(host.Players[1].Motor.Body.position, room.PlayerTwoSpawnAnchor.position), Is.LessThan(.01));
            Assert.That(Vector2.Distance(host.Players[0].Motor.Body.position, host.Players[1].Motor.Body.position), Is.GreaterThan(1.2));
            Assert.That(Physics2D.GetIgnoreCollision(host.Players[0].Colliders[0], host.Players[1].Colliders[0]), Is.True);
            foreach (var rig in client.Players) { Assert.That(rig.Simulation, Is.Null); Assert.That(rig.Motor.Body.simulated, Is.False); }
            foreach (var rig in host.Players) Assert.That(rig.Combat.Targets, Is.EquivalentTo(room.Targets));
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
            var accepted = new NetworkManager.ConnectionApprovalResponse();
            host.Network.ConnectionApprovalCallback(request, accepted);
            Assert.That(accepted.Approved, Is.True);
            Assert.That(host.Authority.BothConnected, Is.False, "Approval alone must not assign an authoritative player.");
        }

        [UnityTest]
        public IEnumerator SharedLevelBoundariesResetRoomsWithoutReplacingPlayersOrEquipment()
        {
            yield return StartPair(levels: true);
            var originalPlayers = host.Players.ToArray();
            var weapon = host.Players[0].Combat.Simulation.EquippedWeapon;
            var lastEpoch = host.Epoch;
            while (host.LevelNumber < 3 || !host.Progression.IsTerminal)
            {
                Assert.That(host.Run.Nodes.Count, Is.InRange(10, 17));
                var level = host.LevelNumber;
                var destination = host.Progression.Choices[0].To;
                var nextLevel = host.Run.GetRoom(destination).IsTerminal && level < 3;
                var exit = host.Progression.Choices.Count == 1 ? host.Rooms.CurrentRoom.SingleExit : host.Rooms.CurrentRoom.LeftExit;
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
                    SendClient("session.input.v3", new PlayerInputMessage { Epoch = lastEpoch - 1, ClaimedPlayer = 2,
                        Sequence = 9999, AttackSequence = 9999, Active = true, Move = Vector2.up, Aim = Vector2.up });
                    SendJson(client.Network, host.Network.LocalClientId, "session.room-ready.v3", JsonUtility.ToJson(
                        new RoomLoadAcknowledgment { epoch = lastEpoch, level = level, room = 0, fingerprint = host.Fingerprint }));
                    yield return WaitFor(() => host.Authority.RejectedStale > rejected, "prior-level input rejected");
                    Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1), "Old-level acknowledgment cannot commit entry again.");
                }
            }
            Assert.That(host.LevelNumber, Is.EqualTo(3));
            Assert.That(host.RoomReady && client.RoomReady, Is.True);
            Assert.That(host.Progression.Choices, Is.Empty);
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
            SendClient("session.input.v3", new PlayerInputMessage { ClaimedPlayer = 1, Epoch = host.Epoch, Sequence = 9999,
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
            Assert.That(host.Progression.Choices, Is.Empty);
            Assert.That(new[] { host.Rooms.CurrentRoom.SingleExit, host.Rooms.CurrentRoom.LeftExit, host.Rooms.CurrentRoom.RightExit }.Any(e => e.gameObject.activeSelf), Is.False);
            Assert.That(client.Describe(), Does.Contain("Terminal room reached"));
            Move(client, CoreVector.UnitX); Aim(client, CoreVector.UnitY); Attack(client);
            yield return new WaitForSeconds(.15f); Assert.That(host.Players[1].Simulation.DesiredVelocity.X, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator BlasterProjectilesAreHostOnlyAndCleanupOnSharedEntry()
        {
            yield return StartPair(true); Assert.That(client.WeaponName, Is.EqualTo("weapon.blaster"));
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
            var forged = new RunSessionDescriptor { epoch = host.Epoch + 1, seed = "999", fingerprint = "forged", destination = 10, generationVersion = 1, weapon = "weapon.knife" };
            SendJson(client.Network, host.Network.LocalClientId, "session.run.v3", JsonUtility.ToJson(forged));
            yield return new WaitForSeconds(.15f);
            Assert.That(host.Run.Seed, Is.EqualTo(42)); Assert.That(host.CurrentRoom, Is.EqualTo(initial)); Assert.That(host.Progression.RoomsEntered, Is.EqualTo(1));
            // Fault injection from the real server transport, deliberately corrupting graph agreement.
            SendJson(host.Network, client.Network.LocalClientId, "session.run.v3", JsonUtility.ToJson(forged));
            yield return WaitFor(() => client.Failure != null, "mismatching descriptor rejection");
            Assert.That(client.Failure, Does.Contain("mismatch")); Assert.That(client.Run.Seed, Is.EqualTo(42));
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
