#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace SpaceCrawler.Gameplay
{
    public sealed partial class SessionValidationDriver
    {
        private IEnumerator RunKillRewards()
        {
            Local.Input.HandleFocus(true);flow.TryRequest(ApplicationCommand.Resume);
            bool restoring=SessionDiagnostics.Option("--session-validation")=="rewards-resume";
            var root=GameAudio.Current.GetComponent<ApplicationRoot>();
            Check(session.RewardPolicy!=null && session.Difficulty==RunDifficulty.Normal,"host reward policy replicated");
            if(!restoring && Host)
            {
                var edge=session.Progression.Choices.First(e=>!session.Run.GetRoom(e.To).IsTerminal);
                Local.Motor.PlaceAt(session.Rooms.CurrentRoom.ExitFor(edge,session.Progression.Choices).transform.position);
            }
            yield return Until(()=>session.RoomReady && !session.PlayerRoom(0).Equals(session.PlayerRoom(1)),"independent rooms for shared reward proof");
            var room=session.PlayerRoomLoader(0).CurrentRoom;var actor=room.Encounter.Enemies.First(e=>e.Archetype==EnemyArchetype.Melee);int spawn=actor.Identity.Spawn;
            Check(root.Profile.Experience==0 && root.Profile.FractionHundredths==0,"no profile payout during an active run");
            if(!restoring && Host)
            {
                session.DamagePlayer(new PlayerIdentity(2),Guid.NewGuid(),100);
                actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),88001,actor.Target.Identity,actor.Target.InitialHealth),out _);session.SaveCurrent();
                var saved=root.SavedCheckpoint;
                Check(saved.Rewards!=null && saved.Life.RunXp==10 && saved.World.Rooms.Single(r=>r.Level==1 && r.Room.Equals(session.PlayerRoom(0))).Enemies.Single(e=>e.Spawn==spawn).Health==0,"one checkpoint contains death, XP, and frozen policy");
            }
            yield return Until(()=>session.RunXp==10 && actor.Lifecycle==EnemyLifecycle.Dead && session.Vitals(1).State==PlayerLifeState.Downed,"XP shared with downed teammate in another room");
            yield return Until(()=>root.CurrentScreen.View.Q<Label>("run-xp").text.Contains("10"),"existing HUD displays earned XP");
            if(!restoring)
            {Record("KILL-REWARDS-PASS");if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}yield break;}
            Check(!actor.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),88002,actor.Target.Identity,999),out _),"restored dead enemy cannot produce another reward");
            Check(session.RunXp==10,"Continue did not replay kill credit");
            long expected=room.Encounter.Enemies.Sum(e=>session.RewardPolicy.KillXp(1,e.Archetype,session.Difficulty));
            if(Host)foreach(var other in room.Encounter.Enemies.Where(e=>e.Lifecycle==EnemyLifecycle.Alive))
                other.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),88003,other.Target.Identity,other.Target.InitialHealth),out _);
            yield return Until(()=>session.RunXp==expected,"remaining enemy rewards replicated exactly once");
            Check(root.Profile.Experience==0,"profile rewards deferred until outcome");
            if(Host)session.DamagePlayer(new PlayerIdentity(1),Guid.NewGuid(),1000);
            yield return Until(()=>flow.State==ApplicationState.Death,"party loss reaches existing death screen");
            string folder=SessionDiagnostics.Option("--save-directory");var store=new ProfileProgressStore(folder);
            Check(store.Profile.Experience==expected*6/100 && store.Profile.FractionHundredths==expected*6%100,"each participant receives six percent including fractional carry");
            store.EndRun(session.Outcome);Check(store.Profile.Experience==expected*6/100 && store.Profile.FractionHundredths==expected*6%100,"duplicate receipt cannot credit again");
            Check(new RunCheckpointStore(Path.Combine(folder,"run.save")).Load(out _,out _)==CheckpointLoadStatus.Missing,"lost run cannot Continue");
            if(Host)yield return Until(()=>store.PendingFor(session.Life.Capture().Profiles[1]).Count==0,"peer reward acknowledgement persisted");
            Record("KILL-REWARDS-END-PASS");if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}
        }
    }
}
#endif
