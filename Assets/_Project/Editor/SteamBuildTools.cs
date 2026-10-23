using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using SpaceCrawler.Platform;
namespace SpaceCrawler.Editor
{
    public sealed class SteamBuildTools : IPostprocessBuildWithReport
    {
        public int callbackOrder=>100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if(report.summary.platform!=BuildTarget.StandaloneWindows64)return;
            string path=Path.Combine(Path.GetDirectoryName(report.summary.outputPath),"steam_appid.txt");
            if((report.summary.options&BuildOptions.Development)!=0)File.WriteAllText(path,SteamClientService.ExpectedAppId.ToString(),System.Text.Encoding.ASCII);
            else if(File.Exists(path))throw new BuildFailedException("The release output contains a development steam_appid.txt. Use a clean release output directory.");
        }
    }
}
