using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Checks GitHub Releases for a newer version and tells the user on the command line.</summary>
    public static class Updater
    {
        private static string _pendingMessage;

        public static void CheckInBackground()
        {
            Task.Run(async () =>
            {
                try
                {
                    string latest = await GetLatestVersionAsync().ConfigureAwait(false);
                    if (latest != null && IsNewer(latest, AppInfo.Version) && latest != Settings.Current.SkippedVersion)
                    {
                        _pendingMessage = "\n[SurveyX] New version " + latest + " is available (you have " + AppInfo.Version +
                                          "). Type SXUPDATE to download it.\n";
                        AcApp.Idle += ShowOnIdle;
                    }
                }
                catch { /* offline or rate-limited: silently ignore */ }
            });
        }

        private static void ShowOnIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= ShowOnIdle;
            try { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(_pendingMessage); } catch { }
        }

        public static async Task<string> GetLatestVersionAsync()
        {
#if !NET
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SurveyXCAD/" + AppInfo.Version);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await http.GetStringAsync(AppInfo.LatestApiUrl).ConfigureAwait(false);
                var obj = MiniJson.Parse(json) as Dictionary<string, object>;
                if (obj == null || !obj.ContainsKey("tag_name")) return null;
                return ((obj["tag_name"] as string) ?? "").TrimStart('v', 'V');
            }
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            return Version.TryParse(Pad(latest), out a) && Version.TryParse(Pad(current), out b) && a > b;
        }

        private static string Pad(string v)
        {
            var parts = (v ?? "0").Split('.');
            return parts.Length == 1 ? v + ".0" : v;
        }
    }
}
