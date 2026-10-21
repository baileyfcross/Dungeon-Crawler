using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceCrawler.Core
{
    /// <summary>Seeded rectangle separation, Delaunay candidates, degree-bounded spanning tree and loops.
    /// Layout coordinates describe authored 24x16 room footprints, not physical corridors.</summary>
    internal static class TinyKeepLayout
    {
        private struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
        private readonly struct Edge
        {
            public readonly int A, B; public readonly long Length;
            public Edge(int a, int b, Point[] points)
            { A = Math.Min(a, b); B = Math.Max(a, b); long x = points[a].X - points[b].X, y = points[a].Y - points[b].Y; Length = x*x+y*y; }
        }
        public static DungeonGraph Generate(ulong seed, RunGenerationConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var random = new DeterministicRandom(seed);
            int count = configuration.Settings.MinRouteRooms + random.NextInt(configuration.Settings.MaxRouteRooms - configuration.Settings.MinRouteRooms + 1);
            if (count < 10 || count > 25) throw new ArgumentException("Spatial levels require 10 to 25 total rooms.");
            for (int attempt = 0; attempt < 128; attempt++)
            {
                var points = new Point[count];
                for (int i = 0; i < count; i++) points[i] = new Point(random.NextInt(121)-60, random.NextInt(121)-60);
                bool separated = false;
                for (int step = 0; step < 512; step++)
                {
                    bool overlap = false;
                    for (int a = 0; a < count; a++) for (int b = a+1; b < count; b++)
                    {
                        int dx = points[b].X-points[a].X, dy = points[b].Y-points[a].Y;
                        if (Math.Abs(dx) >= 28 || Math.Abs(dy) >= 20) continue;
                        overlap = true;
                        if (28-Math.Abs(dx) < 20-Math.Abs(dy))
                        { int move = (28-Math.Abs(dx)+1)/2 * (dx >= 0 ? 1 : -1); points[a].X -= move; points[b].X += move; }
                        else { int move = (20-Math.Abs(dy)+1)/2 * (dy >= 0 ? 1 : -1); points[a].Y -= move; points[b].Y += move; }
                    }
                    if (!overlap) { separated = true; break; }
                }
                if (!separated) continue;
                // The empty circumcircle predicate defines the Delaunay triangulation.
                // Exact integer determinants avoid platform-dependent floating point predicates.
                var candidateKeys = new HashSet<(int,int)>();
                for (int a = 0; a < count; a++) for (int b = a+1; b < count; b++) for (int c = b+1; c < count; c++)
                {
                    long orientation = Cross(points[a], points[b], points[c]);
                    if (orientation == 0) continue;
                    bool empty = true;
                    for (int d = 0; d < count; d++)
                    {
                        if (d == a || d == b || d == c) continue;
                        long ax = points[a].X-points[d].X, ay = points[a].Y-points[d].Y;
                        long bx = points[b].X-points[d].X, by = points[b].Y-points[d].Y;
                        long cx = points[c].X-points[d].X, cy = points[c].Y-points[d].Y;
                        long determinant = (ax*ax+ay*ay)*(bx*cy-by*cx) - (bx*bx+by*by)*(ax*cy-ay*cx) + (cx*cx+cy*cy)*(ax*by-ay*bx);
                        if (determinant == 0)
                        {
                            // Symbolic infinitesimal lift in stable point order resolves cocircular
                            // rooms into one triangulation, rather than keeping crossing diagonals.
                            long ca = bx*cy-by*cx, cb = -(ax*cy-ay*cx), cc = ax*by-ay*bx;
                            long cd = -ca-cb-cc;
                            int first = int.MaxValue;
                            if (ca != 0 && a < first) { first = a; determinant = ca; }
                            if (cb != 0 && b < first) { first = b; determinant = cb; }
                            if (cc != 0 && c < first) { first = c; determinant = cc; }
                            if (cd != 0 && d < first) determinant = cd;
                        }
                        if ((orientation > 0 && determinant > 0) || (orientation < 0 && determinant < 0)) { empty = false; break; }
                    }
                    if (empty) { candidateKeys.Add((a,b)); candidateKeys.Add((a,c)); candidateKeys.Add((b,c)); }
                }
                var candidates = candidateKeys.Select(k => new Edge(k.Item1,k.Item2,points)).OrderBy(e => e.Length).ThenBy(e => e.A).ThenBy(e => e.B).ToArray();
                var parent = Enumerable.Range(0,count).ToArray(); var degree = new int[count]; var chosen = new List<Edge>();
                foreach (var edge in candidates)
                {
                    int a = Root(parent,edge.A), b = Root(parent,edge.B);
                    if (a == b || degree[edge.A] == 4 || degree[edge.B] == 4) continue;
                    parent[a] = b; degree[edge.A]++; degree[edge.B]++; chosen.Add(edge);
                }
                if (chosen.Count != count-1) continue; // Retry instead of deleting an edge and disconnecting rooms.
                var treeKeys = new HashSet<(int,int)>(chosen.Select(e => (e.A,e.B)));
                foreach (var edge in candidates)
                    if (!treeKeys.Contains((edge.A,edge.B)) && degree[edge.A] < 4 && degree[edge.B] < 4 && random.NextInt(100) < 15)
                    { chosen.Add(edge); degree[edge.A]++; degree[edge.B]++; }
                int start = Enumerable.Range(0,count).OrderBy(i => points[i].X).ThenBy(i => points[i].Y).ThenBy(i => i).First();
                var distances = Enumerable.Repeat(-1,count).ToArray(); var queue = new Queue<int>(); distances[start] = 0; queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    foreach (var edge in chosen) { int next = edge.A == current ? edge.B : edge.B == current ? edge.A : -1;
                        if (next >= 0 && distances[next] < 0) { distances[next] = distances[current]+1; queue.Enqueue(next); } }
                }
                int terminal = Enumerable.Range(0,count).OrderByDescending(i => distances[i]).ThenBy(i => i).First();
                var order = new List<int> { start }; order.AddRange(Enumerable.Range(0,count).Where(i => i != start && i != terminal)); order.Add(terminal);
                var ids = new int[count]; var nodes = new RoomNode[count];
                for (int i = 0; i < count; i++) { int original = order[i]; ids[original] = i;
                    nodes[i] = new RoomNode(new RunRoomId(i), RunGenerator.ChooseDefinition(configuration,i == 0,random), i == count-1,points[original].X,points[original].Y); }
                var ports = new Dictionary<(int,int),int>();
                for (int room = 0; room < count; room++)
                {
                    int used = 0;
                    foreach (var edge in chosen.Where(e => e.A == room || e.B == room).OrderBy(e => e.Length).ThenBy(e => e.A).ThenBy(e => e.B))
                    {
                        int other = edge.A == room ? edge.B : edge.A;
                        int dx = points[other].X-points[room].X, dy = points[other].Y-points[room].Y;
                        int[] scores = { dy,dx,-dy,-dx };
                        int port = Enumerable.Range(0,4).Where(i => (used & (1<<i)) == 0).OrderByDescending(i => scores[i]).ThenBy(i => i).First();
                        used |= 1<<port; ports[(room,other)] = port;
                    }
                }
                var connections = new List<RoomConnection>();
                foreach (var e in chosen)
                { connections.Add(new RoomConnection(new RunRoomId(ids[e.A]), new RunRoomId(ids[e.B]), ports[(e.A,e.B)],ports[(e.B,e.A)]));
                  connections.Add(new RoomConnection(new RunRoomId(ids[e.B]), new RunRoomId(ids[e.A]), ports[(e.B,e.A)],ports[(e.A,e.B)])); }
                return new DungeonGraph(seed,2,configuration,nodes,connections.OrderBy(e => e.From.Value).ThenBy(e => e.FromPort).ToArray());
            }
            throw new InvalidOperationException("Could not construct a connected degree-bounded spatial level within the deterministic retry budget.");
        }
        private static int Root(int[] parent, int i) { while (parent[i] != i) i = parent[i]; return i; }
        private static long Cross(Point a, Point b, Point c) => (long)(b.X-a.X)*(c.Y-a.Y)-(long)(b.Y-a.Y)*(c.X-a.X);
    }
}
