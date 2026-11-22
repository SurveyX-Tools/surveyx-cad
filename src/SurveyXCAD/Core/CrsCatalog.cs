using System;
using System.Collections.Generic;
using System.Linq;

namespace SurveyX.Core
{
    public sealed class Datum
    {
        public string Name { get; }
        public Ellipsoid Ellipsoid { get; }
        public Helmert ToWgs84 { get; }
        public Datum(string name, Ellipsoid el, Helmert toWgs84) { Name = name; Ellipsoid = el; ToWgs84 = toWgs84; }

        public static readonly Datum WGS84 = new Datum("WGS 84", Ellipsoid.WGS84, Helmert.None);

        // Egypt 1907 to WGS 84 (1)
        public static readonly Datum Egypt1907 = new Datum("Egypt 1907", Ellipsoid.Helmert1906, Helmert.ThreeParam(-130, 110, -13));

        // Nahrwan 1967 to WGS 84 (3) — UAE onshore & offshore
        public static readonly Datum Nahrwan1967UAE = new Datum("Nahrwan 1967 (UAE)", Ellipsoid.Clarke1880RGS, Helmert.ThreeParam(-249, -156, 381));

        // Nahrwan 1967 to WGS 84 (12) — Abu Dhabi onshore, coordinate frame
        public static readonly Datum Nahrwan1967AbuDhabi = new Datum("Nahrwan 1967 (Abu Dhabi onshore)", Ellipsoid.Clarke1880RGS,
            new Helmert { Tx = -253.4392, Ty = -148.452, Tz = 386.5267, Rx = -0.15605, Ry = -0.43, Rz = 0.1013, Ppm = -0.0424, CoordinateFrame = true });

        // Nahrwan 1967 to WGS 84 (10) — Dubai municipality
        public static readonly Datum Nahrwan1967Dubai = new Datum("Nahrwan 1967 (Dubai)", Ellipsoid.Clarke1880RGS, Helmert.ThreeParam(-250.7, -157.9, 380.4));
    }

    public enum CrsKind { Geographic, Projected }

    public sealed class Crs
    {
        public string Id { get; }          // stable key, e.g. "EPSG:32640"
        public string NameEn { get; }
        public string NameAr { get; }
        public string Group { get; }       // UAE / Egypt / Saudi / World
        public Datum Datum { get; }
        public TransverseMercator Projection { get; } // null for geographic
        public CrsKind Kind => Projection == null ? CrsKind.Geographic : CrsKind.Projected;

        public Crs(string id, string en, string ar, string group, Datum datum, TransverseMercator tm)
        {
            Id = id; NameEn = en; NameAr = ar; Group = group; Datum = datum; Projection = tm;
        }

        public string DisplayName(bool arabic) => (arabic ? NameAr : NameEn) + "  [" + Id + "]";
        public override string ToString() => DisplayName(Lang.IsArabic);
    }

    public static class CrsCatalog
    {
        public static readonly List<Crs> All = Build();

        public static Crs Find(string id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        public static Crs Wgs84Geo => Find("EPSG:4326");

        private static TransverseMercator Utm(Ellipsoid el, int zone) =>
            new TransverseMercator(el, 0, zone * 6 - 183, 0.9996, 500000, 0);

        private static List<Crs> Build()
        {
            var l = new List<Crs>();
            l.Add(new Crs("EPSG:4326", "WGS 84 Geographic (Lat/Long)", "WGS 84 جغرافي (خط عرض / خط طول)", "World", Datum.WGS84, null));

            // UAE
            l.Add(new Crs("EPSG:3997", "Dubai Local TM (DLTM)", "دبي المحلي DLTM", "UAE", Datum.WGS84,
                new TransverseMercator(Ellipsoid.WGS84, 0, 55.0 + 20.0 / 60.0, 1.0, 500000, 0)));
            l.Add(new Crs("EPSG:32640", "WGS 84 / UTM 40N (Abu Dhabi, Dubai, North UAE)", "WGS 84 / UTM 40 شمال (أبوظبي - دبي - الإمارات الشمالية)", "UAE", Datum.WGS84, Utm(Ellipsoid.WGS84, 40)));
            l.Add(new Crs("EPSG:32639", "WGS 84 / UTM 39N (Western Region / Al Dhafra)", "WGS 84 / UTM 39 شمال (المنطقة الغربية - الظفرة)", "UAE", Datum.WGS84, Utm(Ellipsoid.WGS84, 39)));
            l.Add(new Crs("EPSG:27040", "Nahrwan 1967 / UTM 40N (UAE)", "نهروان 1967 / UTM 40 شمال (الإمارات)", "UAE", Datum.Nahrwan1967UAE, Utm(Ellipsoid.Clarke1880RGS, 40)));
            l.Add(new Crs("EPSG:27040-AD", "Nahrwan 1967 / UTM 40N (Abu Dhabi onshore, 7-param)", "نهروان 1967 / UTM 40 شمال (أبوظبي البري - 7 بارامتر)", "UAE", Datum.Nahrwan1967AbuDhabi, Utm(Ellipsoid.Clarke1880RGS, 40)));
            l.Add(new Crs("EPSG:27039", "Nahrwan 1967 / UTM 39N (UAE)", "نهروان 1967 / UTM 39 شمال (الإمارات)", "UAE", Datum.Nahrwan1967UAE, Utm(Ellipsoid.Clarke1880RGS, 39)));

            // Egypt
            l.Add(new Crs("EPSG:22992", "Egypt 1907 / Red Belt", "مصر 1907 / الحزام الأحمر", "Egypt", Datum.Egypt1907,
                new TransverseMercator(Ellipsoid.Helmert1906, 30, 31, 1, 615000, 810000)));
            l.Add(new Crs("EPSG:22993", "Egypt 1907 / Purple Belt", "مصر 1907 / الحزام البنفسجي", "Egypt", Datum.Egypt1907,
                new TransverseMercator(Ellipsoid.Helmert1906, 30, 27, 1, 700000, 200000)));
            l.Add(new Crs("EPSG:22994", "Egypt 1907 / Extended Purple Belt", "مصر 1907 / البنفسجي الممتد", "Egypt", Datum.Egypt1907,
                new TransverseMercator(Ellipsoid.Helmert1906, 30, 27, 1, 700000, 1200000)));
            l.Add(new Crs("EPSG:22991", "Egypt 1907 / Blue Belt", "مصر 1907 / الحزام الأزرق", "Egypt", Datum.Egypt1907,
                new TransverseMercator(Ellipsoid.Helmert1906, 30, 35, 1, 300000, 1100000)));
            l.Add(new Crs("EPSG:32635", "WGS 84 / UTM 35N (West Egypt)", "WGS 84 / UTM 35 شمال (غرب مصر)", "Egypt", Datum.WGS84, Utm(Ellipsoid.WGS84, 35)));
            l.Add(new Crs("EPSG:32636", "WGS 84 / UTM 36N (Egypt, Sinai)", "WGS 84 / UTM 36 شمال (مصر - سيناء)", "Egypt", Datum.WGS84, Utm(Ellipsoid.WGS84, 36)));

            // Saudi / Gulf
            l.Add(new Crs("EPSG:32637", "WGS 84 / UTM 37N (West Saudi)", "WGS 84 / UTM 37 شمال (غرب السعودية)", "Gulf", Datum.WGS84, Utm(Ellipsoid.WGS84, 37)));
            l.Add(new Crs("EPSG:32638", "WGS 84 / UTM 38N (Central Saudi, Kuwait)", "WGS 84 / UTM 38 شمال (وسط السعودية - الكويت)", "Gulf", Datum.WGS84, Utm(Ellipsoid.WGS84, 38)));
            return l;
        }
    }

    /// <summary>Converts coordinates between two CRS. Heights are passed through unchanged
    /// (surveyors' Z values are orthometric levels, not ellipsoidal heights).</summary>
    public sealed class CoordinateTransformer
    {
        public Crs Source { get; }
        public Crs Target { get; }
        private readonly bool _sameDatum;

        public CoordinateTransformer(Crs source, Crs target)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            _sameDatum = ReferenceEquals(source.Datum, target.Datum);
        }

        /// <summary>x = Easting (or Longitude°), y = Northing (or Latitude°).</summary>
        public void Transform(double x, double y, out double xOut, out double yOut)
        {
            double lat, lon;
            if (Source.Kind == CrsKind.Geographic) { lat = y * Math.PI / 180; lon = x * Math.PI / 180; }
            else Source.Projection.Inverse(x, y, out lat, out lon);

            if (!_sameDatum)
            {
                Geocentric.FromGeodetic(Source.Datum.Ellipsoid, lat, lon, 0, out double X, out double Y, out double Z);
                Source.Datum.ToWgs84.ToWgs84(ref X, ref Y, ref Z);
                Target.Datum.ToWgs84.FromWgs84(ref X, ref Y, ref Z);
                Geocentric.ToGeodetic(Target.Datum.Ellipsoid, X, Y, Z, out lat, out lon, out _);
            }

            if (Target.Kind == CrsKind.Geographic) { yOut = lat * 180 / Math.PI; xOut = lon * 180 / Math.PI; }
            else Target.Projection.Forward(lat, lon, out xOut, out yOut);
        }

        public bool IsIdentity => ReferenceEquals(Source, Target);
    }
}
