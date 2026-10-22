#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;
using CoreVector = System.Numerics.Vector2;
namespace SpaceCrawler.Gameplay
{
    /// <summary>Opt-in standalone journey fixture. Only fixture damage and XP are injected; travel and completion use ordinary commands.</summary>
    public sealed class CompletionValidationDriver : MonoBehaviour
    {
        private ApplicationRoot root;
        private bool cooperative, host;
        private GameplaySession Solo => root.CurrentGameplay;
        private MultiplayerSession Net => root.CurrentMultiplayer;
        private DungeonGraph Graph => cooperative ? Net.Run : Solo.Run;
        private RunRoomId Room => cooperative ? Net.CurrentRoom : Solo.Rooms.Progression.CurrentRoomId;
        private int Level => cooperative ? Net.LevelNumber : Solo.LevelNumber;
        private AuthoredRoom Physical => cooperative ? Net.Rooms.CurrentRoom : Solo.Rooms.CurrentRoom;
        private CoreVector Position => cooperative ? Net.Players[Net.LocalPlayer-1].State.Position : Solo.Simulation.State.Position;
        private PlayerIdentity Player => cooperative ? new PlayerIdentity(Net.LocalPlayer) : new PlayerIdentity(1);
        public void Bind(ApplicationRoot application) { root=application; StartCoroutine(Run()); }
        private void Check(bool value,string message) { if(!value)throw new InvalidOperationException("[CompletionValidation] FAILED "+message); }
        private IEnumerator Until(Func<bool> condition,string operation)
        { var deadline=Time.realtimeSinceStartupAsDouble+20; while(!condition()){Check(Time.realtimeSinceStartupAsDouble<deadline,operation);yield return null;} yield return null; }
        private void Submit(string name)
        { var button=root.CurrentScreen.View.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,name+" available");using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);} }
        private void Move(CoreVector direction)
        { if(cooperative){if(Net.LocalCommands.InputActive)Net.LocalCommands.Move(new MoveIntent(Player,direction));}else Solo.Simulation.RequestMove(new MoveIntent(Player,direction)); }
        private void Interact(bool held)
        { if(cooperative)Net.LocalCommands.Interact(Player,held);else Solo.Interact(Player,held); }
        private IEnumerator Walk(Vector2 destination,bool crossing)
        {
            var before=Room;int level=Level;var deadline=Time.realtimeSinceStartupAsDouble+18;
            while(Level==level && Room.Equals(before))
            {
                if(cooperative && !Net.RoomReady){yield return null;continue;}
                Check(Time.realtimeSinceStartupAsDouble<deadline,$"physical movement to exit level={Level} room={Room} position={Position} destination={destination} crossing={crossing}");
                var delta=new CoreVector(destination.x,destination.y)-Position;
                if(!crossing && delta.Length()<.15f)break;
                // Slow the fixture near its target so delayed authoritative snapshots do not cause full-speed oscillation.
                Move(Math.Abs(delta.X)>.1f?new CoreVector(Mathf.Clamp(delta.X,-1,1),0):new CoreVector(0,Mathf.Clamp(delta.Y,-1,1)));
                yield return null;
            }
            Move(CoreVector.Zero);
            if(cooperative)yield return Until(()=>Net.RoomReady,"room agreement");
            yield return new WaitForSeconds(.15f);
        }
        private IEnumerator Run()
        {
            cooperative=SessionDiagnostics.Option("--session-role")!=null;host=!cooperative || SessionDiagnostics.Option("--session-role")=="host";
            if(!cooperative)
            {
                yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"main menu");
                if(SessionDiagnostics.Option("--completion-validation")=="reload")
                {
                    Check(root.Profile.Experience==15 && root.Flow.ContinueAvailable,"profile credit survives restart and fresh unfinished run remains continuable");
                    Submit("continue");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"fresh Continue");
                    Check(Solo.Life.RunXp==0 && Solo.Life[0].Current==100,"new run restored without old run XP");
                    Debug.Log("[CompletionValidation] RELOAD-PASS");Application.Quit(0);yield break;
                }
                Submit("new-game");
            }
            yield return Until(()=>root.Flow.State==ApplicationState.Gameplay && (cooperative?Net!=null && Net.RoomReady:Solo!=null),"gameplay");
            if(cooperative){Net.PollLocalDevices=false;Net.Players[Net.LocalPlayer-1].Input.HandleFocus(true);}else{Solo.PollLocalDevices=false;Solo.Input.HandleFocus(true);}
            root.Flow.TryRequest(ApplicationCommand.Resume);yield return null;
            Interact(false);Interact(true);Check(cooperative?!Net.RunEnded:!Solo.Life.Ended,"early completion rejected");Interact(false);
            int transitions=0;
            while(Level<3 || !Graph.GetRoom(Room).IsTerminal)
            {
                Check(++transitions<=80,"bounded three-level traversal");
                if(host)foreach(var actor in Physical.Encounter.Enemies.Where(a=>a.Lifecycle==EnemyLifecycle.Alive))
                    actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),100000+transitions,actor.Target.Identity,actor.Target.InitialHealth),out _);
                var edge=Graph.Route(Room,Graph.TerminalRoomId)[0];var choices=Graph.Connections.Where(e=>e.From.Equals(Room)).ToArray();
                yield return Walk(Physical.ExitFor(edge,choices).transform.position,true);
            }
            Check(Physical.RunExitAnchor.gameObject.activeSelf,"final run exit visible");
            Check(Graph.Connections.Any(e=>e.From.Equals(Room)),"backtracking remains available before completion");
            if(host){var life=cooperative?Net.Life:Solo.Life;Check(life.TryAwardRunXp(250),"explicit reward fixture");}
            var anchor=(Vector2)Physical.RunExitAnchor.position;
            yield return Walk(anchor+(cooperative?(host?Vector2.left:Vector2.right)*.6f:Vector2.zero),false);
            if(cooperative)
            {
                yield return Until(()=>Net.Players.All(p=>CoreVector.Distance(p.State.Position,new CoreVector(anchor.x,anchor.y))<1),"both players at exit");
                if(host)Net.DamagePlayer(new PlayerIdentity(2),Guid.NewGuid(),100);
                yield return Until(()=>Net.Vitals(1).State==PlayerLifeState.Downed,"downed partner");
                Interact(false);if(host)Interact(true);
                yield return new WaitForSeconds(.3f);Check(!Net.RunEnded,"downed party cannot complete");
                yield return Until(()=>Net.Vitals(1).State==PlayerLifeState.Alive,"three-second revival");
                Interact(false);yield return new WaitForSeconds(.2f);
                if(host)Interact(true);
                else {yield return Until(()=>Net.CompletionHint.Contains("1 / 2"),"host confirmation");Check(!Net.RunEnded,"one confirmation cannot complete");Interact(true);}
            }
            else {Interact(false);Interact(true);}
            yield return Until(()=>root.Flow.State==ApplicationState.Completed,"completion panel");
            var receipt=cooperative?Net.Outcome:Solo.Outcome;
            Check(receipt.Reason==RunEndReason.Completed && receipt.RunXp==250,"one successful outcome");
            Check(root.Profile.Experience==15 && root.LastRunReward.ProfileXp==15,"actual permanent profile credit");
            Check(root.CurrentScreen.View.Q<Label>("completion-rewards").text.Contains("15"),"reward presentation");
            Check(!root.Flow.ContinueAvailable,"completed run cannot Continue");
            if(cooperative)yield return Until(()=>Net.CanLeaveCompletion,"durable peer reward acknowledgment");
            var completedId=receipt.RunId;
            if(cooperative && host)yield return new WaitForSeconds(.5f);
            Submit("completion-return-to-menu");yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"return to menu");
            Check(!root.Flow.ContinueAvailable,"completed save stays invalid");
            cooperative=false;Submit("new-game");yield return Until(()=>root.Flow.State==ApplicationState.Gameplay,"fresh run");
            Check(Solo.Life.RunId!=completedId && Solo.Life.RunXp==0 && Solo.Life[0].Current==100 && root.Profile.Experience==15,"fresh run preserves only permanent XP");
            Submit("return-to-menu");yield return Until(()=>root.Flow.State==ApplicationState.MainMenu,"suspend fresh run");
            Check(root.Flow.ContinueAvailable,"fresh live run can Continue");
            Debug.Log("[CompletionValidation] COMPLETION-PASS");Application.Quit(0);
        }
    }
}
#endif
