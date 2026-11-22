using System;

namespace SurveyX.Core
{
    [Flags]
    public enum PointIssue
    {
        None = 0,
        DuplicateName = 1,
        DuplicateCoords = 2,
        OutlierZ = 4,
        OutlierXY = 8,
        MissingZ = 16,
    }

    public sealed class SurveyPoint
    {
        public string Name { get; set; } = "";
        /// <summary>Easting (or longitude in degrees for geographic data).</summary>
        public double E { get; set; }
        /// <summary>Northing (or latitude in degrees for geographic data).</summary>
        public double N { get; set; }
        public double Z { get; set; }
        public bool HasZ { get; set; } = true;
        public string Desc { get; set; } = "";
        public int SourceLine { get; set; }
        public PointIssue Issues { get; set; }
        public bool Include { get; set; } = true;

        public SurveyPoint Clone() => (SurveyPoint)MemberwiseClone();
    }
}
