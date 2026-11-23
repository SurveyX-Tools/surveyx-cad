using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurveyX.Core;
using Xunit;

namespace SurveyX.Tests
{
    public class EarthworksTests
    {
        private static List<SurveyPoint> GridPts(double x0, double y0, double size, double step, Func<double, double, double> z, string prefix = "P")
        {
            var l = new List<SurveyPoint>();
            int n = (int)Math.Round(size / step);
            int k = 1;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    double x = x0 + i * step, y = y0 + j * step;
                    l.Add(new SurveyPoint { Name = prefix + (k++), E = x, N = y, Z = z(x, y) });
                }
            return l;
        }

        [Fact]
        public void Delaunay_GridHasExpectedTriangles()
        {
            var pts = GridPts(0, 0, 19, 1, (x, y) => 0);
            var tin = new Tin(pts);
            Assert.Equal(2 * 19 * 19, tin.TriangleCount);
            Assert.InRange(Math.Abs(tin.Area() - 361), 0, 1e-6);
        }

        [Fact]
        public void Delaunay_RandomCoversConvexArea_LargeUtmCoordinates()
        {
            var rnd = new Random(7);
            var pts = new List<SurveyPoint>();
            for (int i = 0; i < 20000; i++) pts.Add(new SurveyPoint { E = 234000 + rnd.NextDouble() * 500, N = 2705000 + rnd.NextDouble() * 500, Z = rnd.NextDouble() });
            // add exact square corners so the hull is the square
            pts.Add(new SurveyPoint { E = 233999, N = 2704999 }); pts.Add(new SurveyPoint { E = 234501, N = 2704999 });
            pts.Add(new SurveyPoint { E = 234501, N = 2705501 }); pts.Add(new SurveyPoint { E = 233999, N = 2705501 });
            var tin = new Tin(pts);
            Assert.InRange(Math.Abs(tin.Area() - 502 * 502), 0, 1e-3);
            Assert.InRange(tin.TriangleCount, 2 * pts.Count - 4 - 2 - 50, 2 * pts.Count - 4 - 2); // 2n − h − 2 (h ≥ 4)
        }

        [Fact]
        public void Tin_InterpolatesPlaneExactly()
        {
            var tin = new Tin(GridPts(1000, 2000, 50, 5, (x, y) => 0.02 * (x - 1000) + 0.03 * (y - 2000) + 10));
            Assert.InRange(Math.Abs(tin.ZAt(1012.3, 2033.3) - (0.02 * 12.3 + 0.03 * 33.3 + 10)), 0, 1e-9);
            Assert.True(double.IsNaN(tin.ZAt(900, 2000)));
        }

        [Fact]
        public void CutFill_FlatOneMetreFill()
        {
            var ogl = new Tin(GridPts(0, 0, 100, 5, (x, y) => 5));
            var newPts = GridPts(0, 0, 100, 10, (x, y) => 6, "N");
            var r = CutFill.Compute(ogl, new Tin(newPts), newPts, null, 1.0);
            Assert.InRange(Math.Abs(r.FillVolume - 10000), 0, 1e-6);
            Assert.InRange(r.CutVolume, 0, 1e-9);
            Assert.InRange(Math.Abs(r.TotalArea - 10000), 0, 1e-6);
            Assert.All(r.Points, p => Assert.InRange(Math.Abs(p.Diff - 1), 0, 1e-9));
        }

        [Fact]
        public void CutFill_BoundaryAndCut()
        {
            var ogl = new Tin(GridPts(0, 0, 100, 5, (x, y) => 5));
            var newPts = GridPts(0, 0, 100, 10, (x, y) => 4.5, "N");
            var b = new List<(double, double)> { (0, 0), (50, 0), (50, 50), (0, 50) };
            var r = CutFill.Compute(ogl, new Tin(newPts), newPts, b, 0.5);
            Assert.InRange(Math.Abs(r.CutVolume - 2500 * 0.5), 0, 1e-6);
            Assert.InRange(Math.Abs(r.MaxCut - 0.5), 0, 1e-6);
            Assert.All(r.Points, p => Assert.True(p.Point.E <= 50 && p.Point.N <= 50));
        }

        [Fact]
        public void CutFill_SlopedSurfacesMatchAnalyticVolume()
        {
            // OGL z = 0, new z = x/100 over 0..100 → volume = ∫∫ x/100 = 100 * 100²/200 = 5000
            var ogl = new Tin(GridPts(0, 0, 100, 10, (x, y) => 0));
            var np = GridPts(0, 0, 100, 10, (x, y) => x / 100, "N");
            var r = CutFill.Compute(ogl, new Tin(np), np, null, 0.5);
            Assert.InRange(Math.Abs(r.FillVolume - 5000), 0, 1);
        }

        [Fact]
        public void LevelCheck_ByNameAndTolerance()
        {
            var asb = new List<SurveyPoint>
            {
                new SurveyPoint { Name = "K1", Z = 10.008 }, new SurveyPoint { Name = "K2", Z = 9.985 }, new SurveyPoint { Name = "K3", Z = 10.0 },
            };
            var des = new List<SurveyPoint> { new SurveyPoint { Name = "k1", Z = 10.0 }, new SurveyPoint { Name = "K2", Z = 10.0 } };
            var r = LevelCheck.AgainstNames(asb, des, 0.010, 0.010);
            Assert.Equal(CheckStatus.Pass, r.Rows[0].Status);
            Assert.Equal(CheckStatus.Fail, r.Rows[1].Status);
            Assert.Equal(CheckStatus.NoDesign, r.Rows[2].Status);
            Assert.Equal(50.0, r.PassPercent, 6);
        }

        [Fact]
        public void LevelCheck_AgainstSurfaceAndNearest()
        {
            var design = GridPts(0, 0, 20, 1, (x, y) => 100 + 0.01 * x);
            var asb = new List<SurveyPoint> { new SurveyPoint { Name = "A", E = 10.5, N = 5.5, Z = 100.105 + 0.02 } };
            var r = LevelCheck.AgainstSurface(asb, new Tin(design), 0.015, 0.015);
            Assert.InRange(Math.Abs(r.Rows[0].Diff - 0.02), 0, 1e-9);
            Assert.Equal(CheckStatus.Fail, r.Rows[0].Status);
            var n = LevelCheck.AgainstNearest(asb, design, 1.0, 0.05, 0.05);
            Assert.Equal(CheckStatus.Pass, n.Rows[0].Status);
        }

        [Fact]
        public void Sections_EndAreaVolume()
        {
            var exist = new Tin(GridPts(-20, -20, 140, 5, (x, y) => 0));
            var design = new Tin(GridPts(-20, -20, 140, 5, (x, y) => 1, "D"));
            var align = new List<(double, double)> { (0, 0), (100, 0) };
            var secs = Sections.CrossSections(align, new List<Tin> { exist, design }, 10, 5, 5, 0.5);
            Assert.Equal(11, secs.Count);
            Assert.All(secs, s => Assert.InRange(Math.Abs(s.FillArea - 10), 0, 1e-6));
            var v = Sections.EndArea(secs);
            Assert.InRange(Math.Abs(v.TotalFill - 1000), 0, 1e-6);
            var ls = Sections.LongSection(align, new List<Tin> { exist, design }, 25);
            Assert.Equal(5, ls.Count);
            Assert.All(ls, r => Assert.InRange(Math.Abs(r.z[1] - 1), 0, 1e-9));
        }

        [Fact]
        public void Contours_OnPlane()
        {
            var tin = new Tin(GridPts(0, 0, 10, 1, (x, y) => x));
            var c = tin.Contours(1.0);
            var five = c.Where(l => Math.Abs(l.level - 5) < 1e-9).ToList();
            Assert.Single(five);
            Assert.All(five[0].pts, p => Assert.InRange(Math.Abs(p.x - 5), 0, 1e-6));
            Assert.InRange(five[0].pts.Max(p => p.y) - five[0].pts.Min(p => p.y), 9.999, 10.001);
        }

        [Fact]
        public void Chainage_Format() => Assert.Equal("1+234.500", Geo2D.FormatChainage(234.5, 1000));
    }

    public class PrimaveraTests
    {
        private const string Xer =
            "ERMHDR\t21.12\t2026-11-20\tProject\tadmin\n" +
            "%T\tPROJECT\n%F\tproj_id\tproj_short_name\tlast_recalc_date\n%R\t1\tASB-RAWDA\t2026-11-20 08:00\n" +
            "%T\tPROJWBS\n%F\twbs_id\tproj_id\twbs_name\n%R\t10\t1\tEarthworks\n" +
            "%T\tTASK\n%F\ttask_id\tproj_id\twbs_id\ttask_code\ttask_name\tstatus_code\tphys_complete_pct\ttarget_start_date\ttarget_end_date\tact_start_date\tact_end_date\n" +
            "%R\t100\t1\t10\tA1000\tExcavation Zone 1\tTK_Active\t40\t2026-11-01 08:00\t2026-11-30 17:00\t2026-11-02 08:00\t\n" +
            "%R\t101\t1\t10\tA1010\tFill Zone 2\tTK_NotStart\t0\t2026-11-10 08:00\t2026-12-15 17:00\t\t\n" +
            "%R\t102\t1\t10\tA1020\tSurvey OGL\tTK_Complete\t0\t2026-10-01 08:00\t2026-10-05 17:00\t2026-10-01 08:00\t2026-10-04 17:00\n" +
            "%E\n";

        [Fact]
        public void ParsesXer()
        {
            var s = PrimaveraReader.ParseXer(Xer);
            Assert.Equal("ASB-RAWDA", s.ProjectName);
            Assert.Equal(new DateTime(2026, 11, 20, 8, 0, 0), s.DataDate);
            Assert.Equal(3, s.Activities.Count);
            var a = s.Find("a1000");
            Assert.Equal("Excavation Zone 1", a.Name);
            Assert.Equal("Earthworks", a.Wbs);
            Assert.Equal(40, a.P6Percent);
            Assert.Equal(new DateTime(2026, 11, 2, 8, 0, 0), a.Start);
            Assert.Equal(100, s.Find("A1020").P6Percent);
            Assert.Equal(ActivityState.InProgress, a.State(s.DataDate));
            Assert.Equal(ActivityState.Late, s.Find("A1010").State(s.DataDate)); // should have started on 10-Nov
            Assert.Equal(ActivityState.Completed, s.Find("A1020").State(s.DataDate));
        }

        [Fact]
        public void ParsesP6Xml()
        {
            string xml = "<APIBusinessObjects xmlns=\"http://xmlns.oracle.com/Primavera/P6/V21.12/API/BusinessObjects\"><Project><Id>P1</Id><DataDate>2026-11-20T08:00:00</DataDate>" +
                         "<WBS><ObjectId>5</ObjectId><Name>Roads</Name></WBS>" +
                         "<Activity><Id>R100</Id><Name>Kerb KP8</Name><WBSObjectId>5</WBSObjectId><PlannedStartDate>2026-11-15T07:00:00</PlannedStartDate>" +
                         "<PlannedFinishDate>2026-11-25T17:00:00</PlannedFinishDate><PhysicalPercentComplete>25</PhysicalPercentComplete><Status>In Progress</Status></Activity>" +
                         "</Project></APIBusinessObjects>";
            string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(p, xml);
            var s = PrimaveraReader.Read(p);
            Assert.Equal("P1", s.ProjectName);
            Assert.Single(s.Activities);
            Assert.Equal("Roads", s.Activities[0].Wbs);
            Assert.Equal(25, s.Activities[0].P6Percent);
        }

        [Fact]
        public void ScheduleRoundTripKeepsQuantities()
        {
            var s = PrimaveraReader.ParseXer(Xer);
            s.Find("A1000").PlannedQty = 5000; s.Find("A1000").ExecutedQty = 1250.5; s.Find("A1000").Unit = "m3";
            var back = P6Schedule.Deserialize(s.Serialize());
            Assert.Equal(3, back.Activities.Count);
            Assert.Equal(5000, back.Find("A1000").PlannedQty);
            Assert.Equal(1250.5, back.Find("A1000").ExecutedQty);
            back.Find("A1000").UpdateDrawingPercent(0, false);
            Assert.InRange(Math.Abs(back.Find("A1000").DrawingPercent - 25.01), 0, 1e-9);
            var reimport = PrimaveraReader.ParseXer(Xer);
            reimport.CarryQuantitiesFrom(back);
            Assert.Equal(5000, reimport.Find("A1000").PlannedQty);
        }

        [Fact]
        public void WeightedProgress()
        {
            double p = ProgressCalc.WeightedPercent(new[] { (300.0, 100.0), (100.0, 0.0) });
            Assert.Equal(75, p, 9);
        }

        [Fact]
        public void P6ExcelIsReadable()
        {
            var acts = new List<P6Activity> { new P6Activity { Id = "A1000", P6Percent = 10, PlannedQty = 100, ExecutedQty = 40 } };
            acts[0].UpdateDrawingPercent(0, false);
            string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
            PrimaveraWriter.WriteP6Excel(p, acts);
            var rows = XlsxReader.ReadFirstSheet(p);
            Assert.Equal("task_code", rows[0][0]);
            Assert.Equal("phys_complete_pct", rows[0][1]);
            Assert.Equal("A1000", rows[2][0]);
            Assert.Equal("40", rows[2][1]);
        }
    }

    public class PdfTests
    {
        [Fact]
        public void WritesValidPdfStructure()
        {
            // 1×1 white JPEG
            byte[] jpg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==");
            var ms = new MemoryStream();
            PdfWriter.Write(ms, new List<PdfWriter.Page> { new PdfWriter.Page { Jpeg = jpg, PixelWidth = 1, PixelHeight = 1 }, new PdfWriter.Page { Jpeg = jpg, PixelWidth = 1, PixelHeight = 1 } }, "تقرير", "Ahmed");
            string text = System.Text.Encoding.ASCII.GetString(ms.ToArray());
            Assert.StartsWith("%PDF-1.4", text);
            Assert.Contains("/Count 2", text);
            Assert.Contains("/Filter /DCTDecode", text);
            Assert.EndsWith("%%EOF\n", text);
            // xref offsets must point at "n 0 obj"
            int xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1; // not the "startxref" line
            var lines = text.Substring(xref).Split('\n');
            int count = int.Parse(lines[1].Split(' ')[1]);
            for (int i = 1; i < count; i++)
            {
                long off = long.Parse(lines[2 + i].Substring(0, 10));
                Assert.StartsWith(i + " 0 obj", text.Substring((int)off));
            }
        }
    }
}
