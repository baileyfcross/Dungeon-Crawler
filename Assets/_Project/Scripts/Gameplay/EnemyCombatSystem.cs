using System;
using System.Collections.Generic;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Host-only world observations and execution of Core enemy intentions.</summary>
    public sealed class EnemyCombatSystem : IDisposable
    {
        private readonly Transform owner;
        private readonly Func<int, Vector2> position;
        private readonly Func<RunLife> life;
        private readonly Action<PlayerIdentity, Guid, int> damage;
        private Dictionary<EnemyActor, Route> routes = new Dictionary<EnemyActor, Route>();
        private sealed class RoomPaths
        { public RoomNavigation Navigation; public readonly Dictionary<EnemyActor,Route> Routes = new Dictionary<EnemyActor,Route>(); }
        private readonly Dictionary<AuthoredRoom,RoomPaths> contexts = new Dictionary<AuthoredRoom,RoomPaths>();
        private RoomNavigation sharedNavigation;
        private RoomNavigation navigation;
        private AuthoredRoom room;
        private float clock;
        private readonly List<HostileShot> shots=new List<HostileShot>();
        private long shotSequence;
        private sealed class HostileShot
        {public AuthoredRoom Room;public EnemyActor Source;public EnemyProjectileSimulation Model;public EnemyProjectileView View;public long Sequence;}
        public int ProjectileCount => shots.Count;
        public IEnumerable<EnemyProjectileSnapshot> ProjectileSnapshots
        {
            get{foreach(var shot in shots)yield return new EnemyProjectileSnapshot{Encounter=shot.Source.Identity.Encounter,Spawn=shot.Source.Identity.Spawn,
                Sequence=shot.Sequence,Position=Unity(shot.Model.Position),Direction=Unity(shot.Model.Direction)};}
        }
        public void RetainOccupiedRooms(IEnumerable<AuthoredRoom> occupied)
        {
            var keep=new HashSet<AuthoredRoom>(occupied);
            for(int i=shots.Count-1;i>=0;i--)if(shots[i].Room==null || !keep.Contains(shots[i].Room))RemoveShot(i);
        }
        private void RemoveShot(int index){if(shots[index].View!=null)UnityEngine.Object.Destroy(shots[index].View.gameObject);shots.RemoveAt(index);}
        private void Fire(EnemyActor enemy,Vector2 origin)
        {
            if(shots.Count>=32)return;
            var material=enemy.GetComponentInChildren<LineRenderer>().sharedMaterial;
            var model=new EnemyProjectileSimulation(Core(origin),Core(enemy.Facing),enemy.RangedConfiguration.ProjectileSpeed);
            var view=EnemyProjectileView.Create(owner,material);view.Present(origin,enemy.Facing);
            shots.Add(new HostileShot{Room=room,Source=enemy,Model=model,View=view,Sequence=++shotSequence});
        }
        private void AdvanceShots(float seconds)
        {
            for(int index=shots.Count-1;index>=0;index--)
            {
                var shot=shots[index];if(shot.Room==null){RemoveShot(index);continue;}
                if(shot.Room!=room)continue;
                var model=life();var start=Unity(shot.Model.Position);var direction=Unity(shot.Model.Direction);
                float distance=shot.Model.Distance(seconds),travelled=distance;int victim=-1;bool blocked=false;
                foreach(var hit in Physics2D.CircleCastAll(start,.11f,direction,distance))
                    if(RoomNavigation.IsSolid(hit.collider) && hit.distance<=travelled){travelled=hit.distance;blocked=true;}
                for(int slot=0;slot<model.Count;slot++)
                {
                    Vector2 player=position(slot);
                    if(model[slot].State!=PlayerLifeState.Alive || !room.PlayableBounds.Contains(room.transform.InverseTransformPoint(player)))continue;
                    // Continuous segment/circle sweep; never skip a player on a long fixed step.
                    Vector2 delta=start-player;float along=Vector2.Dot(delta,direction),c=delta.sqrMagnitude-.46f*.46f;
                    float discriminant=along*along-c;if(discriminant<0)continue;
                    float entry=c<=0?0:-along-Mathf.Sqrt(discriminant);
                    if(entry>=0 && entry<travelled){travelled=entry;victim=slot;}
                }
                shot.Model.Advance(seconds,travelled,blocked||victim>=0 || !room.PlayableBounds.Contains(room.transform.InverseTransformPoint(start+direction*travelled)));
                shot.View.Present(Unity(shot.Model.Position),direction);
                if(!shot.Model.Active)RemoveShot(index);
                if(victim>=0)damage(new PlayerIdentity(victim+1),shot.Model.AttackId,shot.Source.Configuration.Damage);
                if(model.Ended)break;
            }
        }
        private sealed class Route { public List<Vector3> Points; public int Next; public float Due; public Vector2 Destination; }

        public EnemyCombatSystem(Transform owner, Func<int, Vector2> position, Func<RunLife> life, Action<PlayerIdentity, Guid, int> damage)
        { this.owner = owner; this.position = position; this.life = life; this.damage = damage; }

        public void Advance(AuthoredRoom current, bool running, float seconds)
        {
            var retired=new List<AuthoredRoom>();
            foreach(var existing in contexts.Keys)if(existing==null)retired.Add(existing);
            foreach(var existing in retired){contexts[existing].Navigation.Dispose();contexts.Remove(existing);}
            // Unity's destroyed objects compare equal to null but still have managed fields.
            // A failed room replacement must never tick the retired encounter's components.
            if (current == null) { room = null; return; }
            room=current;
            if(running && !life().Ended)AdvanceShots(seconds);
            if (room?.Encounter == null || !HasBehaviour()) return;
            if(!contexts.TryGetValue(room,out var context))
            {
                sharedNavigation ??= new RoomNavigation(owner);
                context=new RoomPaths{Navigation=new RoomNavigation(owner,sharedNavigation)};
                context.Navigation.Build(room);contexts.Add(room,context);
            }
            navigation=context.Navigation;routes=context.Routes;
            clock += seconds;
            foreach (var enemy in room.Encounter.Enemies)
            {
                if (enemy == null || enemy.Behaviour == null) continue;
                var brain = enemy.Behaviour; var model = life(); var origin = (Vector2)enemy.transform.position;
                var observations = new EnemyObservation[model.Count];
                for (var i = 0; i < observations.Length; i++)
                {
                    var point = position(i);
                    bool alive = model[i].State == PlayerLifeState.Alive && room.PlayableBounds.Contains(room.transform.InverseTransformPoint(point));
                    observations[i] = new EnemyObservation(new PlayerIdentity(i + 1), Core(point), alive,
                        alive && RoomNavigation.ClearSight(origin, point), alive && navigation.Reachable(origin, point));
                }
                brain.Advance(seconds, Core(origin), observations, running && !model.Ended);
                enemy.SynchronizeBehaviour();
                if (!running || model.Ended || enemy.Lifecycle != EnemyLifecycle.Alive) continue;
                if (brain.StrikeTarget.HasValue)
                {
                    var victim = brain.StrikeTarget.Value;
                    if (model[victim.Value - 1].State == PlayerLifeState.Alive && RoomNavigation.ClearSight(origin, position(victim.Value - 1)))
                        {
                            if(enemy.Archetype==EnemyArchetype.Ranged)Fire(enemy,origin);
                            else damage(victim, Guid.NewGuid(), enemy.Configuration.Damage);
                        }
                }
                if (!brain.Destination.HasValue) continue;
                var destination = Unity(brain.Destination.Value);
                if(!room.PlayableBounds.Contains(room.transform.InverseTransformPoint(destination)))continue;
                if (!routes.TryGetValue(enemy, out var route)) { route = new Route { Due = clock + (enemy.Identity.Spawn - 1) * .01f }; routes.Add(enemy, route); }
                if (clock >= route.Due)
                {
                    route.Due = clock + .25f; route.Destination = destination;
                    route.Points = navigation.FindPath(origin, destination); route.Next = 0;
                }
                if (route.Points == null) continue;
                while (route.Next < route.Points.Count && Vector2.Distance(origin, route.Points[route.Next]) < .15f) route.Next++;
                var next = route.Next < route.Points.Count ? (Vector2)route.Points[route.Next] : route.Destination;
                var moved = RoomNavigation.Move(origin, next, enemy.Configuration.Speed * seconds);
                enemy.transform.position = new Vector3(moved.x, moved.y, 0);
            }
        }
        private bool HasBehaviour() { foreach (var enemy in room.Encounter.Enemies) if (enemy != null && enemy.Behaviour != null) return true; return false; }
        private static CoreVector Core(Vector2 value) => new CoreVector(value.x, value.y);
        private static Vector2 Unity(CoreVector value) => new Vector2(value.X, value.Y);
        public void ClearRooms()
        { while(shots.Count>0)RemoveShot(shots.Count-1);foreach(var entry in contexts.Values)entry.Navigation.Dispose();contexts.Clear();routes.Clear();room=null; }
        public void Dispose() { ClearRooms();sharedNavigation?.Dispose();sharedNavigation=null; }
    }
}
