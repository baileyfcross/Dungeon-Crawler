#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RoomTraversalTests
    {
        private const string SessionPath = "Assets/_Project/Prefabs/Gameplay/RoomGameplaySession.prefab";
        private GameplaySession session;
        private ApplicationRoot root;
        private ApplicationFlow flow;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.EditorInputBehaviorInPlayMode previousInput;
        private RoomLoader Rooms => session.Rooms;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            // A prior Boot/menu test can leave the menu camera behind. Isolate direct prefab
            // tests in the real empty Gameplay scene instead of overlapping its AudioListener.
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity", LoadSceneMode.Single);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = previousInput;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (root != null) Object.Destroy(root.gameObject);
            if (session != null) Object.Destroy(session.gameObject);
            foreach (var screen in Object.FindObjectsByType<ApplicationScreen>(FindObjectsSortMode.None)) Object.Destroy(screen.gameObject);
            yield return null;
        }

        private IEnumerator StartSession(ulong seed, string weapon = "Knife")
        {
            session = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>(SessionPath));
            session.Rooms.UseCombatFixture = true;
            var data = new SerializedObject(session);
            data.FindProperty("startingWeapon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"Assets/_Project/Data/Weapons/{weapon}.asset");
            data.ApplyModifiedPropertiesWithoutUndo();
            flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay);
            session.Bind(new PlayerIdentity(1), flow, seed); session.Input.HandleFocus(true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Frames(4);
            Assert.That(session.Input.GameplayInputEnabled, Is.True);
            Assert.That(session.Input.MovementNeedsNeutral, Is.False);
        }

        [UnityTest]
        public IEnumerator BootNewGameLoadsCorrectStartingPrefabAndMenuDestroysEntireRun()
        {
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Boot.unity", LoadSceneMode.Single);
            yield return WaitFor(() => (root = Object.FindFirstObjectByType<ApplicationRoot>()) != null && root.Flow.State == ApplicationState.MainMenu);
            root.Flow.TryRequest(ApplicationCommand.NewGame);
            yield return WaitFor(() => root.Flow.State == ApplicationState.Gameplay);
            session = root.CurrentGameplay; flow = root.Flow; session.Input.HandleFocus(true);
            Assert.That(Rooms.Progression.CurrentRoomId, Is.EqualTo(session.Run.StartRoomId));
            Assert.That(Rooms.CurrentPrefab, Is.SameAs(AssetDatabase.LoadAssetAtPath<RoomDefinition>("Assets/_Project/Data/Rooms/Starting.asset").RoomPrefab));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/_Project/Scenes/Gameplay.unity"));
            VerifyCurrentRoom();
            var readout = root.CurrentScreen.View.Q<Label>("run-readout").text;
            Assert.That(readout, Does.Contain("room-000").And.Contain("room.starting").And.Contain(session.Run.Seed.ToString()));
            while (session.LevelNumber < RunSequence.LevelCount || !Rooms.Progression.IsTerminal)
            {
                yield return Frames(3);
                var level = session.LevelNumber;
                Assert.That(session.Run.Nodes.Count, Is.InRange(10, 25));
                var destination = session.Run.Route(Rooms.Progression.CurrentRoomId,session.Run.TerminalRoomId)[0].To;
                var nextLevel = session.Run.GetRoom(destination).IsTerminal && level < RunSequence.LevelCount;
                var exit = Rooms.CurrentRoom.ExitFor(Rooms.Progression.Choices.First(e => e.To.Equals(destination)),Rooms.Progression.Choices);
                session.Motor.PlaceAt(exit.transform.position);
                yield return WaitFor(() => nextLevel ? session.LevelNumber == level + 1 && Rooms.Progression.CurrentRoomId.Value == 0
                    : Rooms.Progression.CurrentRoomId.Equals(destination));
                Assert.That(session.LevelNumber, Is.EqualTo(nextLevel ? level + 1 : level));
                VerifyCurrentRoom();
            }
            Assert.That(session.LevelNumber, Is.EqualTo(3));
            Assert.That(root.CurrentScreen.View.Q<Label>("run-readout").text,
                Does.Contain("Terminal room reached").And.Contain("Connected rooms remain available").And.Not.Contain("has been defined"));
            Assert.That(root.Flow.State, Is.EqualTo(ApplicationState.Gameplay));
            Assert.That(session.Input.GameplayInputEnabled, Is.True);
            root.Flow.TryRequest(ApplicationCommand.ReturnToMainMenu);
            yield return WaitFor(() => root.Flow.State == ApplicationState.MainMenu);
            Assert.That(session == null, Is.True);
            Assert.That(Object.FindObjectsByType<AuthoredRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<CombatProjectile>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            Assert.That(root.CurrentGameplay, Is.Null);
        }

        [UnityTest]
        public IEnumerator PhysicalTriggerOnlySubmitsIdentifiedRequestAndCannotBypassAuthority()
        {
            yield return StartSession(42);
            var origin = Rooms.Progression.CurrentRoomId; var room = Rooms.CurrentRoom;
            var edge = Rooms.Progression.Choices[1]; var exit = room.RightExit;
            var requests = 0; ExitIntent observed = default;
            exit.Configure(session.Simulation.State.Player, session.Motor.Body, edge, intent => { requests++; observed = intent; return false; });
            session.Motor.PlaceAt(exit.transform.position);
            yield return new WaitForFixedUpdate(); yield return Frames(2);
            Assert.That(requests, Is.GreaterThan(0));
            Assert.That(observed.Player, Is.EqualTo(session.Simulation.State.Player));
            Assert.That(observed.CurrentRoom, Is.EqualTo(origin)); Assert.That(observed.Destination, Is.EqualTo(edge.To));
            Assert.That(Rooms.CurrentRoom, Is.SameAs(room)); Assert.That(Rooms.Progression.CurrentRoomId, Is.EqualTo(origin));
            Assert.That(Rooms.RequestExit(new ExitIntent(new PlayerIdentity(2), origin, edge.To)), Is.False);
            Assert.That(Rooms.RequestExit(new ExitIntent(observed.Player, edge.To, edge.To)), Is.False);
            Assert.That(Rooms.RequestExit(new ExitIntent(observed.Player, origin, session.Run.TerminalRoomId)), Is.False);
            Assert.That(exit.TryEnter(room.Targets[0].HitColliders[0]), Is.False, "A target collider is not the controlled player's Rigidbody.");
            exit.Configure(observed.Player, session.Motor.Body, edge, Rooms.RequestExit);
            yield return WaitFor(() => Rooms.Progression.CurrentRoomId.Equals(edge.To));
            Assert.That(Rooms.Progression.RoomsEntered, Is.EqualTo(2));
            Assert.That(Rooms.RequestExit(observed), Is.False, "Old-room callbacks are stale after entry.");
            VerifyCurrentRoom();
        }

        [UnityTest]
        public IEnumerator FourCompletePhysicalRoutesCoverSingleBothBranchArmsRejoinsAndTerminal()
        {
            var seeds = new[] { 0UL, 42UL, 42UL, 2026UL };
            for (var route = 0; route < seeds.Length; route++)
            {
                yield return StartSession(seeds[route], route == 3 ? "Blaster" : "Knife");
                var graph = session.Run; var progression = Rooms.Progression; var player = session.Simulation;
                var motor = session.Motor; var camera = session.Follower; var combat = session.Combat.Simulation; var weapon = combat.EquippedWeapon;
                var singleCount = 0; var splitCount = 0; var transitions = 0;
                while (!progression.IsTerminal)
                {
                    VerifyCurrentRoom();
                    Assert.That(session.Run, Is.SameAs(graph)); Assert.That(session.Simulation, Is.SameAs(player));
                    Assert.That(session.Motor, Is.SameAs(motor)); Assert.That(session.Follower, Is.SameAs(camera));
                    Assert.That(session.Combat.Simulation, Is.SameAs(combat)); Assert.That(combat.EquippedWeapon, Is.SameAs(weapon));
                    // Damage is optional technical content, never an exit gate.
                    combat.AdvanceTime(10);
                    Assert.That(player.RequestAim(new AimIntent(player.State.Player, CoreVector.UnitX)), Is.True);
                    Assert.That(session.Combat.RequestAttack(new AttackIntent(player.State.Player, CoreVector.UnitX)), Is.True);
                    if (route != 3) Assert.That(session.Combat.Targets[0].Health.Current, Is.EqualTo(75));
                    else
                    {
                        yield return WaitFor(() => session.Combat.Projectiles.Count == 0);
                        Assert.That(session.Combat.Targets[0].Health.Current, Is.EqualTo(80));
                    }
                    var choices = progression.Choices;
                    if (choices.Count == 1) singleCount++; else splitCount++;
                    var index = route == 2 ? choices.Count - 1 : 0;
                    var destination = choices[index].To;
                    var exit = choices.Count == 1 ? Rooms.CurrentRoom.SingleExit : index == 0 ? Rooms.CurrentRoom.LeftExit : Rooms.CurrentRoom.RightExit;
                    var oldRoom = Rooms.CurrentRoom; var oldTargets = session.Combat.Targets.ToArray();
                    session.Motor.PlaceAt(exit.transform.position);
                    player.RequestMove(new MoveIntent(player.State.Player, CoreVector.UnitY));
                    session.Motor.Body.linearVelocity = Vector2.up * 5;
                    CombatProjectile oldProjectile = null;
                    if (route == 3)
                    {
                        combat.AdvanceTime(10);
                        Assert.That(player.RequestAim(new AimIntent(player.State.Player, -CoreVector.UnitY)), Is.True);
                        Assert.That(session.Combat.RequestAttack(new AttackIntent(player.State.Player, -CoreVector.UnitY)), Is.True);
                        oldProjectile = session.Combat.Projectiles[0];
                    }
                    yield return WaitFor(() => progression.CurrentRoomId.Equals(destination));
                    transitions++;
                    Assert.That(oldRoom == null, Is.True); Assert.That(oldTargets.All(t => t == null), Is.True);
                    Assert.That(session.Combat.Projectiles, Is.Empty);
                    if (route == 3) Assert.That(oldProjectile == null, Is.True, "Old-room projectile must be destroyed, not carried into new space.");
                    Assert.That(session.Combat.Targets.All(t => t.Health.Current == 100), Is.True);
                    Assert.That(session.Combat.RegisteredColliderCount, Is.EqualTo(Rooms.CurrentRoom.CombatTargets.Sum(target => target.HitColliders.Length)));
                    VerifyCurrentRoom();
                    yield return Frames(3);
                    Assert.That(session.Input.GameplayInputEnabled, Is.True);
                    Assert.That(session.Input.MovementNeedsNeutral, Is.False);
                }
                Assert.That(singleCount, Is.GreaterThan(0)); Assert.That(splitCount, Is.GreaterThan(0));
                Assert.That(transitions + 1, Is.InRange(9, 11));
                Assert.That(progression.CurrentRoomId, Is.EqualTo(graph.TerminalRoomId));
                Assert.That(progression.State, Is.EqualTo(RoomFlowState.Terminal));
                VerifyCurrentRoom();
                Assert.That(session.Input.GameplayInputEnabled, Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * 0.7f, Screen.height * 0.5f) });
                yield return new WaitForSeconds(0.15f);
                Assert.That(session.Motor.Body.linearVelocity.y, Is.EqualTo(5).Within(0.01f));
                Assert.That(player.State.AimDirection.X, Is.GreaterThan(0.99f));
                combat.AdvanceTime(10); Assert.That(player.RequestAim(new AimIntent(player.State.Player, CoreVector.UnitX)), Is.True);
                Assert.That(session.Combat.RequestAttack(new AttackIntent(player.State.Player, CoreVector.UnitX)), Is.True);
                flow.TryRequest(ApplicationCommand.Pause); yield return new WaitForFixedUpdate();
                Assert.That(session.Motor.Body.linearVelocity, Is.EqualTo(Vector2.zero));
                flow.TryRequest(ApplicationCommand.Resume); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                TestContext.WriteLine($"seed={seeds[route]} branchPreference={(route == 2 ? "right" : "left")} rooms={progression.RoomsEntered} transitions={transitions} single={singleCount} split={splitCount} terminal={progression.CurrentRoomId}");
                Object.Destroy(session.gameObject); yield return Frames(2);
                Assert.That(Object.FindObjectsByType<AuthoredRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
            }
        }

        [UnityTest]
        public IEnumerator PausedExitAndFocusLossDoNotCommitAndHeldMovementRequiresNeutralAfterTransition()
        {
            yield return StartSession(42);
            var origin = Rooms.Progression.CurrentRoomId; var exit = Rooms.CurrentRoom.LeftExit;
            flow.TryRequest(ApplicationCommand.Pause);
            session.Motor.PlaceAt(exit.transform.position);
            yield return new WaitForFixedUpdate(); yield return Frames(3);
            Assert.That(Rooms.Progression.CurrentRoomId, Is.EqualTo(origin));
            session.Input.HandleFocus(false); session.Input.HandleFocus(true);
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Paused));
            Assert.That(Rooms.Progression.CurrentRoomId, Is.EqualTo(origin));
            session.Motor.PlaceAt(Vector2.zero);
            flow.TryRequest(ApplicationCommand.Resume); yield return Frames(4);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return Frames(3);
            session.Motor.PlaceAt(exit.transform.position);
            yield return WaitFor(() => !Rooms.Progression.CurrentRoomId.Equals(origin));
            yield return Frames(3);
            Assert.That(session.Input.MovementNeedsNeutral, Is.True);
            Assert.That(session.Motor.Body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(session.Motor.Body.position, Is.EqualTo((Vector2)Rooms.CurrentRoom.PlayerSpawnAnchor.position));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); yield return new WaitForSeconds(0.1f);
            Assert.That(session.Motor.Body.linearVelocity.x, Is.EqualTo(5).Within(0.01f));
            session.Input.HandleFocus(false); yield return new WaitForFixedUpdate();
            Assert.That(flow.State, Is.EqualTo(ApplicationState.Paused)); Assert.That(session.Motor.Body.linearVelocity, Is.EqualTo(Vector2.zero));
        }

        [UnityTest]
        public IEnumerator MissingPrefabMappingFailsExplicitlyWithoutEnablingGameplayOrLoadingAnArbitraryRoom()
        {
            session = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplaySession>(SessionPath));
            var generation = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>("Assets/_Project/Data/Runs/DefaultRun.asset"));
            var definition = Object.Instantiate(generation.RoomDefinitions[0]);
            try
            {
                var roomData = new SerializedObject(definition); roomData.FindProperty("roomPrefab").objectReferenceValue = null; roomData.ApplyModifiedPropertiesWithoutUndo();
                var generationData = new SerializedObject(generation); generationData.FindProperty("roomDefinitions").GetArrayElementAtIndex(0).objectReferenceValue = definition; generationData.ApplyModifiedPropertiesWithoutUndo();
                var data = new SerializedObject(session); data.FindProperty("generation").objectReferenceValue = generation; data.ApplyModifiedPropertiesWithoutUndo();
                flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
                flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay);
                Assert.Throws<InvalidOperationException>(() => session.Bind(new PlayerIdentity(1), flow, 42));
                Assert.That(Rooms.CurrentRoom, Is.Null); Assert.That(Rooms.RoomContainer.childCount, Is.Zero);
                Assert.That(session.Input.GameplayInputEnabled, Is.False);
                yield return null;
            }
            finally { Object.Destroy(generation); Object.Destroy(definition); }
        }

        private void VerifyCurrentRoom()
        {
            var room = Rooms.CurrentRoom; var choices = Rooms.Progression.Choices;
            Assert.That(room, Is.Not.Null); Assert.That(Rooms.RoomContainer.childCount, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AuthoredRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            var node = session.Run.GetRoom(Rooms.Progression.CurrentRoomId);
            if (!Rooms.UseCombatFixture)
            {
                Assert.That(room.Targets, Is.Empty, "Normal play must not contain stationary combat fixtures.");
                Assert.That(room.GetComponentsInChildren<DamageableTarget>(true).Length, Is.EqualTo(room.Encounter.Enemies.Count));
            }
            Assert.That(room.Encounter.Enemies.Count, Is.EqualTo(node.Category == RoomCategory.Engine && !node.IsTerminal ? 1 : 0),
                "Only nonterminal Engine rooms receive the authored melee encounter.");
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>($"Assets/_Project/Data/Rooms/{node.Category}.asset");
            Assert.That(Rooms.CurrentPrefab, Is.SameAs(definition.RoomPrefab));
            if (session.Run.GenerationVersion == 1) Assert.That(session.Motor.Body.position, Is.EqualTo((Vector2)room.PlayerSpawnAnchor.position));
            else Assert.That(room.IsSafePosition(session.Motor.Body.position),Is.True);
            Assert.That(session.Simulation.State.Position, Is.EqualTo(new CoreVector(session.Motor.Body.position.x,session.Motor.Body.position.y)));
            Assert.That(session.Motor.Body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(Vector2.Distance(session.Follower.transform.position, session.Motor.transform.position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(session.Follower.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            if (session.Run.GenerationVersion >= 2)
            {
                Assert.That(new[] { room.SingleExit,room.LeftExit,room.RightExit }.Any(e => e.gameObject.activeSelf),Is.False);
                Assert.That(room.CardinalExits.Count(e => e.gameObject.activeSelf),Is.EqualTo(choices.Count));
                foreach (var edge in choices) Assert.That(room.ExitFor(edge,choices).Destination,Is.EqualTo(edge.To));
                return;
            }
            Assert.That(room.SingleExit.gameObject.activeSelf, Is.EqualTo(choices.Count == 1));
            Assert.That(room.LeftExit.gameObject.activeSelf, Is.EqualTo(choices.Count == 2));
            Assert.That(room.RightExit.gameObject.activeSelf, Is.EqualTo(choices.Count == 2));
            if (choices.Count == 1) Assert.That(room.SingleExit.Destination, Is.EqualTo(choices[0].To));
            if (choices.Count == 2)
            { Assert.That(room.LeftExit.Destination, Is.EqualTo(choices[0].To)); Assert.That(room.RightExit.Destination, Is.EqualTo(choices[1].To)); }
        }
        private static IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }
        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var until = Time.realtimeSinceStartup + 10;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(until)); yield return null; }
            yield return Frames(2);
        }
    }
}
#endif
