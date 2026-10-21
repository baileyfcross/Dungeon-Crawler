#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in two-process fixtures. Traversal uses ordinary player commands; health fixtures
    /// explicitly simulate damage/XP through host APIs without enabling a gameplay reward source. Absent in release.</summary>
    public sealed class SessionValidationDriver : MonoBehaviour
    {
        private MultiplayerSession session;
        private ApplicationFlow flow;
        private double began;
        private bool cleanupShot, observedReplicaShot;
        private bool Host => session.Mode == SessionRole.Host;
        private NetworkPlayerRig Local => session.Players[session.LocalPlayer - 1];
        private CoreVector Position => Local.State.Position;

        public static EnemySpawn[] CreateEnemySpawns(AuthoredRoom room, int count = 1)
        {
            if (count < 1 || count > 32) throw new ArgumentOutOfRangeException(nameof(count));
            var spawns = new EnemySpawn[count];
            for (var i = 0; i < count; i++)
            {
                var anchor = new GameObject("EnemySpawn").transform;
                anchor.SetParent(room.ContentRoot);
                anchor.position = count == 1 ? new Vector3(0, 5, 0) : new Vector3((i % 8 - 3.5f) * 1.5f, 5 + i / 8 * 1.5f, 0);
                spawns[i] = new EnemySpawn(i + 1, room.Targets[0], anchor);
            }
            return spawns;
        }

        public void Bind(MultiplayerSession value, ApplicationFlow localFlow)
        { session = value; flow = localFlow; session.PollLocalDevices = false; StartCoroutine(Run()); }
        private void Move(CoreVector direction)
        { if (session.LocalCommands.InputActive) session.LocalCommands.Move(new MoveIntent(Local.State.Player, direction)); }
        private void Aim(CoreVector direction) => session.LocalCommands.Aim(new AimIntent(Local.State.Player, direction));
        private void Attack() => session.LocalCommands.Attack(new AttackIntent(Local.State.Player, Local.State.AimDirection));
        private void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Probe] FAILED " + message + "\n" + session.Describe()); }
        private void Record(string checkpoint) => Debug.Log("[Probe] " + checkpoint + "\n" + session.Describe(), this);
        private IEnumerator At(double elapsed)
        { while (Time.realtimeSinceStartupAsDouble - began < elapsed) yield return null; }
        private IEnumerator Until(Func<bool> condition, string operation)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Check(condition(), operation);
        }
        private IEnumerator Walk(float x, float y)
        {
            var room = session.CurrentRoom; var level = session.LevelNumber; var deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (session.LevelNumber == level && session.CurrentRoom.Equals(room))
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) { Check(false, "movement timed out"); yield break; }
                var delta = new CoreVector(x, y) - Position;
                if (Math.Abs(delta.X) <= .18f && Math.Abs(delta.Y) <= .18f) break;
                if (session.RoomReady) Move(Math.Abs(delta.X) > .18f ? new CoreVector(Math.Sign(delta.X), 0) : new CoreVector(0, Math.Sign(delta.Y)));
                yield return null;
            }
            Move(CoreVector.Zero);
        }
        private void CheckSpawns()
        {
            var room = session.Rooms.CurrentRoom;
            for (var i = 0; i < 2; i++)
            {
                var anchor = i == 0 ? room.PlayerOneSpawnAnchor : room.PlayerTwoSpawnAnchor;
                Check(CoreVector.Distance(session.Players[i].State.Position, new CoreVector(anchor.position.x, anchor.position.y)) < .22f, "distinct mapped room spawn");
            }
        }
        private void Update()
        {
            if (session == null || session.Run == null || session.WeaponName != "weapon.blaster" || session.Players[1] == null) return;
            if (Host && !cleanupShot && session.CurrentRoom.Value == 0 && Time.realtimeSinceStartupAsDouble - began > 9
                && session.Players[1].State.Position.X < -4.5f && session.Players[1].State.Position.Y > 11.25f)
            { cleanupShot = true; Aim(-CoreVector.UnitY); Attack(); Record("projectile-live-before-shared-entry"); }
            if (!Host && !observedReplicaShot && session.ReplicaProjectileCount > 0)
            { observedReplicaShot = true; Record("client-projectile-presentation-only"); }
        }
        private IEnumerator Run()
        {
            yield return Until(() => session.RoomReady, "initial room handshake");
            if (SessionDiagnostics.Option("--session-validation") == "enemies")
            { yield return RunEnemies(); yield break; }
            var mode = SessionDiagnostics.Option("--session-validation");
            if (mode == "health" || mode == "health-checkpoint" || mode == "health-resume")
            { yield return RunHealth(mode); yield break; }
            if (SessionDiagnostics.Option("--session-validation") == "resume")
                Check(session.LevelNumber == 2 && session.CurrentRoom.Value != 0, "cooperative checkpoint restored on both peers");
            Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume); began = Time.realtimeSinceStartupAsDouble;
            yield return new WaitForSeconds(.15f);
            CheckSpawns(); Record("01-joined-distinct-spawns-and-graph");
            yield return At(.6);
            Aim(Host ? CoreVector.UnitY : -CoreVector.UnitY);
            yield return Walk(Host ? -3 : 3, .75f);
            yield return At(2);
            Check(session.Players[0].State.Position.X < -2.7f && session.Players[1].State.Position.X > 2.7f, "independent replicated movement");
            Check(session.Players[0].State.AimDirection.Y > .99f && session.Players[1].State.AimDirection.Y < -.99f, "independent replicated aim");
            Check(Vector2.Distance(session.Follower.transform.position, Local.Motor.transform.position) < .02f, "local camera ownership");
            Check(session.Follower.transform.eulerAngles == Vector3.zero, "fixed camera orientation");
            Record("02-independent-movement-aim-cameras");
            var target = session.Rooms.CurrentRoom.Targets[Host ? 1 : 0].transform.position;
            Aim(new CoreVector(target.x, target.y) - Position); Attack(); Attack();
            yield return At(2.65);
            var expectedHealth = session.WeaponName == "weapon.knife" ? 75 : 80;
            foreach (var fixture in session.Rooms.CurrentRoom.Targets.Take(2))
                Check((Host ? fixture.Health.Current : fixture.DisplayedHealth) == expectedHealth, "authoritative two-player damage agreement");
            foreach (var rig in session.Players)
                Check((Host ? rig.Combat.Simulation.AcceptedAttackCount : rig.LastSnapshot.Attacks) == 1, "cooldown rejects duplicate press");
            Record("03-both-attacks-health-and-cooldown");
            if (Host) flow.TryRequest(ApplicationCommand.Pause); else Move(CoreVector.UnitY);
            yield return At(3.25); if (!Host) Move(CoreVector.Zero);
            yield return At(3.4);
            Check(session.Players[1].State.Position.Y > 2, "client moves while host UI paused");
            Check(session.Players[0].State.Velocity.LengthSquared() < .01f, "paused host input neutral");
            Record("04-host-local-pause-keeps-client-running");
            if (Host) flow.TryRequest(ApplicationCommand.Resume);
            yield return At(4);
            if (!Host) Local.Input.HandleFocus(false); else Move(CoreVector.UnitY);
            yield return At(4.6); if (Host) Move(CoreVector.Zero);
            yield return At(4.8);
            Check(session.Players[0].State.Position.Y > 2, "host moves while client focus lost");
            Check(session.Players[1].State.Velocity.LengthSquared() < .01f, "unfocused client neutral");
            Record("05-client-focus-loss-is-local");
            if (!Host) { Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume); }
            yield return At(5);
            var testedSplit = false;
            var entries = Host ? session.Progression.RoomsEntered : 1;
            var transitions = 0;
            while (session.LevelNumber < RunSequence.LevelCount || !session.Run.GetRoom(session.CurrentRoom).IsTerminal)
            {
                Check(++transitions <= 60, "bounded three-level route");
                var before = session.CurrentRoom; var level = session.LevelNumber;
                Check(session.Run.Nodes.Count >= 10 && session.Run.Nodes.Count <= 17, "total room bounds");
                var choices = session.Run.Connections.Where(c => c.From.Equals(before)).ToArray();
                var index = before.Value % choices.Length; var destination = choices[index].To;
                var advances = level < 3 && session.Run.GetRoom(destination).IsTerminal;
                if (!testedSplit && choices.Length == 2)
                {
                    yield return Walk(Host ? -6 : 6, 13);
                    yield return Until(() => session.Players[0].State.Position.X < -5.7f && session.Players[0].State.Position.Y > 12.7f
                        && session.Players[1].State.Position.X > 5.7f && session.Players[1].State.Position.Y > 12.7f, "different physical exits reached");
                    yield return new WaitForSeconds(.3f);
                    Check(session.LevelNumber == level && session.CurrentRoom.Equals(before), "different exits cannot transition");
                    Record("06-different-exits-no-transition");
                    index = 0; destination = choices[0].To; testedSplit = true;
                    if (!Host) { yield return Walk(6, 11); yield return Walk(-6, 11); yield return Walk(-6, 13); }
                }
                else yield return Walk(choices.Length == 1 ? 0 : index == 0 ? -6 : 6, 13);
                yield return Until(() => session.LevelNumber == (advances ? level + 1 : level)
                    && session.CurrentRoom.Equals(advances ? new RunRoomId(0) : destination) && session.RoomReady, "shared forward level/room transition");
                yield return new WaitForSeconds(.15f); CheckSpawns();
                entries = advances ? 1 : entries + 1;
                if (Host) Check(session.Progression.RoomsEntered == entries, "exactly one Core entry commitment");
                Check(session.Rooms.RoomContainer.childCount == 1, "old room cleanup");
                Check(Host ? session.Players.Sum(p => p.Combat.Projectiles.Count) == 0 : session.ReplicaProjectileCount == 0, "room projectiles cleaned");
                if (advances) Record("LEVEL-BOUNDARY-PASS-" + session.LevelNumber);
                Record("09-route-level-" + session.LevelNumber + "-room-" + session.CurrentRoom.Value);
                if (SessionDiagnostics.Option("--session-validation") == "checkpoint" && session.LevelNumber == 2 && session.CurrentRoom.Value != 0)
                {
                    Record("CHECKPOINT-PASS");
                    while (true) yield return null; // Harness terminates both processes to test durable restoration.
                }
                yield return new WaitForSeconds(.25f);
            }
            Check(testedSplit, "split readiness exercised");
            Check(!new[] { session.Rooms.CurrentRoom.SingleExit, session.Rooms.CurrentRoom.LeftExit, session.Rooms.CurrentRoom.RightExit }.Any(e => e.gameObject.activeSelf), "Terminal has no exits");
            Aim(CoreVector.UnitY); Attack(); Move(Host ? -CoreVector.UnitX : CoreVector.UnitX);
            yield return new WaitForSeconds(.25f); Move(CoreVector.Zero);
            yield return new WaitForSeconds(.3f);
            Record("10-PASS-terminal-remains-playable");
            if (!Host) { yield return new WaitForSeconds(1); Record("11-client-graceful-disconnect"); Application.Quit(0); }
        }

        private IEnumerator RunHealth(string mode)
        {
            Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume); began = Time.realtimeSinceStartupAsDouble;
            if (mode == "health-resume")
            {
                Check(session.Vitals(0).Current == 77 && session.Vitals(1).State == PlayerLifeState.Downed && session.RunXp == 100,
                    "checkpoint restored health, XP and downed partner");
                Record("HEALTH-RESTORED-PASS");
                if (Host) { var target = session.Players[1].State.Position; yield return Walk(target.X - 1, target.Y); }
            }
            else
            {
                yield return Walk(Host ? -.5f : .5f, 3);
                yield return At(2);
                if (Host) { session.Life.TryAwardRunXp(100); session.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100); }
                yield return Until(() => session.Vitals(1).State == PlayerLifeState.Downed, "downed partner replication");
                Record("HEALTH-DOWNED-PASS");
                yield return At(3);
                if (Host) { session.LocalCommands.Interact(new PlayerIdentity(1), false); session.LocalCommands.Interact(new PlayerIdentity(1), true); }
                yield return At(4.2);
                if (Host) session.LocalCommands.Interact(new PlayerIdentity(1), false);
                yield return At(4.5);
                Check(session.Vitals(1).State == PlayerLifeState.Downed, "partial revive cannot finish after release");
                Record("HEALTH-REVIVE-CANCEL-PASS");
            }
            if (Host) { session.LocalCommands.Interact(new PlayerIdentity(1), false); session.LocalCommands.Interact(new PlayerIdentity(1), true); }
            yield return Until(() => session.Vitals(1).Current == 50, "three-second hold revives at half health");
            Record("HEALTH-REVIVE-PASS");
            yield return new WaitForSeconds(.5f);
            if (mode == "health-checkpoint")
            {
                if (Host)
                { session.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 23); session.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 100); }
                yield return Until(() => session.Vitals(0).Current == 77 && session.Vitals(1).State == PlayerLifeState.Downed, "downed checkpoint committed");
                Record("HEALTH-CHECKPOINT-PASS"); while (true) yield return null;
            }
            yield return new WaitForSeconds(.5f);
            if (Host)
            { session.DamagePlayer(new PlayerIdentity(1), Guid.NewGuid(), 1000); session.DamagePlayer(new PlayerIdentity(2), Guid.NewGuid(), 1000); }
            yield return Until(() => flow.State == ApplicationState.Death, "party loss reaches death screen");
            var folder = SessionDiagnostics.Option("--save-directory");
            var profile = new ProfileProgressStore(folder);
            Check(profile.Profile.Experience == 6, "each profile receives full six percent of shared run XP");
            Check(new RunCheckpointStore(System.IO.Path.Combine(folder, "run.save")).Load(out _, out _) == CheckpointLoadStatus.Missing, "ended run cannot continue");
            if (Host) yield return Until(() => profile.PendingFor(session.Life.Capture().Profiles[1]).Count == 0, "peer reward acknowledged");
            Record("HEALTH-PASS");
            if (!Host) { yield return new WaitForSeconds(1); Application.Quit(0); }
        }

        private IEnumerator RunEnemies()
        {
            Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume); began = Time.realtimeSinceStartupAsDouble;
            var enemy = session.Rooms.CurrentRoom.Encounter.Enemies[0]; var identity = enemy.Identity;
            Check(Host ? enemy.Simulation != null : enemy.Simulation == null && enemy.Target.Health == null, "enemy authority boundary");
            yield return Walk(Host ? -1 : 1, 3.5f);
            yield return At(2);
            Aim(new CoreVector(0, 5) - Position); Attack(); Attack();
            var expected = session.WeaponName == "weapon.knife" ? 50 : 60;
            yield return Until(() => enemy.Target.DisplayedHealth == expected, "both player attacks on one enemy without duplicate damage");
            Record("ENEMY-SHARED-DAMAGE-PASS");
            yield return At(4);
            if (Host)
            {
                while (enemy.Lifecycle == EnemyLifecycle.Alive)
                { Attack(); yield return new WaitForSeconds(.7f); }
            }
            yield return Until(() => enemy.Lifecycle == EnemyLifecycle.Dead, "replicated enemy death");
            Check(enemy.DeathEvents == 1 && enemy.Target.HitColliders.All(c => !c.enabled), "single death event and disabled hit shapes");
            Record("ENEMY-DEATH-PASS");
            yield return At(8);
            // Lifecycle operation on the host-owned fixture, not an input or client authority bypass.
            if (Host) Check(enemy.Simulation.Despawn(), "host fixture despawn");
            yield return Until(() => enemy.Lifecycle == EnemyLifecycle.Despawned, "replicated despawn");
            Check(!enemy.gameObject.activeSelf, "despawn removes presentation and collision");
            Record("ENEMY-DESPAWN-PASS");
            var before = session.CurrentRoom; var level = session.LevelNumber;
            var choices = session.Run.Connections.Where(c => c.From.Equals(before)).ToArray();
            yield return Walk(choices.Length == 1 ? 0 : -6, 13);
            yield return Until(() => !session.CurrentRoom.Equals(before) && session.RoomReady, "room transition after enemy despawn");
            var next = session.Rooms.CurrentRoom.Encounter.Enemies[0];
            Check(next.Identity.Encounter != identity.Encounter && next.Target.DisplayedHealth == next.Target.InitialHealth,
                "new room incarnation and fresh health");
            Check(session.LevelNumber == level && session.Rooms.RoomContainer.childCount == 1, "one room after encounter cleanup");
            Record("ENEMY-ROOM-CLEANUP-PASS");
            Record("ENEMY-PASS");
            if (!Host) { yield return new WaitForSeconds(1); Application.Quit(0); }
        }
    }
}
#endif
