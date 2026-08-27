using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DghTools.Revit
{
    internal static class UpdateManager
    {
        public const string CurrentVersion = "0.8.1";
        private const string ManifestUrl = "https://raw.githubusercontent.com/DANYLO2204/DGH-Tools-Revit/main/update/update.json";
        private const int CheckIntervalHours = 24;

        public static void BeginBackgroundCheck()
        {
            Task.Run(() =>
            {
                try { CheckAndDownload(); }
                catch (Exception ex) { Log("Check failed: " + ex.Message); }
            });
        }

        public static void TryLaunchPendingUpdate(string assemblyPath)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(assemblyPath);
                string stateDir = GetStateDirectory();
                string versionFile = Path.Combine(stateDir, "pending-version.txt");
                string updater = Path.Combine(pluginDir, "ApplyUpdate.ps1");

                if (!File.Exists(versionFile) || !File.Exists(updater)) return;

                string pendingVersion = File.ReadAllText(versionFile).Trim();
                if (!IsNewerVersion(pendingVersion, CurrentVersion))
                {
                    ClearPending(stateDir);
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(updater) +
                                " -RevitPid " + Process.GetCurrentProcess().Id +
                                " -StateDir " + Quote(stateDir) +
                                " -PluginDir " + Quote(pluginDir) +
                                " -Version " + Quote(pendingVersion);

                Process.Start(psi);
            }
            catch (Exception ex) { Log("Launch updater failed: " + ex.Message); }
        }

        private static void CheckAndDownload()
        {
            if (!ShouldCheck()) return;
            WriteLastCheckNow();

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json = DownloadString(ManifestUrl + "?t=" + DateTime.UtcNow.Ticks);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            UpdateManifest manifest = serializer.Deserialize<UpdateManifest>(json);

            if (manifest == null || !manifest.enabled) return;
            if (!String.Equals(manifest.revit, "2023", StringComparison.OrdinalIgnoreCase)) return;
            if (!IsNewerVersion(manifest.version, CurrentVersion)) return;
            if (manifest.files == null || manifest.files.Count == 0) return;

            string stateDir = GetStateDirectory();
            Directory.CreateDirectory(stateDir);

            foreach (UpdateFile file in manifest.files)
            {
                if (file == null || String.IsNullOrWhiteSpace(file.name) || String.IsNullOrWhiteSpace(file.url))
                    continue;

                string safeName = Path.GetFileName(file.name);
                string destination = Path.Combine(stateDir, "pending-" + safeName);
                string temp = destination + ".part";
                SafeDelete(temp);
                DownloadFile(AddCacheBuster(file.url), temp);
                SafeDelete(destination);
                File.Move(temp, destination);
            }

            if (!String.IsNullOrWhiteSpace(manifest.updater_url))
            {
                string updaterTemp = Path.Combine(stateDir, "pending-ApplyUpdate.ps1.part");
                string updaterFinal = Path.Combine(stateDir, "pending-ApplyUpdate.ps1");
                SafeDelete(updaterTemp);
                DownloadFile(AddCacheBuster(manifest.updater_url), updaterTemp);
                SafeDelete(updaterFinal);
                File.Move(updaterTemp, updaterFinal);
            }

            File.WriteAllText(Path.Combine(stateDir, "pending-version.txt"), NormalizeVersion(manifest.version));
            Log("Queued update " + manifest.version + ".");
        }

        private static string AddCacheBuster(string url)
        {
            return url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
        }

        private static string DownloadString(string url)
        {
            using (WebClient client = CreateClient()) return client.DownloadString(url);
        }

        private static void DownloadFile(string url, string path)
        {
            using (WebClient client = CreateClient()) client.DownloadFile(url, path);
        }

        private static WebClient CreateClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "DGH-Tools-Revit-Updater/4.1";
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return client;
        }

        private static bool ShouldCheck()
        {
            try
            {
                string path = Path.Combine(GetStateDirectory(), "last-check.txt");
                if (!File.Exists(path)) return true;
                DateTime last;
                if (!DateTime.TryParse(File.ReadAllText(path).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last)) return true;
                return DateTime.UtcNow - last.ToUniversalTime() >= TimeSpan.FromHours(CheckIntervalHours);
            }
            catch { return true; }
        }

        private static void WriteLastCheckNow()
        {
            try
            {
                string dir = GetStateDirectory();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "last-check.txt"), DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        private static string GetStateDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DGH Tools", "GridEndAligner", "Updates");
        }

        private static bool IsNewerVersion(string candidate, string current)
        {
            Version a, b;
            return Version.TryParse(NormalizeVersion(candidate), out a) &&
                   Version.TryParse(NormalizeVersion(current), out b) && a > b;
        }

        private static string NormalizeVersion(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "0.0.0";
            value = value.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int dash = value.IndexOf('-');
            return dash >= 0 ? value.Substring(0, dash) : value;
        }

        private static void ClearPending(string stateDir)
        {
            try
            {
                foreach (string file in Directory.GetFiles(stateDir, "pending-*")) SafeDelete(file);
            }
            catch { }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Log(string text)
        {
            try
            {
                string dir = GetStateDirectory();
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "update-check.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + text + Environment.NewLine);
            }
            catch { }
        }

        private class UpdateManifest
        {
            public bool enabled { get; set; }
            public string version { get; set; }
            public string revit { get; set; }
            public List<UpdateFile> files { get; set; }
            public string updater_url { get; set; }
        }

        private class UpdateFile
        {
            public string name { get; set; }
            public string url { get; set; }
        }
    }
}
