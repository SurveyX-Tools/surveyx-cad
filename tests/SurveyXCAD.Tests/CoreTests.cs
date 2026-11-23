using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurveyX.Core;
using Xunit;

namespace SurveyX.Tests
{
    public class GeodesyTests
    {
        // Reference values generated with PROJ (pyproj 3.8) using the same EPSG definitions / datum shifts.
        public static IEnumerable<object[]> Vectors => new List<object[]>
        {
            new object[] { "EPSG:3997", 54.3773, 24.4539, 403063.1690, 2705898.1122 },
            new object[] { "EPSG:3997", 55.2708, 25.2048, 493697.7578, 2788742.2337 },
            new object[] { "EPSG:3997", 53.6, 24.1, 323748.7654, 2667453.3850 },
            new object[] { "EPSG:32640", 54.3773, 24.4539, 234124.8809, 2707001.2912 },
            new object[] { "EPSG:32640", 55.2708, 25.2048, 325780.3087, 2788745.0708 },
            new object[] { "EPSG:32640", 53.6, 24.1, 154313.9357, 2669489.4511 },
            new object[] { "EPSG:32639", 54.3773, 24.4539, 842424.4785, 2708662.0738 },
            new object[] { "EPSG:32639", 55.2708, 25.2048, 930503.8613, 2794466.4793 },
            new object[] { "EPSG:32639", 53.6, 24.1, 764304.9262, 2667747.8495 },
            new object[] { "EPSG:27040", 54.3773, 24.4539, 234002.4082, 2706583.3108 },
            new object[] { "EPSG:27040", 55.2708, 25.2048, 325657.3076, 2788326.7880 },
            new object[] { "EPSG:27040", 53.6, 24.1, 154191.9241, 2669071.5452 },
            new object[] { "EPSG:27039", 54.3773, 24.4539, 842326.8968, 2708239.2065 },
            new object[] { "EPSG:27039", 55.2708, 25.2048, 930405.8774, 2794042.8965 },
            new object[] { "EPSG:27039", 53.6, 24.1, 764207.8097, 2667325.3762 },
            new object[] { "EPSG:27040-AD", 54.3773, 24.4539, 234002.8639, 2706583.5602 },
            new object[] { "EPSG:27040-AD", 55.2708, 25.2048, 325657.9852, 2788326.9500 },
            new object[] { "EPSG:27040-AD", 53.6, 24.1, 154192.2465, 2669071.9016 },
            new object[] { "EPSG:22992", 31.2357, 30.0444, 637570.5093, 814926.6831 },
            new object[] { "EPSG:22992", 29.9187, 31.2001, 511783.2431, 943531.6483 },
            new object[] { "EPSG:22992", 31.18, 27.18, 632676.1447, 497455.5257 },
            new object[] { "EPSG:22992", 32.55, 29.97, 764446.8905, 807666.8855 },
            new object[] { "EPSG:22993", 31.2357, 30.0444, 1108534.7652, 212469.8358 },
            new object[] { "EPSG:22993", 29.9187, 31.2001, 978063.6910, 336695.0172 },
            new object[] { "EPSG:22993", 31.18, 27.18, 1114281.3957, -105648.8390 },
            new object[] { "EPSG:22993", 32.55, 29.97, 1235925.4022, 209646.1210 },
            new object[] { "EPSG:22994", 31.2357, 30.0444, 1108534.7652, 1212469.8358 },
            new object[] { "EPSG:22994", 29.9187, 31.2001, 978063.6910, 1336695.0172 },
            new object[] { "EPSG:22994", 31.18, 27.18, 1114281.3957, 894351.1610 },
            new object[] { "EPSG:22994", 32.55, 29.97, 1235925.4022, 1209646.1210 },
            new object[] { "EPSG:22991", 31.2357, 30.0444, -63338.4986, 1110887.4651 },
            new object[] { "EPSG:22991", 29.9187, 31.2001, -184731.8927, 1244182.7962 },
            new object[] { "EPSG:22991", 31.18, 27.18, -78878.5360, 793220.3100 },
            new object[] { "EPSG:22991", 32.55, 29.97, 63335.7789, 1099188.4005 },
            new object[] { "EPSG:32636", 31.2357, 30.0444, 329899.6430, 3325016.7362 },
            new object[] { "EPSG:32636", 29.9187, 31.2001, 206390.4096, 3455870.6687 },
            new object[] { "EPSG:32636", 31.18, 27.18, 319698.9592, 3007680.5114 },
            new object[] { "EPSG:32636", 32.55, 29.97, 456585.2678, 3315546.2855 },
        };

        [Theory]
        [MemberData(nameof(Vectors))]
        public void Wgs84ToProjected_MatchesProj(string crsId, double lon, double lat, double e, double n)
        {
            var t = new CoordinateTransformer(CrsCatalog.Wgs84Geo, CrsCatalog.Find(crsId));
            t.Transform(lon, lat, out double x, out double y);
            Assert.InRange(Math.Abs(x - e), 0, 0.002);
            Assert.InRange(Math.Abs(y - n), 0, 0.002);
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void ProjectedToWgs84_RoundTrips(string crsId, double lon, double lat, double e, double n)
        {
            var t = new CoordinateTransformer(CrsCatalog.Find(crsId), CrsCatalog.Wgs84Geo);
            t.Transform(e, n, out double lo, out double la);
            Assert.InRange(Math.Abs(lo - lon) * 111000, 0, 0.01);
            Assert.InRange(Math.Abs(la - lat) * 111000, 0, 0.01);
        }

        [Fact]
        public void AllCatalogIdsUnique() =>
            Assert.Equal(CrsCatalog.All.Count, CrsCatalog.All.Select(c => c.Id).Distinct().Count());

        [Theory]
        [InlineData("25.2048", 25.2048)]
        [InlineData("25°12'17.28\"", 25.2048)]
        [InlineData("25:12:17.28", 25.2048)]
        [InlineData("25 12 17.28 N", 25.2048)]
        [InlineData("55d16m14.88sE", 55.2708)]
        [InlineData("-31.5", -31.5)]
        [InlineData("31°30'00\" W", -31.5)]
        public void ParsesAngles(string text, double expected)
        {
            Assert.True(AngleFormat.TryParse(text, out double v));
            Assert.InRange(Math.Abs(v - expected), 0, 1e-6);
        }

        [Fact]
        public void DmsRoundTrip()
        {
            string s = AngleFormat.ToDms(25.2048, true);
            Assert.True(AngleFormat.TryParse(s, out double v));
            Assert.InRange(Math.Abs(v - 25.2048), 0, 1e-7);
        }
    }

    public class ReaderTests
    {
        private static string Temp(string ext, string content)
        {
            string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void ReadsPnezdCsvWithoutHeader()
        {
            var r = PointFileReader.Read(Temp(".csv", "1,2705898.112,234124.881,10.25,TBM\n2,2705900.0,234130.0,10.30,EG\n3,2705910.5,234140.2,10.10,EG\n"));
            Assert.Equal(3, r.Points.Count);
            Assert.Equal("1", r.Points[0].Name);
            Assert.Equal(2705898.112, r.Points[0].N, 3);
            Assert.Equal(234124.881, r.Points[0].E, 3);
            Assert.Equal(10.25, r.Points[0].Z, 3);
            Assert.Equal("TBM", r.Points[0].Desc);
        }

        [Fact]
        public void GuessesEastingFirstFromMagnitudes()
        {
            var r = PointFileReader.Read(Temp(".txt", "P1 234124.881 2705898.112 10.25\nP2 234130.0 2705900.0 10.30\nP3 234140.0 2705905.0 10.40\n"));
            Assert.Equal(3, r.Points.Count);
            Assert.Equal(2705898.112, r.Points[0].N, 3);
            Assert.Equal(234124.881, r.Points[0].E, 3);
        }

        [Fact]
        public void MapsColumnsFromHeader_ControllerStyle()
        {
            var r = PointFileReader.Read(Temp(".csv", "Name,Code,E,N,H\nCP1,CTRL,234124.881,2705898.112,10.25\nCP2,CTRL,234130,2705900,10.3\n"));
            Assert.Equal(2, r.Points.Count);
            Assert.Equal("CP1", r.Points[0].Name);
            Assert.Equal("CTRL", r.Points[0].Desc);
            Assert.Equal(2705898.112, r.Points[0].N, 3);
            Assert.Equal(10.25, r.Points[0].Z, 3);
        }

        [Fact]
        public void MapsArabicHeader()
        {
            var r = PointFileReader.Read(Temp(".csv", "رقم,شرقي,شمالي,منسوب\n1,234124.881,2705898.112,10.25\n"));
            Assert.Single(r.Points);
            Assert.Equal(234124.881, r.Points[0].E, 3);
        }

        [Fact]
        public void ReadsLandXml()
        {
            var r = PointFileReader.Read(Temp(".xml", "<LandXML><CgPoints><CgPoint name=\"A\" code=\"X\">2705898.1 234124.8 10.2</CgPoint></CgPoints></LandXML>"));
            Assert.Single(r.Points);
            Assert.Equal(2705898.1, r.Points[0].N, 3);
            Assert.Equal("X", r.Points[0].Desc);
        }

        [Fact]
        public void ReadsGeoJson()
        {
            var r = PointFileReader.Read(Temp(".geojson", "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[54.3773,24.4539,5]},\"properties\":{\"name\":\"P1\"}}]}"));
            Assert.True(r.IsGeographic);
            Assert.Single(r.Points);
            Assert.Equal(54.3773, r.Points[0].E, 6);
            Assert.Equal("P1", r.Points[0].Name);
        }

        [Fact]
        public void WriteThenReadAllFormats()
        {
            var pts = new List<SurveyPoint>
            {
                new SurveyPoint { Name = "1", E = 234124.881, N = 2705898.112, Z = 10.25, Desc = "TBM" },
                new SurveyPoint { Name = "2", E = 234130.000, N = 2705900.000, Z = 10.30, Desc = "EG, edge" },
            };
            var toWgs = new CoordinateTransformer(CrsCatalog.Find("EPSG:32640"), CrsCatalog.Wgs84Geo);
            foreach (var fmt in new[] { ExportFormat.Csv, ExportFormat.Txt, ExportFormat.LandXml, ExportFormat.Kml, ExportFormat.GeoJson })
            {
                string ext = fmt == ExportFormat.LandXml ? ".xml" : fmt == ExportFormat.Kml ? ".kml" : fmt == ExportFormat.GeoJson ? ".geojson" : fmt == ExportFormat.Txt ? ".txt" : ".csv";
                string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
                PointWriters.Write(p, fmt, pts, "PNEZD", 3, toWgs);
                var r = PointFileReader.Read(p);
                Assert.Equal(2, r.Points.Count);
                if (r.IsGeographic)
                {
                    var back = new CoordinateTransformer(CrsCatalog.Wgs84Geo, CrsCatalog.Find("EPSG:32640"));
                    back.Transform(r.Points[0].E, r.Points[0].N, out double e, out double n);
                    Assert.InRange(Math.Abs(e - 234124.881), 0, 0.01);
                    Assert.InRange(Math.Abs(n - 2705898.112), 0, 0.01);
                }
                else
                {
                    Assert.Equal(234124.881, r.Points[0].E, 3);
                    Assert.Equal(2705898.112, r.Points[0].N, 3);
                }
                if (fmt != ExportFormat.Kml) Assert.Equal("EG, edge", r.Points[1].Desc);
                else Assert.EndsWith("EG, edge", r.Points[1].Desc);
            }
        }
    }

    public class AnalysisTests
    {
        [Fact]
        public void FlagsDuplicatesAndOutliers()
        {
            var pts = new List<SurveyPoint>();
            for (int i = 0; i < 20; i++) pts.Add(new SurveyPoint { Name = (i + 1).ToString(), E = 1000 + i, N = 2000 + i, Z = 10 + 0.01 * i });
            pts.Add(new SurveyPoint { Name = "5", E = 5000, N = 6000, Z = 10 });      // dup name + far away
            pts.Add(new SurveyPoint { Name = "X", E = 1003, N = 2003, Z = 10.03 });   // dup coords of point 4
            pts.Add(new SurveyPoint { Name = "Z", E = 1010.5, N = 2010.5, Z = 99 });  // abnormal elevation
            var s = PointAnalysis.Analyze(pts);
            Assert.Equal(2, s.DuplicateNames);
            Assert.Equal(2, s.DuplicateCoords);
            Assert.Equal(1, s.OutliersXY);
            Assert.Equal(1, s.OutliersZ);
        }
    }
}
