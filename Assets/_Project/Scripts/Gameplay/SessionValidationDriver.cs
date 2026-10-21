#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in two-OS-process validation driver. Produces ordinary local commands only;
    /// never writes physics, health, graph, readiness, or progression. Absent from release players.</summary>
    public sealed class SessionValidationDriver : MonoBehaviour
    {
        private MultiplayerSession session;
        private ApplicationFlow flow;
        private double began;
        private bool cleanupShot, observedReplicaShot;
        private bool Host => session.Mode == SessionRole.Host;
        private NetworkPlayerRig Local => session.Players[session.LocalPlayer - 1];
        private CoreVector Position => Local.State.Position;

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
            var room = session.CurrentRoom; var deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (session.CurrentRoom.Equals(room))
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
            yield return Walk(Host ? -6 : 6, 13);
            yield return At(9);
            Check(session.CurrentRoom.Value == 0, "different exits cannot transition");
            if (Host) Check(session.Readiness.Count(session.Progression.Choices[0].To) == 1 && session.Readiness.Count(session.Progression.Choices[1].To) == 1, "one player at each physical exit");
            Record("06-different-exits-no-transition");
            if (!Host)
            {
                yield return Walk(6, 11); Record("07-left-right-exit-cleared");
                yield return Walk(-6, 11); yield return Walk(-6, 13);
            }
            yield return Until(() => session.CurrentRoom.Value == 1 && session.RoomReady, "both ready at left enter exactly one destination");
            yield return new WaitForSeconds(.15f); CheckSpawns();
            Check(session.Rooms.RoomContainer.childCount == 1, "one active room container child");
            Check(Host ? session.Players.Sum(p => p.Combat.Projectiles.Count) == 0 : session.ReplicaProjectileCount == 0, "room projectiles cleaned");
            Record("08-six-proofs-passed-shared-left-transition");
            yield return new WaitForSeconds(.4f);
            var entries = 2;
            while (!session.Run.GetRoom(session.CurrentRoom).IsTerminal)
            {
                var before = session.CurrentRoom;
                var choices = session.Run.Connections.Where(c => c.From.Equals(before)).ToArray();
                var index = before.Value % choices.Length; var destination = choices[index].To;
                yield return Walk(choices.Length == 1 ? 0 : index == 0 ? -6 : 6, 13);
                yield return Until(() => session.CurrentRoom.Equals(destination) && session.RoomReady, "shared forward transition");
                yield return new WaitForSeconds(.15f); CheckSpawns(); entries++;
                if (Host) Check(session.Progression.RoomsEntered == entries, "exactly one Core entry commitment");
                Check(session.Rooms.RoomContainer.childCount == 1, "old room cleanup");
                Record("09-route-room-" + session.CurrentRoom.Value);
                yield return new WaitForSeconds(.4f);
            }
            Check(!new[] { session.Rooms.CurrentRoom.SingleExit, session.Rooms.CurrentRoom.LeftExit, session.Rooms.CurrentRoom.RightExit }.Any(e => e.gameObject.activeSelf), "Terminal has no exits");
            Aim(CoreVector.UnitY); Attack(); Move(Host ? -CoreVector.UnitX : CoreVector.UnitX);
            yield return new WaitForSeconds(.25f); Move(CoreVector.Zero);
            yield return new WaitForSeconds(.3f);
            Record("10-PASS-terminal-remains-playable");
            if (!Host) { yield return new WaitForSeconds(1); Record("11-client-graceful-disconnect"); Application.Quit(0); }
        }
    }
}
#endif
