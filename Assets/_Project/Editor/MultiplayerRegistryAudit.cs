using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace SpaceCrawler.Editor
{
    /// <summary>Live registry inspection and the explicitly approved, dependency-guarded spike installation.</summary>
    public static class MultiplayerRegistryAudit
    {
        private static readonly string[] Queries = {
            "com.unity.netcode.gameobjects", "com.unity.transport",
            "com.unity.netcode.gameobjects@2.7.0", "com.unity.transport@2.6.0" };
        private static SearchRequest request;
        private static int queryIndex;
        private static Audit report;
        private static AddAndRemoveRequest installation;

        [Serializable] private sealed class Dependency { public string name, version; }
        [Serializable] private sealed class Package
        {
            public string query, name, version, packageId, source, latestCompatible;
            public string[] compatible;
            public Dependency[] dependencies;
        }
        [Serializable] private sealed class Audit
        {
            public string unity, utc, status, error;
            public List<Package> packages = new List<Package>();
        }

        [MenuItem("Tools/Space Crawler/Phase 5A/Inspect Registry")]
        public static void Inspect()
        {
            if (request != null || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Registry inspection requires an idle Editor and no active inspection.");
            queryIndex = 0;
            report = new Audit { unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), status = "Running" };
            Write();
            EditorApplication.update += Poll;
            StartNext();
        }

        private static void StartNext()
        {
            if (queryIndex == Queries.Length)
            {
                report.status = "Completed";
                Write();
                EditorApplication.update -= Poll;
                request = null;
                Debug.Log("[Phase5A] Read-only registry inspection complete: Logs/Phase5A/RegistryAudit.json");
                return;
            }
            request = Client.Search(Queries[queryIndex], false);
        }

        private static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            if (request.Status != StatusCode.Success)
            {
                report.status = "Failed";
                report.error = Queries[queryIndex] + ": " + request.Error?.message;
                Write();
                EditorApplication.update -= Poll;
                request = null;
                return;
            }
            foreach (var info in request.Result)
                report.packages.Add(new Package {
                    query = Queries[queryIndex], name = info.name, version = info.version,
                    packageId = info.packageId, source = info.source.ToString(),
                    latestCompatible = info.versions.latestCompatible, compatible = info.versions.compatible,
                    dependencies = info.dependencies.Select(d => new Dependency { name = d.name, version = d.version }).ToArray()
                });
            queryIndex++;
            Write();
            StartNext();
        }

        private static void Write()
        {
            Directory.CreateDirectory("Logs/Phase5A");
            File.WriteAllText("Logs/Phase5A/RegistryAudit.json", JsonUtility.ToJson(report, true));
        }

        [MenuItem("Tools/Space Crawler/Phase 5A/Install Verified Stack")]
        public static void InstallVerifiedStack()
        {
            if (request != null || installation != null || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Package installation requires an idle Editor.");
            var audit = JsonUtility.FromJson<Audit>(File.ReadAllText("Logs/Phase5A/RegistryAudit.json"));
            if (audit.status != "Completed" || audit.unity != Application.unityVersion)
                throw new InvalidOperationException("Complete this Editor's registry inspection first.");
            var wanted = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "com.unity.netcode.gameobjects", "2.13.2" }, { "com.unity.transport", "2.7.4" } };
            var existing = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, p => p.version, StringComparer.Ordinal);
            foreach (var pair in wanted)
            {
                var package = audit.packages.Single(p => p.query == pair.Key);
                if (package.version != pair.Value || !package.compatible.Contains(pair.Value))
                    throw new InvalidOperationException("Approved stable version is not in this Editor's compatible registry list.");
                foreach (var dependency in package.dependencies)
                {
                    if (wanted.TryGetValue(dependency.name, out var selected) && new Version(selected) >= new Version(dependency.version)) continue;
                    if (!existing.TryGetValue(dependency.name, out var installed) || new Version(installed) < new Version(dependency.version))
                        throw new InvalidOperationException("STOP: unexpected dependency addition/upgrade: " + dependency.name + "@" + dependency.version);
                }
            }
            File.WriteAllText("Logs/Phase5A/PackageInstall.txt", "Started: " + DateTime.UtcNow.ToString("O") + "\nNGO 2.13.2 / Unity Transport 2.7.4; other dependencies already installed.\n");
            installation = Client.AddAndRemove(wanted.Select(p => p.Key + "@" + p.Value).ToArray());
            EditorApplication.update += PollInstallation;
        }

        private static void PollInstallation()
        {
            if (installation == null || !installation.IsCompleted) return;
            File.AppendAllText("Logs/Phase5A/PackageInstall.txt", installation.Status + ": " + installation.Error?.message + "\n");
            installation = null;
            EditorApplication.update -= PollInstallation;
        }
    }
}
