using System.Reflection;

namespace SurveyX.Core
{
    public static class AppInfo
    {
        public const string Name = "SurveyX CAD";
        public const string Author = "Ahmed Gamal Shalaby";
        public const string GitHubOwner = "SurveyX-Tools";
        public const string GitHubRepo = "surveyx-cad";
        public static string ReleasesUrl => "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
        public static string LatestApiUrl => "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "1.0.0" : v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
