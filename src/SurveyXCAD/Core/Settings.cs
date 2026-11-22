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
