#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;
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
                anchor.position = room.transform.TransformPoint(count == 1 ? new Vector3(0, 5, 0) : new Vector3((i % 8 - 3.5f) * 1.5f, 5 + i / 8 * 1.5f, 0));
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
                if (session.Run.GenerationVersion == 1) Check(CoreVector.Distance(session.Players[i].State.Position, new CoreVector(anchor.position.x, anchor.position.y)) < .22f, "distinct mapped room spawn");
                else Check(room.IsSafePosition(new Vector2(session.Players[i].State.Position.X,session.Players[i].State.Position.Y)),"safe reciprocal arrival");
            }
        }
        private void Update()
        {
            if(SessionDiagnostics.Option("--session-validation")=="audio" || SessionDiagnostics.Option("--session-validation").StartsWith("exploration"))return;
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
            if(SessionDiagnostics.Option("--session-validation").StartsWith("exploration")){yield return RunExploration();yield break;}
            if(SessionDiagnostics.Option("--session-validation")=="audio"){yield return RunAudio();yield break;}
            if (SessionDiagnostics.Option("--session-validation") == "traversal" || SessionDiagnostics.Option("--session-validation") == "traversal-resume")
            { yield return RunTraversal(); yield break; }
            if (SessionDiagnostics.Option("--session-validation") == "melee")
            { yield return RunMelee(); yield break; }
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
                Check(session.Run.Nodes.Count >= 10 && session.Run.Nodes.Count <= 25, "total room bounds");
                var choices = session.Run.Connections.Where(c => c.From.Equals(before)).ToArray();
                var routeEdge = session.Run.Route(before,session.Run.TerminalRoomId)[0];
                var index = Array.FindIndex(choices,e=>e.To.Equals(routeEdge.To)); var destination = choices[index].To;
                var advances = level < 3 && session.Run.GetRoom(destination).IsTerminal;
                if (!testedSplit && choices.Length > 1)
                {
                    var firstDoor = session.Rooms.CurrentRoom.ExitFor(choices[0],choices).transform.position;
                    var secondDoor = session.Rooms.CurrentRoom.ExitFor(choices[1],choices).transform.position;
                    yield return Walk(Host ? firstDoor.x : secondDoor.x,Host ? firstDoor.y : secondDoor.y);
                    yield return Until(() => CoreVector.Distance(session.Players[0].State.Position,new CoreVector(firstDoor.x,firstDoor.y)) < .3f
                        && CoreVector.Distance(session.Players[1].State.Position,new CoreVector(secondDoor.x,secondDoor.y)) < .3f,"different physical exits reached");
                    yield return new WaitForSeconds(.3f);
                    Check(session.LevelNumber == level && session.CurrentRoom.Equals(before),"different exits cannot transition");
                    Record("06-different-exits-no-transition");
                    index = 0; destination = choices[0].To; testedSplit = true;
                    if (!Host) { yield return Walk(0,6); yield return Walk(firstDoor.x,firstDoor.y); }
                }
                else { var door = session.Rooms.CurrentRoom.ExitFor(choices[index],choices).transform.position;
                yield return Walk(door.x,door.y); }
                advances = level < 3 && session.Run.GetRoom(destination).IsTerminal;
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
            if (session.Run.GenerationVersion == 1) Check(!new[] { session.Rooms.CurrentRoom.SingleExit, session.Rooms.CurrentRoom.LeftExit, session.Rooms.CurrentRoom.RightExit }.Any(e => e.gameObject.activeSelf), "Legacy terminal has no exits");
            else Check(session.Rooms.CurrentRoom.CardinalExits.Any(e=>e.gameObject.activeSelf),"terminal retains bidirectional travel");
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

        private IEnumerator Door(RoomConnection edge)
        {
            var epoch = session.Epoch;
            var choices = session.Run.Connections.Where(e=>e.From.Equals(edge.From)).ToArray();
            var position = session.Rooms.CurrentRoom.ExitFor(edge,choices).transform.position;
            yield return Walk(position.x,position.y);
            yield return Until(() => session.Epoch > epoch && session.RoomReady && session.CurrentRoom.Equals(edge.To),"bidirectional shared travel");
            yield return new WaitForSeconds(.2f);
        }
        private IEnumerator RunTraversal()
        {
            Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume);
            Check(session.Run.GenerationVersion == 2,"TinyKeep generation agreed");
            if (SessionDiagnostics.Option("--session-validation") == "traversal-resume")
            {
                yield return Until(() => session.Rooms.CurrentRoom.Encounter.Enemies.Count == 1
                    && session.Rooms.CurrentRoom.Encounter.Enemies[0].Target.DisplayedHealth == 0,"dead enemy restored on both peers");
                Check(session.Vitals(0).Current == 1 && session.Vitals(1).State == PlayerLifeState.Downed,"per-run health restored");
                var offset=(Vector2)session.Rooms.CurrentRoom.transform.position;
                Check(CoreVector.Distance(session.Players[0].State.Position,new CoreVector(offset.x-2,offset.y+3)) < .02f
                    && CoreVector.Distance(session.Players[1].State.Position,new CoreVector(offset.x+2,offset.y+3)) < .02f,"both exact positions restored");
                Check(session.Players[0].State.AimDirection.Y > .99f && session.Players[1].State.AimDirection.Y < -.99f,"both aim directions restored");
                Check(session.Players[1].ReviveProgress == 0,"revive hold cleared on Continue");
                Record("TRAVERSAL-RESUME-PASS");
                if (!Host) { yield return new WaitForSeconds(1); Application.Quit(0); }
                yield break;
            }
            // Ordinary movement/aim/attack requests on each independent process.
            yield return Walk(Host ? -3 : 3,2);
            yield return Until(() => session.Players[0].State.Position.X < -2.7f && session.Players[1].State.Position.X > 2.7f,"independent movement");
            Aim(Host ? CoreVector.UnitY : -CoreVector.UnitY); Attack();
            yield return Until(() => session.Players.All(p => (Host ? p.Combat.Simulation.AcceptedAttackCount : p.LastSnapshot.Attacks) > 0),"both attacks replicated");
            var start = session.CurrentRoom; var choices = session.Run.Connections.Where(e=>e.From.Equals(start)).ToArray();
            var first = choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);
            yield return Door(first);
            var reverse = session.Run.Connections.Single(e=>e.From.Equals(first.To) && e.To.Equals(start));
            yield return Door(reverse);
            Check(session.CurrentRoom.Equals(start),"backtracking returns to same starting room");
            var route = RouteToEngine(session.Run); Check(route != null,"authored encounter is reachable");
            foreach (var edge in route) yield return Door(edge);
            var encounter = session.CurrentRoom; var enemy = session.Rooms.CurrentRoom.Encounter.Enemies[0];
            // Explicit fixture damage isolates persistence from combat pacing (combat has separate proofs).
            if (Host) enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),10000,enemy.Target.Identity,100),out _);
            yield return Until(() => enemy.Lifecycle == EnemyLifecycle.Dead,"authoritative enemy death");
            var leave = session.Run.Connections.First(e=>e.From.Equals(encounter) && !session.Run.GetRoom(e.To).IsTerminal);
            yield return Door(leave);
            yield return Door(session.Run.Connections.Single(e=>e.From.Equals(leave.To) && e.To.Equals(encounter)));
            Check(session.Rooms.CurrentRoom.Encounter.Enemies[0].Target.DisplayedHealth == 0,"dead enemy stays dead after revisit");
            yield return Until(()=>session.PlayerRoom(0).Equals(encounter) && session.PlayerRoom(1).Equals(encounter),"both independent routes return to the encounter");
            var meeting=(Vector2)session.Rooms.CurrentRoom.transform.position;
            yield return Walk(meeting.x+(Host?-2:2),meeting.y+3);
            yield return Until(()=>CoreVector.Distance(session.Players[0].State.Position,new CoreVector(meeting.x-2,meeting.y+3))<.2f
                && CoreVector.Distance(session.Players[1].State.Position,new CoreVector(meeting.x+2,meeting.y+3))<.2f
                && session.Players.All(p=>p.State.Velocity.LengthSquared()<.01f),"both players finish movement before the downed save fixture");
            if (Host)
            {
                var offset=(Vector2)session.Rooms.CurrentRoom.transform.position;
                session.Players[0].Motor.PlaceAt(offset+new Vector2(-2,3)); session.Players[1].Motor.PlaceAt(offset+new Vector2(2,3));
                session.Players[0].Simulation.RestoreAim(CoreVector.UnitY); session.Players[1].Simulation.RestoreAim(-CoreVector.UnitY);
                session.DamagePlayer(new PlayerIdentity(1),Guid.NewGuid(),session.Life[0].Current-1); session.DamagePlayer(new PlayerIdentity(2),Guid.NewGuid(),100);
                session.SaveCurrent();
            }
            yield return Until(() => session.Vitals(0).Current == 1 && session.Vitals(1).State == PlayerLifeState.Downed,"persistent health fixture replicated");
            Record("TRAVERSAL-PREPARE-PASS");
            if (!Host) { yield return new WaitForSeconds(1); Application.Quit(0); }
        }

        private IEnumerator RunMelee()
        {
            Local.Input.HandleFocus(true); flow.TryRequest(ApplicationCommand.Resume);
            var route = RouteToEngine(session.Run);
            Check(route != null, "a reachable authored Engine encounter exists");
            foreach (var edge in route)
            {
                var choices = session.Run.Connections.Where(connection => connection.From.Equals(edge.From)).ToArray();
                var index = Array.FindIndex(choices, connection => connection.To.Equals(edge.To));
                var door = session.Rooms.CurrentRoom.ExitFor(choices[index],choices).transform.position;
                yield return Walk(door.x,door.y);
                yield return Until(() => session.CurrentRoom.Equals(edge.To) && session.RoomReady, "physical traversal to authored encounter");
            }
            var enemy = session.Rooms.CurrentRoom.Encounter.Enemies[0]; var start = enemy.transform.position;
            var localOffset=(Vector2)session.Rooms.CurrentRoom.transform.position;
            yield return Walk(localOffset.x+(Host ? -1 : 1),localOffset.y+3);
            Check(enemy.Configuration != null && (Host ? enemy.Behaviour != null : enemy.Behaviour == null && enemy.Simulation == null), "melee authority boundary");
            yield return Until(() => Vector3.Distance(start, enemy.transform.position) > .5f, "moving enemy replicated");
            yield return Until(() => enemy.BehaviourState == EnemyBehaviourState.Windup && enemy.WindupProgress > .05f, "readable windup on each peer");
            Record("MELEE-WINDUP-PASS");
            yield return Until(() => session.Vitals(0).Current + session.Vitals(1).Current < 200, "real enemy damage replicated to player health");
            Record("MELEE-DAMAGE-PASS");
            var deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (enemy.Lifecycle == EnemyLifecycle.Alive)
            {
                Check(Time.realtimeSinceStartupAsDouble < deadline, "kill enemy using equipped weapon");
                Aim(new CoreVector(enemy.transform.position.x, enemy.transform.position.y) - Position); Attack();
                yield return new WaitForSeconds(.35f);
            }
            Check(enemy.Lifecycle == EnemyLifecycle.Dead && enemy.DeathEvents == 1 && session.RunXp == 0, "one replicated death without XP rewards");
            Record("MELEE-DEATH-PASS");
            var before = session.CurrentRoom; var oldModel = enemy.Simulation;
            var exits = session.Run.Connections.Where(edge => edge.From.Equals(before)).ToArray();
            var exitPosition = session.Rooms.CurrentRoom.ExitFor(exits[0],exits).transform.position;
            yield return Walk(exitPosition.x,exitPosition.y);
            yield return Until(() => !session.CurrentRoom.Equals(before) && session.RoomReady, "shared transition and enemy cleanup");
            Check(!Host || oldModel.Lifecycle == (session.IndependentTravel?EnemyLifecycle.Dead:EnemyLifecycle.Despawned), "old encounter cannot produce a second death");
            Check(session.Rooms.RoomContainer.childCount == 1 && session.RunXp == 0, "one room and no unintended XP");
            Record("MELEE-PASS");
            if (!Host) { yield return new WaitForSeconds(1); Application.Quit(0); }
        }

        public static List<RoomConnection> RouteToEngine(DungeonGraph graph)
        {
            var queue = new Queue<List<RoomConnection>>(); queue.Enqueue(new List<RoomConnection>());
            var visited = new HashSet<RunRoomId> { graph.StartRoomId };
            List<RoomConnection> route = null;
            while (queue.Count > 0)
            {
                var path = queue.Dequeue(); var node = path.Count == 0 ? graph.StartRoomId : path[path.Count - 1].To;
                if (graph.GetRoom(node).Category == RoomCategory.Engine && !graph.GetRoom(node).IsTerminal) { route = path; break; }
                foreach (var edge in graph.Connections.Where(edge => edge.From.Equals(node) && !graph.GetRoom(edge.To).IsTerminal && visited.Add(edge.To)))
                { var next = new List<RoomConnection>(path) { edge }; queue.Enqueue(next); }
            }
            return route;
        }

        private IEnumerator RunExploration()
        {
            Local.Input.HandleFocus(true);flow.TryRequest(ApplicationCommand.Resume);began=Time.realtimeSinceStartupAsDouble;
            bool restoring=SessionDiagnostics.Option("--session-validation")=="exploration-resume";
            Check(session.IndependentTravel && session.LevelNumber==1,"shared level with independent room authority");
            if(!restoring)
            {
                Check(session.Map.Rooms.Count==1 && session.Rooms.CurrentRoom.Encounter.Enemies.Count==0,"only safe starting room revealed");
                session.LocalCommands.Move(new MoveIntent(new PlayerIdentity(session.LocalPlayer),CoreVector.UnitX,true));
                yield return Until(()=>Local.State.Velocity.Length()>7.3f,"authoritative sprint velocity");
                Move(CoreVector.Zero);yield return At(1);
                if(Host)
                {
                    var edge=session.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);
                    Local.Motor.PlaceAt(session.Rooms.CurrentRoom.ExitFor(edge,session.Progression.Choices).transform.position);
                }
            }
            yield return Until(()=>session.RoomReady && !session.PlayerRoom(0).Equals(session.PlayerRoom(1)),"independent rooms replicated");
            Check(session.Map.Rooms.Count==2 && session.Map.PlayerRooms.Count==2,"shared discovery without hidden rooms");
            Check(session.Map.PlayerRooms[0].Equals(session.PlayerRoom(0)) && session.Map.PlayerRooms[1].Equals(session.PlayerRoom(1)),"distinct player room markers");
            var room=session.PlayerRoomLoader(0).CurrentRoom;
            Check(room.Encounter.Enemies.Count==3 && session.Difficulty==RunDifficulty.Normal,"normal encounter count replicated");
            if(!restoring && Host)
            {
                var enemy=room.Encounter.Enemies[0];
                enemy.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),8701,enemy.Target.Identity,100),out _);
                session.SaveCurrent();
            }
            yield return Until(()=>room.Encounter.Enemies[0].Lifecycle==EnemyLifecycle.Dead,"dead enemy persists across split-room save or replication");
            if(Host)session.SaveCurrent();
            var evidence=SessionDiagnostics.Option("--exploration-evidence");
            if(!string.IsNullOrEmpty(evidence))
            {
                System.IO.Directory.CreateDirectory(evidence);
                var root=GameAudio.Current.GetComponent<ApplicationRoot>();
                yield return SteamSessionValidationDriver.CapturePanel(root.CurrentScreen,System.IO.Path.Combine(evidence,session.Mode+"-map.png"),1280,720);
            }
            Record(restoring?"EXPLORATION-RESUME-PASS":"EXPLORATION-PASS");
            if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}
        }

        private IEnumerator RunAudio()
        {
            Local.Input.HandleFocus(true);flow.TryRequest(ApplicationCommand.Resume);began=Time.realtimeSinceStartupAsDouble;
            var audio=GameAudio.Current;var root=audio.GetComponent<ApplicationRoot>();
            var cue=session.WeaponName=="weapon.knife"?SoundCue.KnifeSwing:SoundCue.BlasterShot;
            int shots=audio.EmittedCount(cue);
            yield return At(.3);
            if(!Host)
            {
                flow.TryRequest(ApplicationCommand.Pause);
                var button=root.CurrentScreen.View.Q<Button>("options");
                using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
                Check(root.CurrentScreen.GetComponent<AudioOptionsPanel>().IsOpen,"client Options open");
            }
            yield return At(1);
            if(Host){Aim(CoreVector.UnitX);Attack();Attack();}
            yield return Until(()=>audio.EmittedCount(cue)==shots+1,"one accepted host attack sound per process");
            yield return At(2);
            if(Host)session.DamagePlayer(new PlayerIdentity(1),Guid.NewGuid(),10);
            yield return Until(()=>session.Vitals(0).Current==90,"host simulation and health replication continue during client Options");
            yield return At(3);
            if(!Host)
            {
                Check(flow.State==ApplicationState.Paused,"Options are local pause");root.CurrentScreen.GetComponent<AudioOptionsPanel>().ConsumePause();
                Check(flow.State==ApplicationState.Paused,"closing Options cannot resume");flow.TryRequest(ApplicationCommand.Resume);
            }
            yield return At(4);
            if(!Host){Aim(-CoreVector.UnitX);Attack();Attack();}
            yield return Until(()=>audio.EmittedCount(cue)==shots+2,"one accepted client attack sound per process");
            yield return At(6);Check(audio.EmittedCount(cue)==shots+2,"repeated snapshots cannot replay attack sounds");
            Check(audio.VoiceCount==GameAudio.VoiceLimit,"bounded per-process audio pool");Record("AUDIO-PASS");
            if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}
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
            var exitPosition = session.Rooms.CurrentRoom.ExitFor(choices[0],choices).transform.position;
            yield return Walk(exitPosition.x,exitPosition.y);
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
