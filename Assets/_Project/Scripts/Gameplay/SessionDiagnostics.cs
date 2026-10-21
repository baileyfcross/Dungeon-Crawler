using System;
using System.IO;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Development-process launch options and read-only validation evidence; no gameplay command file.</summary>
    public static class SessionDiagnostics
    {
        private static double nextReport;
        public static string Option(string name)
        {
            if (!Debug.isDebugBuild) return null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        public static void Record(MultiplayerSession session, double now)
        {
            var path = Option("--session-report");
            if (string.IsNullOrEmpty(path) || now < nextReport) return;
            nextReport = now + 0.25;
            File.WriteAllText(path, "UTC=" + DateTime.UtcNow.ToString("O") + "\n" + session.Describe());
        }
    }
}
