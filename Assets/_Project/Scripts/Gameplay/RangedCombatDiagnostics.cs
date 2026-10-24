#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector=System.Numerics.Vector2;
namespace SpaceCrawler.Gameplay
{
    public sealed partial class SessionValidationDriver
    {
        private void CaptureRangedFrame(string path)
        {
            var destination=new RenderTexture(1280,720,24);var previous=RenderTexture.active;Texture2D pixels=null;
            try
            {
                destination.Create();var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=destination};
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(session.Follower.GameplayCamera,request);
                RenderTexture.active=destination;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;destination.Release();Destroy(destination);if(pixels!=null)Destroy(pixels);}
        }
        private IEnumerator RunRangedCombat()
        {
            Local.Input.HandleFocus(true);flow.TryRequest(ApplicationCommand.Resume);
            bool restoring=SessionDiagnostics.Option("--session-validation")=="ranged-resume";
            Check(session.EncounterVersion==1 && session.Difficulty==RunDifficulty.Normal,"ranged encounter policy replicated");
            if(restoring)
            {
                var room=session.PlayerRoomLoader(0).CurrentRoom;
                yield return Until(()=>room.Encounter.Enemies.Any(e=>e.Archetype==EnemyArchetype.Ranged && e.Lifecycle==EnemyLifecycle.Dead),"ranged death restored on both peers");
                Check(!session.PlayerRoom(0).Equals(session.PlayerRoom(1)),"split-room continue retained");
                Check(room.Encounter.Enemies.Single(e=>e.Archetype==EnemyArchetype.Ranged).Target.InitialHealth==80,"saved archetype health retained");
                Check(session.HostileProjectileCount==0 && session.RunXp==0,"transient shots excluded and no XP source added");
                Record("RANGED-RESUME-PASS");if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}yield break;
            }
            Check(session.Rooms.CurrentRoom.Encounter.Enemies.Count==0,"safe starting room");
            var node=session.Run.Nodes.First(n=>!n.Id.Equals(session.Run.StartRoomId) && !n.IsTerminal
                && EncounterComposition.RangedCount(session.Run.Seed^(ulong)n.Id.Value,session.Difficulty,3,1)>0);
            if(Host)
            {
                foreach(var edge in session.Run.Route(session.Run.StartRoomId,node.Id))
                {
                    var choices=session.Progression.Choices;var door=session.Rooms.CurrentRoom.ExitFor(edge,choices).transform.position;
                    Local.Motor.PlaceAt(door);
                    yield return Until(()=>session.RoomReady && session.CurrentRoom.Equals(edge.To),"host room traversal");
                    yield return new WaitForSeconds(.1f);
                }
            }
            yield return Until(()=>session.RoomReady && session.PlayerRoom(0).Equals(node.Id),"split-room ranged encounter replicated");
            var encounter=session.PlayerRoomLoader(0).CurrentRoom;var enemy=encounter.Encounter.Enemies.Single(e=>e.Archetype==EnemyArchetype.Ranged);
            Check(Host?enemy.Simulation!=null && enemy.Behaviour!=null:enemy.Simulation==null && enemy.Behaviour==null,"host-only ranged simulation");
            Check(encounter.Encounter.Enemies.Count==3 && enemy.Target.InitialHealth==80,"approved normal composition and health");
            if(Host)
            {
                // Controlled fixture removes melee interference; damage/projectile assertions below exercise real combat.
                foreach(var other in encounter.Encounter.Enemies.Where(e=>e!=enemy))other.Target.ReceiveDamage(new DamageRequest(new PlayerIdentity(1),9201,other.Target.Identity,other.Target.InitialHealth),out _);
                Vector2 origin=enemy.transform.position;Vector2? safe=null;
                for(int i=0;i<24 && !safe.HasValue;i++)
                {
                    float angle=i*Mathf.PI/12;Vector2 point=origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*5;
                    if(encounter.IsSafePosition(point) && RoomNavigation.ClearSight(origin,point))safe=point;
                }
                Check(safe.HasValue,"clear five-unit shooting lane");Local.Motor.PlaceAt(safe.Value);Local.Motor.CaptureState();Physics2D.SyncTransforms();
            }
            yield return Until(()=>encounter.Encounter.Enemies.Where(e=>e!=enemy).All(e=>e.Lifecycle==EnemyLifecycle.Dead),"isolated ranged combat fixture replicated");
            int healthBefore=session.Vitals(0).Current;
            yield return Until(()=>enemy.BehaviourState==EnemyBehaviourState.Windup && enemy.WindupProgress>.05f,"ranged telegraph replicated");
            Record("RANGED-WINDUP-PASS");
            yield return Until(()=>session.HostileProjectileCount>0,"hostile projectile visible on each peer");
            Record("RANGED-PROJECTILE-PASS");
            var evidence=SessionDiagnostics.Option("--ranged-evidence");
            if(Host && !string.IsNullOrEmpty(evidence))
            {System.IO.Directory.CreateDirectory(evidence);CaptureRangedFrame(System.IO.Path.Combine(evidence,"RangedCombat.png"));}
            yield return Until(()=>session.Vitals(0).Current<healthBefore,"authoritative projectile damage replicated");
            Check(session.Vitals(1).Current==100,"player in another room remains safe");Record("RANGED-DAMAGE-PASS");
            if(Host)
            {
                Local.Motor.PlaceAt((Vector2)enemy.transform.position+Vector2.right*.85f);Local.Motor.CaptureState();Physics2D.SyncTransforms();
                double deadline=Time.realtimeSinceStartupAsDouble+12;
                while(enemy.Lifecycle==EnemyLifecycle.Alive)
                {
                    Check(Time.realtimeSinceStartupAsDouble<deadline,"ranged enemy killed with equipped weapon");
                    var delta=new CoreVector(enemy.transform.position.x,enemy.transform.position.y)-Position;
                    Move(delta.Length()>.8f?CoreVector.Normalize(delta):CoreVector.Zero);Aim(delta);Attack();
                    yield return new WaitForSeconds(.2f);
                }
                Move(CoreVector.Zero);session.SaveCurrent();
            }
            yield return Until(()=>enemy.Lifecycle==EnemyLifecycle.Dead,"ranged death replicated");
            Check(session.RunXp==0,"no new XP rewards");Record("RANGED-COMBAT-PASS");
            if(!Host){yield return new WaitForSeconds(1);Application.Quit(0);}
        }
    }
}
#endif
