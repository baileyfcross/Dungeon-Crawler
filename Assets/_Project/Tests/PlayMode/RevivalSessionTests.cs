#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SpaceCrawler.Tests.PlayMode
{
    public sealed class RevivalSessionTests
    {
        private MultiplayerSession host, client;
        private ApplicationFlow hostFlow, clientFlow;
        private ProfileProgressStore hostProfile, clientProfile;
        private RunCheckpointStore checkpoints;
        private RunCheckpoint saved;
        private ApplicationScreen screen;
        private bool previousBackground;
        private static ushort port = 18700;
        [UnitySetUp] public IEnumerator SetUp()
        {
            previousBackground = Application.runInBackground;
            yield return SceneManager.LoadSceneAsync("Assets/_Project/Scenes/Gameplay.unity");
            var directory = Path.GetFullPath(Path.Combine("Logs/Validation/Health/Network", Guid.NewGuid().ToString("N")));
            hostProfile = new ProfileProgressStore(Path.Combine(directory, "Host")); clientProfile = new ProfileProgressStore(Path.Combine(directory, "Client"));
            checkpoints = new RunCheckpointStore(Path.Combine(directory, "Host/run.save"));
        }
        [UnityTearDown] public IEnumerator TearDown()
        { if (screen != null) Object.Destroy(screen.gameObject); yield return StopPair(); Application.runInBackground = previousBackground; }
        private IEnumerator StopPair()
        {
            if (client != null) client.Stop(); if (host != null) host.Stop(); yield return Frames(4);
            if (client != null) Object.Destroy(client.gameObject); if (host != null) Object.Destroy(host.gameObject);
            yield return Frames(3);
        }
        private static ApplicationFlow Playing()
        {
            var flow = new ApplicationFlow(); flow.TryRequest(ApplicationCommand.Initialize); flow.CompleteLoading(ApplicationState.MainMenu);
            flow.TryRequest(ApplicationCommand.NewGame); flow.CompleteLoading(ApplicationState.Gameplay); return flow;
        }
        private IEnumerator StartPair(RunCheckpoint checkpoint = null, bool acknowledge = true, bool withScreen = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MultiplayerSession>("Assets/_Project/Prefabs/Gameplay/MultiplayerSession.prefab");
            host = Object.Instantiate(prefab); client = Object.Instantiate(prefab);
            client.Follower.GameplayCamera.enabled = false; client.Follower.GetComponent<AudioListener>().enabled = false;
            host.PollLocalDevices = false; client.PollLocalDevices = false; hostFlow = Playing(); clientFlow = Playing(); var endpoint = ++port;
            host.Bind(SessionRole.Host, hostFlow, port: endpoint, hostSeed: 42, useLevelSequence: true, checkpoint: checkpoint,
                save: value => { checkpoints.Save(value); saved = value; }, profileId: hostProfile.Profile.Id, finish: hostProfile.EndRun,
                pendingRewards: hostProfile.PendingFor, acknowledgeReward: (run, peer) => { if (acknowledge) hostProfile.Acknowledge(run, peer); });
            client.Bind(SessionRole.Client, clientFlow, port: endpoint, profileId: clientProfile.Profile.Id, finish: clientProfile.EndRun);
            if (withScreen)
            {
                Assert.That(client.Players[0], Is.Null);
                screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ApplicationScreen>("Assets/_Project/Prefabs/UI/GameplayScreen.prefab"));
                screen.gameObject.AddComponent<GameAudio>().Bind(Path.Combine("Logs/Validation/Audio/Network",Guid.NewGuid().ToString("N")));
                screen.Bind(clientFlow); screen.BindMultiplayer(client);
                Assert.That(screen.View.Q<Label>("health-status").text, Does.Contain("Waiting"));
                Assert.That(screen.View.Q<ProgressBar>("player-health").style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            yield return Until(() => host.RoomReady && client.RoomReady);
            host.Players[0].Input.HandleFocus(true); client.Players[1].Input.HandleFocus(true);
            hostFlow.TryRequest(ApplicationCommand.Resume); clientFlow.TryRequest(ApplicationCommand.Resume); yield return Frames(5);
        }
        private void PlaceTogether()
        {
            host.Players[0].Motor.PlaceAt(new Vector2(-.5f, 3)); host.Players[1].Motor.PlaceAt(new Vector2(.5f, 3));
            foreach (var rig in host.Players) rig.Motor.CaptureState(); Physics2D.SyncTransforms();
        }
        [UnityTest]
        public IEnumerator ClientOptionsKeepTheHostSimulationRunningAndRestorePauseFocus()
        {
            yield return StartPair(withScreen:true);PlaceTogether();clientFlow.TryRequest(ApplicationCommand.Pause);yield return null;
            var button=screen.View.Q<Button>("options");Assert.That(button.enabledInHierarchy,Is.True);
            using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
            yield return null;Assert.That(screen.GetComponent<AudioOptionsPanel>().IsOpen,Is.True);
            Assert.That(clientFlow.State,Is.EqualTo(ApplicationState.Paused));Assert.That(hostFlow.State,Is.EqualTo(ApplicationState.Gameplay));
            int before=client.Vitals(0).Current;host.DamagePlayer(new PlayerIdentity(1),Guid.NewGuid(),10);
            yield return Until(()=>client.Vitals(0).Current==before-10);
            Assert.That(screen.GetComponent<AudioOptionsPanel>().ConsumePause(),Is.True);yield return null;
            Assert.That(clientFlow.State,Is.EqualTo(ApplicationState.Paused));Assert.That(screen.View.Q<Button>("resume").enabledInHierarchy,Is.True);
        }
        [UnityTest]
        public IEnumerator ClientHealthScreenWaitsForTheHandshakeBeforeReadingPlayerState()
        {
            yield return StartPair(withScreen: true);
            Assert.That(screen.View.Q<ProgressBar>("player-health").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.View.Q<ProgressBar>("player-health").value, Is.EqualTo(100));
            Assert.That(screen.View.Q<Label>("equipped-weapon").text,Does.StartWith("Knife").And.Not.Contain("weapon."));
            host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 30);
            yield return Until(() => screen.View.Q<ProgressBar>("player-health").value == 70);
            Assert.That(screen.View.Q<ProgressBar>("teammate-health").value, Is.EqualTo(100));
            Assert.That(screen.View.Q<Label>("run-readout").text, Does.Contain("Level 1 / 3").And.Not.Contain("fingerprint").And.Not.Contain("epoch"));
            host.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 35);
            yield return Until(() => screen.View.Q<ProgressBar>("teammate-health").value == 65);
        }
        [UnityTest]
        public IEnumerator HeldReviveReplicatesCancelsOnReleaseThenWipeRewardsBothProfilesOnce()
        {
            yield return StartPair(withScreen: true); PlaceTogether(); host.Life.TryAwardRunXp(100);
            Assert.That(client.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 100), Is.False);
            Assert.That(host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100), Is.True);
            yield return Until(() => client.Vitals(1).State == PlayerLifeState.Downed);
            Assert.That(host.Players[1].Simulation.CanAct, Is.False);
            var room = host.CurrentRoom; var destination = host.Progression.Choices[0].To;
            host.Readiness.SetReady(new ExitIntent(new PlayerIdentity(1), room, destination), true);
            host.Readiness.SetReady(new ExitIntent(new PlayerIdentity(2), room, destination), true);
            yield return Frames(5); Assert.That(host.CurrentRoom, Is.EqualTo(room));
            host.Readiness.Reset(host.CurrentRoom, host.Progression.ConnectionsFrom(host.CurrentRoom), true);
            host.LocalCommands.Interact(new PlayerIdentity(1), false); host.LocalCommands.Interact(new PlayerIdentity(1), true);
            yield return Until(() => host.Life.ReviveProgress(0) > 1);
            yield return Until(() => screen.View.Q<ProgressBar>("revive-progress").value > 0);
            Assert.That(screen.View.Q<ProgressBar>("revive-progress").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            host.LocalCommands.Interact(new PlayerIdentity(1), false); yield return Frames(3);
            Assert.That(host.Life.ReviveProgress(0), Is.Zero); Assert.That(host.Life[1].Current, Is.Zero);
            host.LocalCommands.Interact(new PlayerIdentity(1), true);
            yield return Until(() => client.Vitals(1).Current == 50);
            yield return Until(() => screen.View.Q<ProgressBar>("revive-progress").style.display.value == DisplayStyle.None);
            Assert.That(host.Life[1].Current, Is.EqualTo(50));
            // Clear test-supplied readiness before the restored player becomes eligible to leave.
            host.Readiness.Reset(host.CurrentRoom, host.Progression.ConnectionsFrom(host.CurrentRoom), true);
            host.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 1000); host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 1000);
            yield return Until(() => hostFlow.State == ApplicationState.Death && clientFlow.State == ApplicationState.Death);
            yield return Until(() => hostProfile.PendingFor(clientProfile.Profile.Id).Count == 0);
            Assert.That(hostProfile.Profile.Experience, Is.EqualTo(6)); Assert.That(clientProfile.Profile.Experience, Is.EqualTo(6));
            Assert.That(checkpoints.Load(out _, out _), Is.EqualTo(CheckpointLoadStatus.Missing));
            Assert.That(host.Failure ?? client.Failure, Is.Null);
        }
        [UnityTest]
        public IEnumerator DuplicateCurrentRunReceiptsDoNotBlockDeathScreenOrDoubleCredit()
        {
            yield return StartPair(acknowledge: false); host.Life.TryAwardRunXp(100);
            host.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 100); host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100);
            yield return Until(() => hostFlow.State == ApplicationState.Death && clientFlow.State == ApplicationState.Death);
            yield return new WaitForSeconds(1.5f);
            Assert.That(hostProfile.PendingFor(clientProfile.Profile.Id).Count, Is.EqualTo(1));
            Assert.That(clientProfile.Profile.Experience, Is.EqualTo(6)); Assert.That(hostProfile.Profile.Experience, Is.EqualTo(6));
            Assert.That(clientFlow.RunEndPending, Is.False); Assert.That(hostFlow.RunEndPending, Is.False);
            Assert.That(clientFlow.CanRequest(ApplicationCommand.ReturnToMainMenu), Is.True);
            Assert.That(hostFlow.CanRequest(ApplicationCommand.ReturnToMainMenu), Is.True);
        }
        [UnityTest]
        public IEnumerator PendingRewardFromAnOlderRunIsDeliveredWithoutEndingTheNewRun()
        {
            var receipt = new RunEndReceipt(Guid.NewGuid(), 100, RunEndReason.Loss, new[] { hostProfile.Profile.Id, clientProfile.Profile.Id });
            hostProfile.EndRun(receipt);
            yield return StartPair();
            yield return Until(() => clientProfile.Profile.Experience == 6 && hostProfile.PendingFor(clientProfile.Profile.Id).Count == 0);
            Assert.That(hostFlow.State, Is.EqualTo(ApplicationState.Gameplay)); Assert.That(clientFlow.State, Is.EqualTo(ApplicationState.Gameplay));
            Assert.That(host.Life.RunId, Is.Not.EqualTo(receipt.RunId)); Assert.That(host.RunXp, Is.Zero);
            Assert.That(hostProfile.Profile.Experience, Is.EqualTo(6)); Assert.That(clientProfile.Profile.Experience, Is.EqualTo(6));
        }
        [UnityTest] public IEnumerator KeyboardInteractRevivesThroughTheRealInputAdapter() { yield return ReviveWithDevice(false); }
        [UnityTest] public IEnumerator GamepadInteractRevivesThroughTheRealInputAdapter() { yield return ReviveWithDevice(true); }
        private IEnumerator ReviveWithDevice(bool controller)
        {
            var previous = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputDevice device = controller ? (InputDevice)InputSystem.AddDevice<Gamepad>() : InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return StartPair(); PlaceTogether(); host.PollLocalDevices = true;
                host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100);
                if (controller) InputSystem.QueueStateEvent((Gamepad)device, new GamepadState());
                else InputSystem.QueueStateEvent((Keyboard)device, new KeyboardState());
                yield return Frames(5);
                if (controller) InputSystem.QueueStateEvent((Gamepad)device, new GamepadState().WithButton(GamepadButton.South));
                else InputSystem.QueueStateEvent((Keyboard)device, new KeyboardState(Key.E));
                yield return Until(() => client.Vitals(1).Current == 50);
                Assert.That(host.Life[1].State, Is.EqualTo(PlayerLifeState.Alive));
            }
            finally
            {
                if (device.added) InputSystem.RemoveDevice(device);
                InputSystem.settings.editorInputBehaviorInPlayMode = previous;
            }
        }
        [UnityTest]
        public IEnumerator ContinueRestoresDownedPartnerHealthXpAndProfileSlotOwnership()
        {
            yield return StartPair(); host.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 23);
            host.Life.TryAwardRunXp(180); host.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100);
            var snapshot = saved; var id = host.Life.RunId;
            Assert.That(snapshot.Life.Players[1].State, Is.EqualTo(PlayerLifeState.Downed));
            yield return StopPair(); yield return StartPair(snapshot);
            Assert.That(host.Life.RunId, Is.EqualTo(id)); Assert.That(host.Life[0].Current, Is.EqualTo(77));
            Assert.That(host.Life.RunXp, Is.EqualTo(180)); Assert.That(client.Vitals(1).State, Is.EqualTo(PlayerLifeState.Downed));
            Assert.That(client.Players[1].Simulation, Is.Null); Assert.That(hostProfile.Profile.Experience, Is.Zero);
        }
        private static IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }
        private IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 12;
            while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), host?.Describe() + "\n" + client?.Describe()); yield return null; }
            yield return null;
        }
    }
}
#endif
