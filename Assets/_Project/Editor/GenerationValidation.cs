using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SpaceCrawler.Core;
using SpaceCrawler.Gameplay;
using UnityEditor;

namespace SpaceCrawler.Editor
{
    /// <summary>Editor-only textual inspection. It does not own or mutate generation or save a game.</summary>
    public static class GenerationValidation
    {
        [MenuItem("Tools/Space Crawler/Phase 4/Dump Sample Graphs")]
        public static void DumpSampleGraphs()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation/import before inspecting graphs.");
            var asset = AssetDatabase.LoadAssetAtPath<RunGenerationDefinition>(GenerationFoundationAssets.GenerationPath);
            if (asset == null) throw new InvalidOperationException("Create the Phase 4 static definitions first.");
            var configuration = asset.CreateConfiguration();
            var directory = FoundationValidation.ResultsDirectory + "/Graphs";
            Directory.CreateDirectory(directory);
            var summary = new StringBuilder();
            foreach (var seed in new[] { 0UL, 1UL, 42UL, 2026UL, ulong.MaxValue })
            {
                var first = new RunGenerator().Generate(seed, configuration);
                var text = Format(first);
                // Recreate the snapshot from the same authored data to check repeatability end to end.
                var repeated = Format(new RunGenerator().Generate(seed, asset.CreateConfiguration()));
                if (!StringComparer.Ordinal.Equals(text, repeated)) throw new InvalidOperationException("Repeated seed changed its graph: " + seed);
                File.WriteAllText(directory + "/seed-" + seed.ToString(CultureInfo.InvariantCulture) + ".txt", text);
                var splits = first.Connections.GroupBy(edge => edge.From).Count(edges => edges.Count() == 2);
                summary.AppendLine($"Seed {seed}: nodes={first.Nodes.Count}, edges={first.Connections.Count}, routeRooms={first.Nodes.Count - splits}, splits={splits}, terminalCategory={first.GetRoom(first.TerminalRoomId).Category}, repeatIdentical=True");
            }
            File.WriteAllText(directory + "/summary.txt", summary.ToString());
        }

        private static string Format(DungeonGraph graph)
        {
            var text = new StringBuilder();
            var settings = graph.Configuration.Settings;
            text.AppendLine("Abstract run graph; no room loading, transition, or terminal gameplay objective.");
            text.AppendLine("Seed: " + graph.Seed.ToString(CultureInfo.InvariantCulture));
            text.AppendLine($"Generation version: {graph.GenerationVersion}; PRNG: {DeterministicRandom.Algorithm}");
            text.AppendLine($"Settings: routeRooms={settings.MinRouteRooms}..{settings.MaxRouteRooms} inclusive of Start/Terminal; maxSplits={settings.MaxSplits}");
            foreach (var definition in graph.Configuration.Definitions)
                text.AppendLine($"Definition: {definition.Id}; category={definition.Category}; weight={definition.Weight}");
            text.AppendLine($"Start: {graph.StartRoomId}; Terminal: {graph.TerminalRoomId}");
            foreach (var node in graph.Nodes)
            {
                var exits = graph.Connections.Where(edge => edge.From.Equals(node.Id)).Select(edge => edge.To.ToString());
                text.AppendLine($"{node.Id} [{node.DefinitionId} / {node.Category}{(node.IsTerminal ? " / Terminal" : "")}] -> {string.Join(", ", exits)}");
            }
            return text.ToString();
        }
    }
}
