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
        private readonly Dictionary<EnemyActor, Route> routes = new Dictionary<EnemyActor, Route>();
        private RoomNavigation navigation;
        private AuthoredRoom room;
        private float clock;
        private sealed class Route { public List<Vector3> Points; public int Next; public float Due; public Vector2 Destination; }

        public EnemyCombatSystem(Transform owner, Func<int, Vector2> position, Func<RunLife> life, Action<PlayerIdentity, Guid, int> damage)
        { this.owner = owner; this.position = position; this.life = life; this.damage = damage; }

        public void Advance(AuthoredRoom current, bool running, float seconds)
        {
            // Unity's destroyed objects compare equal to null but still have managed fields.
            // A failed room replacement must never tick the retired encounter's components.
            if (current == null) { room = null; routes.Clear(); return; }
            if (room != current) { room = current; routes.Clear(); if (room?.Encounter != null && HasMelee()) { navigation ??= new RoomNavigation(owner); navigation.Build(room); } }
            if (room?.Encounter == null || !HasMelee()) return;
            clock += seconds;
            foreach (var enemy in room.Encounter.Enemies)
            {
                if (enemy == null || enemy.Behaviour == null) continue;
                var brain = enemy.Behaviour; var model = life(); var origin = (Vector2)enemy.transform.position;
                var observations = new EnemyObservation[model.Count];
                for (var i = 0; i < observations.Length; i++)
                {
                    var point = position(i);
                    observations[i] = new EnemyObservation(new PlayerIdentity(i + 1), Core(point), model[i].State == PlayerLifeState.Alive,
                        RoomNavigation.ClearSight(origin, point), navigation.Reachable(origin, point));
                }
                brain.Advance(seconds, Core(origin), observations, running && !model.Ended);
                enemy.SynchronizeBehaviour();
                if (!running || model.Ended || enemy.Lifecycle != EnemyLifecycle.Alive) continue;
                if (brain.StrikeTarget.HasValue)
                {
                    var victim = brain.StrikeTarget.Value;
                    if (model[victim.Value - 1].State == PlayerLifeState.Alive && RoomNavigation.ClearSight(origin, position(victim.Value - 1)))
                        damage(victim, Guid.NewGuid(), enemy.Configuration.Damage);
                }
                if (!brain.Destination.HasValue) continue;
                var destination = Unity(brain.Destination.Value);
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
        private bool HasMelee() { foreach (var enemy in room.Encounter.Enemies) if (enemy != null && enemy.Behaviour != null) return true; return false; }
        private static CoreVector Core(Vector2 value) => new CoreVector(value.x, value.y);
        private static Vector2 Unity(CoreVector value) => new Vector2(value.X, value.Y);
        public void Dispose() { routes.Clear(); navigation?.Dispose(); }
    }
}
