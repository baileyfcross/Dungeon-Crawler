#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in standalone validation through menu commands and ordinary movement intentions.</summary>
    public sealed class SaveValidationDriver : MonoBehaviour
    {
        private ApplicationRoot root;
        public void Bind(ApplicationRoot application) { root = application; StartCoroutine(Run()); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[SaveValidation] FAILED " + message); }
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (!condition()) { Check(Time.realtimeSinceStartupAsDouble < deadline, "operation timed out"); yield return null; }
            yield return null;
        }
        private IEnumerator Run()
        {
            yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            var mode = SessionDiagnostics.Option("--save-validation");
            if (mode == "melee-prepare" || mode == "melee-resume" || mode == "melee-loss") { yield return RunMelee(mode); yield break; }
            if (mode == "health-profile")
            {
                Check(root.Profile.Experience == 15 && root.Flow.ContinueAvailable, "permanent XP survives process restart with a fresh unfinished run");
                Debug.Log("[SaveValidation] HEALTH-PROFILE-PASS"); Application.Quit(0); yield break;
            }
            if (mode == "health-prepare" || mode == "health-resume") { yield return RunHealth(mode); yield break; }
            var expected = root.SavedCheckpoint;
            var button = root.CurrentScreen.View.Q<Button>(mode == "prepare" ? "new-game" : "continue");
            if (mode == "invalid")
            {
                Check(expected == null && !button.enabledInHierarchy, "invalid save must disable Continue");
                Check(!string.IsNullOrWhiteSpace(root.CurrentScreen.View.Q<Label>("status").text), "invalid save feedback");
                Debug.Log("[SaveValidation] PASS-invalid-save-preserved"); Application.Quit(0); yield break;
            }
            Check(button != null && button.enabledInHierarchy, "requested menu command is available");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            yield return Until(() => root.Flow.State == ApplicationState.Gameplay && root.CurrentGameplay != null);
            var session = root.CurrentGameplay;
            session.PollLocalDevices = false; session.Input.HandleFocus(true); root.Flow.TryRequest(ApplicationCommand.Resume);
            if (mode == "prepare")
            {
                while (session.LevelNumber < 2 || session.Rooms.Progression.CurrentRoomId.Value == 0)
                {
                    var before = session.Rooms.Progression.CurrentRoomId; var level = session.LevelNumber;
                    var exit = session.Rooms.Progression.Choices.Count == 1 ? session.Rooms.CurrentRoom.SingleExit : session.Rooms.CurrentRoom.LeftExit;
                    var target = (Vector2)exit.transform.position;
                    var deadline = Time.realtimeSinceStartupAsDouble + 15;
                    while (session.LevelNumber == level && session.Rooms.Progression.CurrentRoomId.Equals(before))
                    {
                        Check(Time.realtimeSinceStartupAsDouble < deadline, "physical room traversal");
                        var delta = target - session.Motor.Body.position;
                        var direction = Mathf.Abs(delta.x) > .15f ? new Vector2(Mathf.Sign(delta.x), 0) : Vector2.up;
                        session.Simulation.RequestMove(new MoveIntent(session.Simulation.State.Player, new System.Numerics.Vector2(direction.x, direction.y)));
                        yield return null;
                    }
                    session.Simulation.RequestMove(new MoveIntent(session.Simulation.State.Player, System.Numerics.Vector2.Zero));
                    yield return null;
                }
                Check(root.SavedCheckpoint.LevelNumber == 2 && root.SavedCheckpoint.CurrentRoom.Value != 0, "level-aware checkpoint committed");
                Debug.Log($"[SaveValidation] PASS-prepared seed={root.SavedCheckpoint.RunSeed} level=2 room={root.SavedCheckpoint.CurrentRoom}");
            }
            else
            {
                Check(expected != null && session.Sequence.Seed == expected.RunSeed && session.LevelNumber == expected.LevelNumber,
                    "run and level restored");
                Check(session.Rooms.Progression.CurrentRoomId.Equals(expected.CurrentRoom)
                    && session.Rooms.Progression.RoomsEntered == expected.RoomVisits.Count, "room and history restored");
                Check(Vector2.Distance(session.Motor.Body.position, session.Rooms.CurrentRoom.PlayerSpawnAnchor.position) < .001f, "entry spawn restored");
                Check(session.Combat.Projectiles.Count == 0, "no transient projectile restored");
                foreach (var target in session.Combat.Targets) Check(target.Health.Current == target.InitialHealth, "room combat reset");
                Check(session.Combat.Simulation.EquippedWeapon.Id == expected.EquipmentIds[0], "equipment restored");
                Debug.Log($"[SaveValidation] PASS-resumed seed={expected.RunSeed} level={expected.LevelNumber} room={expected.CurrentRoom}");
            }
            Application.Quit(0);
        }
        private void Submit(string name)
        {
            var button = root.CurrentScreen.View.Q<Button>(name); Check(button != null && button.enabledInHierarchy, name + " available");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }
        private IEnumerator RunMelee(string mode)
        {
            var saved = root.SavedCheckpoint;
            Submit(mode == "melee-resume" ? "continue" : "new-game");
            yield return Until(() => root.Flow.State == ApplicationState.Gameplay && root.CurrentGameplay != null);
            var session = root.CurrentGameplay; session.PollLocalDevices = false; session.Input.HandleFocus(true); root.Flow.TryRequest(ApplicationCommand.Resume);
            if (mode == "melee-resume")
            {
                Check(saved != null && session.Rooms.Progression.CurrentRoomId.Equals(saved.CurrentRoom), "saved encounter room restored");
                Check(session.Life[0].Current == saved.Life.Players[0].Current && session.Life[0].Current < 100, "enemy damage persists through Continue");
                Check(session.Rooms.CurrentRoom.Encounter.Enemies.Count == 1 && session.Rooms.CurrentRoom.Encounter.Enemies[0].Target.Health.Current == 100, "enemy reset after Continue");
                Check(session.Life.RunXp == 0 && root.Profile.Experience == 0, "no unintended XP reward");
                Debug.Log("[SaveValidation] MELEE-RESUME-PASS"); Application.Quit(0); yield break;
            }
            var route = SessionValidationDriver.RouteToEngine(session.Run); Check(route != null, "reachable authored Engine encounter");
            foreach (var edge in route)
            {
                var choices = session.Rooms.Progression.Choices; var index = 0;
                while (index < choices.Count && !choices[index].To.Equals(edge.To)) index++;
                Check(index < choices.Count, "route follows legal connection");
                var target = choices.Count == 1 ? session.Rooms.CurrentRoom.SingleExit.transform.position
                    : index == 0 ? session.Rooms.CurrentRoom.LeftExit.transform.position : session.Rooms.CurrentRoom.RightExit.transform.position;
                var deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (session.Rooms.Progression.CurrentRoomId.Equals(edge.From))
                {
                    Check(Time.realtimeSinceStartupAsDouble < deadline, "physical encounter traversal");
                    var delta = target - session.Motor.transform.position;
                    var direction = Mathf.Abs(delta.x) > .15f ? new System.Numerics.Vector2(Mathf.Sign(delta.x), 0) : System.Numerics.Vector2.UnitY;
                    session.Simulation.RequestMove(new MoveIntent(new PlayerIdentity(1), direction)); yield return null;
                }
                session.Simulation.RequestMove(new MoveIntent(new PlayerIdentity(1), System.Numerics.Vector2.Zero)); yield return null;
            }
            var enemy = session.Rooms.CurrentRoom.Encounter.Enemies[0];
            yield return Until(() => session.Life[0].Current < 100);
            if (mode == "melee-loss")
            {
                yield return Until(() => root.Flow.State == ApplicationState.Death);
                Submit("death-return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
                Check(!root.Flow.ContinueAvailable && root.Profile.Experience == 0, "real enemy solo loss invalidates Continue without XP");
                Debug.Log("[SaveValidation] MELEE-LOSS-PASS"); Application.Quit(0); yield break;
            }
            var combatDeadline = Time.realtimeSinceStartupAsDouble + 10;
            while (enemy.Lifecycle == EnemyLifecycle.Alive)
            {
                Check(Time.realtimeSinceStartupAsDouble < combatDeadline, "player kills pursuing enemy");
                var direction = System.Numerics.Vector2.Normalize(new System.Numerics.Vector2(enemy.transform.position.x, enemy.transform.position.y) - session.Simulation.State.Position);
                session.Simulation.RequestAim(new AimIntent(new PlayerIdentity(1), direction)); session.Combat.RequestAttack(new AttackIntent(new PlayerIdentity(1), direction));
                yield return new WaitForSeconds(.35f);
            }
            Check(enemy.DeathEvents == 1 && session.Life[0].Current > 0 && session.Life.RunXp == 0, "solo fight leaves living player and no kill XP");
            Check(session.Rooms.Progression.Choices.Count > 0, "exits retain existing availability");
            Submit("return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
            Check(root.Flow.ContinueAvailable && root.Profile.Experience == 0, "live encounter run can be suspended");
            Debug.Log("[SaveValidation] MELEE-PREPARE-PASS"); Application.Quit(0);
        }

        private IEnumerator RunHealth(string mode)
        {
            Submit(mode == "health-prepare" ? "new-game" : "continue");
            yield return Until(() => root.Flow.State == ApplicationState.Gameplay && root.CurrentGameplay != null);
            var session = root.CurrentGameplay; session.PollLocalDevices = false;
            if (mode == "health-prepare")
            {
                session.Life.TrySetMaximum(new PlayerIdentity(1), 150); session.DamagePlayer(Guid.NewGuid(), 25); session.Life.TryAwardRunXp(250);
                Submit("return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
                Check(root.Profile.Experience == 0, "suspending a run grants no profile XP");
                Debug.Log("[SaveValidation] HEALTH-PREPARED-PASS");
            }
            else
            {
                Check(session.Life[0].Current == 75 && session.Life[0].Maximum == 150 && session.Life.RunXp == 250, "health and XP restored");
                session.DamagePlayer(Guid.NewGuid(), 1000); yield return Until(() => root.Flow.State == ApplicationState.Death);
                Check(root.Profile.Experience == 15, "solo run-end reward");
                Submit("death-return-to-menu"); yield return Until(() => root.Flow.State == ApplicationState.MainMenu);
                Check(!root.Flow.ContinueAvailable && !root.RequestContinue(), "solo death invalidates Continue");
                Submit("new-game"); yield return Until(() => root.Flow.State == ApplicationState.Gameplay);
                Check(root.CurrentGameplay.Life[0].Current == 100 && root.CurrentGameplay.Life.RunXp == 0 && root.Profile.Experience == 15, "fresh run retains permanent XP only");
                Debug.Log("[SaveValidation] HEALTH-RESUMED-DEATH-PASS");
            }
            Application.Quit(0);
        }
    }
}
#endif
