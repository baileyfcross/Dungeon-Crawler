using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpaceCrawler.Core
{
    /// <summary>Canonical agreement descriptor; does not alter generation or the immutable graph.</summary>
    public static class GraphFingerprint
    {
        public static string Compute(DungeonGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            using (var bytes = new MemoryStream())
            {
                using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
                {
                    writer.Write("SpaceCrawler.GraphFingerprint.v1");
                    writer.Write(graph.Seed); writer.Write(graph.GenerationVersion);
                    writer.Write(graph.StartRoomId.Value); writer.Write(graph.TerminalRoomId.Value);
                    writer.Write(graph.Nodes.Count);
                    foreach (var node in graph.Nodes.OrderBy(n => n.Id.Value))
                    {
                        writer.Write(node.Id.Value); writer.Write(node.DefinitionId);
                        writer.Write((int)node.Category); writer.Write(node.IsTerminal);
                    }
                    writer.Write(graph.Connections.Count);
                    foreach (var edge in graph.Connections.OrderBy(e => e.From.Value).ThenBy(e => e.To.Value))
                    { writer.Write(edge.From.Value); writer.Write(edge.To.Value); }
                }
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
