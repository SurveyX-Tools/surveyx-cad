using System;
using System.IO;
using System.Xml.Serialization;

namespace SurveyX.Core
{
    public sealed class Settings
    {
        public string Language = "ar";
        public double TextHeight = 0.5;
        public int Decimals = 3;
        public string LayerPrefix = "SX_";
        public bool Place3D = true;
        public bool ShowName = true;
        public bool ShowElevation = true;
        public bool ShowDesc = true;
        public string LastSourceCrs = "EPSG:32640";
        public string LastTargetCrs = "EPSG:3997";
        public string LastFolder = "";
        public string LastPreset = "";
        public int TableRowsPerColumn = 40;
        public bool CheckUpdates = true;
        public string SkippedVersion = "";

        // reports
        public string CompanyName = "";
        public string LogoPath = "";
        public string ProjectName = "";
        public string PreparedBy = "";
        public string SubmittedTo = "";
        // surfaces / earthworks
        public double GridCell = 0;          // 0 = automatic
        public double MaxEdge = 50;          // TIN max triangle edge (m), 0 = no limit
        public double TolPlus = 0.010;       // level check tolerances (m)
        public double TolMinus = 0.010;
        public double ContourInterval = 0.5;
        public double SectionInterval = 20;
        public double SectionLeft = 20;
        public double SectionRight = 20;
        public double SectionStep = 1;
        public double StartChainage = 0;
        public double VerticalExaggeration = 10;
        public string LastOglPath = "";
        public string ApiKeyProtected = "";   // DPAPI-encrypted, per Windows user
        public string AiModel = "claude-sonnet-5-5";
        public string LastDesignPath = "";

        private static Settings _current;
        public static Settings Current => _current ?? (_current = Load());

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SurveyXCAD");
        private static string FilePath => Path.Combine(Folder, "settings.xml");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                        return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(fs);
            }
            catch { /* corrupted settings → defaults */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using (var fs = File.Create(FilePath))
                    new XmlSerializer(typeof(Settings)).Serialize(fs, this);
            }
            catch { }
        }
    }
}
