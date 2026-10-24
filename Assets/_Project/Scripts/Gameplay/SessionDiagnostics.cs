using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Development-process launch options and read-only validation evidence; no gameplay command file.</summary>
    public static class SessionDiagnostics
    {
        private static double nextReport;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static void ConfirmNewRun(ApplicationScreen screen)
        {
            var panel=screen.GetComponent<NewRunPanel>();
            if(panel==null || !panel.IsOpen)return;
            var choice=screen.View.Q<UnityEngine.UIElements.DropdownField>("player-class");
            if(choice.index==0)choice.value="Pilot";
            var button=screen.View.Q<UnityEngine.UIElements.Button>("start-run");
            using(var submit=UnityEngine.UIElements.NavigationSubmitEvent.GetPooled())
            {submit.target=button;button.SendEvent(submit);}
        }
#endif
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
