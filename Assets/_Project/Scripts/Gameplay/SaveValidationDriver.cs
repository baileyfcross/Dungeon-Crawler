#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
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
    }
}
#endif
