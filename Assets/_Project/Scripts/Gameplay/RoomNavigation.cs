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
        public int PathRequests { get; private set; }

        public RoomNavigation(Transform parent)
        {
            owner = new GameObject("RoomNavigation"); owner.transform.SetParent(parent, false); owner.SetActive(false);
            astar = owner.AddComponent<AstarPath>(); astar.scanOnStartup = false;
            astar.threadCount = ThreadCount.None; astar.logPathResults = PathLog.None;
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
            Physics2D.SyncTransforms(); astar.Scan();
            astar.AddWorkItem(new AstarWorkItem(context =>
            {
                graph.GetNodes(node =>
                {
                    Vector3 point = (Vector3)node.position;
                    node.Walkable = !Blocked(point, BodyRadius + .05f);
                });
                graph.GetNodes(node => graph.CalculateConnections((GridNode)node));
                context.QueueFloodFill();
            }));
            astar.FlushWorkItems();
        }

        public bool Reachable(Vector2 from, Vector2 to)
        {
            if (graph == null) return false;
            var start = graph.GetNearest(from, NNConstraint.Default).node;
            var end = graph.GetNearest(to, NNConstraint.Default).node;
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
        public void Dispose() { if (owner != null) { owner.SetActive(false); UnityEngine.Object.Destroy(owner); } }
    }
}
