using System;
using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Small adapter over the vendor grid. Only static solid geometry blocks navigation;
    /// triggers, players, and enemy hit shapes do not become baked obstacles.</summary>
    public sealed class RoomNavigation : IDisposable
    {
        public const float BodyRadius = .4f;
        private const float CellSize = .5f;
        private readonly GameObject owner;
        private readonly AstarPath astar;
        private GridGraph graph;
        private readonly List<StaticObstacle> obstacles=new List<StaticObstacle>();
        private readonly struct StaticObstacle
        {
            public readonly Collider2D Collider;
            public readonly Bounds Bounds;
            public StaticObstacle(Collider2D collider,float radius){Collider=collider;var bounds=collider.bounds;bounds.Expand(radius*2);Bounds=bounds;}
        }
        private bool NodeBlocked(Vector2 point,float radius)
        {
            float squared=radius*radius;
            foreach(var obstacle in obstacles)
            {
                var bounds=obstacle.Bounds;
                if(point.x<bounds.min.x || point.x>bounds.max.x || point.y<bounds.min.y || point.y>bounds.max.y)continue;
                if((obstacle.Collider.ClosestPoint(point)-point).sqrMagnitude<=squared)return true;
            }
            return false;
        }
        public int PathRequests { get; private set; }

        public RoomNavigation(Transform parent, RoomNavigation shared = null)
        {
            if(shared!=null){astar=shared.astar;return;}
            owner = new GameObject("RoomNavigation"); owner.transform.SetParent(parent, false); owner.SetActive(false);
            astar = owner.AddComponent<AstarPath>(); astar.scanOnStartup = false;
            astar.threadCount = ThreadCount.None; astar.logPathResults = PathLog.None;
            // Small per-room scans must not stop the entire game for a full-heap collection.
            // The library opt-out is local to this instance; normal automatic GC is unchanged.
            astar.CollectGarbageAfterScan = false;
            owner.SetActive(true);
        }

        public void Build(AuthoredRoom room)
        {
            astar.FlushWorkItems();
            if (graph != null) astar.data.RemoveGraph(graph);
            graph = (GridGraph)astar.data.AddGraph(typeof(GridGraph));
            var bounds = room.PlayableBounds;
            graph.is2D = true; graph.center = room.transform.TransformPoint(bounds.center);
            graph.SetDimensions(Mathf.CeilToInt(bounds.width / CellSize), Mathf.CeilToInt(bounds.height / CellSize), CellSize);
            graph.cutCorners = false; graph.collision.heightCheck = false; graph.collision.collisionCheck = false;
            Physics2D.SyncTransforms();
            // Snapshot the room's static geometry once. Per-cell overlap queries allocate and cross the
            // physics boundary thousands of times; bounds reject most cells before an exact shape query.
            float radius=BodyRadius+.05f;obstacles.Clear();
            foreach(var collider in Physics2D.OverlapBoxAll(graph.center,bounds.size+Vector2.one*(radius*2),0))
                if(IsSolid(collider))obstacles.Add(new StaticObstacle(collider,radius));
            // Other occupied rooms share this AstarPath. Never rescan their grids or erase their cover.
            astar.Scan(new NavGraph[]{graph});
            astar.AddWorkItem(new AstarWorkItem(context =>
            {
                graph.GetNodes(node =>
                {
                    Vector3 point = (Vector3)node.position;
                    node.Walkable = !NodeBlocked(point,radius);
                });
                graph.GetNodes(node => graph.CalculateConnections((GridNode)node));
                // Walkable/connection setters mark affected nodes dirty; FlushWorkItems updates connectivity.
            }));
            astar.FlushWorkItems();obstacles.Clear();
        }

        public bool Reachable(Vector2 from, Vector2 to)
        {
            if (graph == null) return false;
            // GridGraph.GetNearest returns an unconstrained cell, which may be blocked beside a wall.
            // Constrain both endpoints to walkable cells in this room's graph.
            var start = graph.GetNearestForce(from, NNConstraint.Default).node;
            var end = graph.GetNearestForce(to, NNConstraint.Default).node;
            return start != null && end != null && ((Vector3)start.position - (Vector3)from).sqrMagnitude < 1
                && ((Vector3)end.position - (Vector3)to).sqrMagnitude < 1 && PathUtilities.IsPathPossible(start, end);
        }

        public List<Vector3> FindPath(Vector2 from, Vector2 to)
        {
            if (!Reachable(from, to)) return null;
            PathRequests++;
            var path = ABPath.Construct(from, to);
            path.Claim(this);
            try
            {
                AstarPath.StartPath(path); path.BlockUntilCalculated();
                return path.error ? null : new List<Vector3>(path.vectorPath);
            }
            finally { path.Release(this); }
        }

        public static bool IsSolid(Collider2D collider) => collider != null && !collider.isTrigger && collider.attachedRigidbody == null;
        public static bool Blocked(Vector2 position, float radius)
        {
            foreach (var collider in Physics2D.OverlapCircleAll(position, radius)) if (IsSolid(collider)) return true;
            return false;
        }
        public static bool ClearSight(Vector2 from, Vector2 to)
        {
            foreach (var hit in Physics2D.LinecastAll(from, to)) if (IsSolid(hit.collider)) return false;
            return true;
        }
        public static Vector2 Move(Vector2 position, Vector2 destination, float distance)
        {
            var delta = destination - position; var length = Mathf.Min(distance, delta.magnitude);
            if (length <= .00001f) return position;
            var direction = delta.normalized;
            foreach (var hit in Physics2D.CircleCastAll(position, BodyRadius, direction, length + .02f))
                if (IsSolid(hit.collider)) length = Mathf.Min(length, Mathf.Max(0, hit.distance - .02f));
            return position + direction * length;
        }
        public void Dispose()
        {
            if(astar!=null && graph!=null){astar.FlushWorkItems();astar.data.RemoveGraph(graph);graph=null;}
            if (owner != null) { owner.SetActive(false); UnityEngine.Object.Destroy(owner); }
        }
    }
}
