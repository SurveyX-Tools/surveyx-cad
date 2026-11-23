using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SurveyX.Core
{
    /// <summary>Excel (.xlsx) exports for every report. Numbers are written as real numbers so Excel can sum them.</summary>
    public static class ExcelExport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(double v, int d = 3) => double.IsNaN(v) ? "" : v.ToString("F" + d, Inv);
        private static void Save(string path, string sheet, List<string[]> rows) => XlsxWriter.Write(path, sheet, rows, 0, null);

        public static void CutFill(string path, CutFillResult r, string area, DateTime date, string ogl, string survey)
        {
            var rows = new List<string[]>
            {
                new[] { "Daily Cut & Fill", area ?? "" },
                new[] { "Project", Settings.Current.ProjectName },
                new[] { "Date", date.ToString("yyyy-MM-dd", Inv) },
                new[] { "OGL", ogl }, new[] { "Survey", survey },
                new[] { "Cut volume (m3)", F(r.CutVolume, 2) }, new[] { "Fill volume (m3)", F(r.FillVolume, 2) },
                new[] { "Net fill - cut (m3)", F(r.NetVolume, 2) }, new[] { "Area (m2)", F(r.TotalArea, 2) },
                new[] { "Cut area (m2)", F(r.CutArea, 2) }, new[] { "Fill area (m2)", F(r.FillArea, 2) },
                new[] { "Max cut (m)", F(r.MaxCut) }, new[] { "Max fill (m)", F(r.MaxFill) }, new[] { "Grid cell (m)", F(r.Cell) },
                new string[0],
                new[] { "Point", "Easting", "Northing", "New Z", "OGL Z", "Difference (+fill/-cut)", "Description" },
            };
            rows.AddRange(r.Points.Select(p => new[] { p.Point.Name, F(p.Point.E), F(p.Point.N), F(p.Point.Z), F(p.RefZ), F(p.Diff), p.Point.Desc ?? "" }));
            Save(path, "CutFill", rows);
        }

        public static void LevelCheck(string path, LevelCheckResult r, string element)
        {
            var rows = new List<string[]>
            {
                new[] { "Level Check", element ?? "" },
                new[] { "Project", Settings.Current.ProjectName },
                new[] { "Tolerance + (m)", F(r.TolPlus) }, new[] { "Tolerance - (m)", F(r.TolMinus) },
                new[] { "Checked", (r.Pass + r.Fail).ToString(Inv) }, new[] { "Pass", r.Pass.ToString(Inv) }, new[] { "Fail", r.Fail.ToString(Inv) },
                new[] { "Pass rate (%)", F(r.PassPercent, 1) },
                new string[0],
                new[] { "Point", "Easting", "Northing", "As-built Z", "Design Z", "Difference", "Result", "Description" },
            };
            rows.AddRange(r.Rows.Select(x => new[] { x.Point.Name, F(x.Point.E), F(x.Point.N), F(x.Point.Z), F(x.DesignZ), F(x.Diff), x.Status.ToString(), x.Point.Desc ?? "" }));
            Save(path, "LevelCheck", rows);
        }

        public static void Qto(string path, IList<QtoRow> q)
        {
            var rows = new List<string[]>
            {
                new[] { "Quantity Take-off", Settings.Current.ProjectName, DateTime.Today.ToString("yyyy-MM-dd", Inv) },
                new string[0],
                new[] { "Layer", "Length (m)", "Area (m2)", "Lines + polylines", "Closed areas", "Blocks", "Texts", "Points", "Other" },
            };
            rows.AddRange(q.Select(r => new[] { r.Layer, F(r.Length, 2), F(r.Area, 2), (r.Lines + r.Polylines).ToString(Inv), r.ClosedAreas.ToString(Inv),
                r.Blocks.ToString(Inv), r.Texts.ToString(Inv), r.Points.ToString(Inv), r.Other.ToString(Inv) }));
            rows.Add(new[] { "TOTAL", F(q.Sum(r => r.Length), 2), F(q.Sum(r => r.Area), 2), q.Sum(r => r.Lines + r.Polylines).ToString(Inv),
                q.Sum(r => r.ClosedAreas).ToString(Inv), q.Sum(r => r.Blocks).ToString(Inv), q.Sum(r => r.Texts).ToString(Inv), q.Sum(r => r.Points).ToString(Inv), q.Sum(r => r.Other).ToString(Inv) });
            var blocks = q.SelectMany(r => r.BlockNames.Select(b => new[] { r.Layer, b.Key, b.Value.ToString(Inv) })).ToList();
            if (blocks.Count > 0)
            {
                rows.Add(new string[0]);
                rows.Add(new[] { "Layer", "Block name", "Count" });
                rows.AddRange(blocks);
            }
            Save(path, "QTO", rows);
        }

        public static void SetOut(string path, IList<(SurveyPoint p, double station, double offset)> pts, double startCh)
        {
            var rows = new List<string[]> { new[] { "Point", "Chainage", "Station (m)", "Offset (m, +R / -L)", "Easting", "Northing", "Elevation", "Description" } };
            rows.AddRange(pts.Select(x => new[] { x.p.Name, Geo2D.FormatChainage(x.station, startCh), F(x.station + startCh), F(x.offset), F(x.p.E), F(x.p.N), x.p.HasZ ? F(x.p.Z) : "", x.p.Desc ?? "" }));
            Save(path, "SettingOut", rows);
        }

        public static void Cumulative(string path, IList<CutFillLog.Entry> e, string area)
        {
            var rows = new List<string[]>
            {
                new[] { "Cumulative Cut & Fill", string.IsNullOrWhiteSpace(area) ? "All areas" : area },
                new[] { "Project", Settings.Current.ProjectName },
                new string[0],
                new[] { "Date", "Area", "Cut (m3)", "Fill (m3)", "Cumulative cut (m3)", "Cumulative fill (m3)", "Surveyed area (m2)", "Survey file" },
            };
            double cc = 0, cf = 0;
            foreach (var x in e)
            {
                cc += x.Cut; cf += x.Fill;
                rows.Add(new[] { x.Date.ToString("yyyy-MM-dd", Inv), x.Area, F(x.Cut, 2), F(x.Fill, 2), F(cc, 2), F(cf, 2), F(x.AreaM2, 2), x.Survey });
            }
            rows.Add(new[] { "TOTAL", "", F(cc, 2), F(cf, 2) });
            Save(path, "Cumulative", rows);
        }

        public static void SectionVolumes(string path, SectionVolumes v, double startCh)
        {
            var rows = new List<string[]> { new[] { "Chainage", "Cut area (m2)", "Fill area (m2)", "Cut volume (m3)", "Fill volume (m3)", "Cumulative cut (m3)", "Cumulative fill (m3)" } };
            double cc = 0, cf = 0;
            foreach (var r in v.Rows)
            {
                cc += r.cutVol; cf += r.fillVol;
                rows.Add(new[] { Geo2D.FormatChainage(r.ch, startCh), F(r.cutArea, 2), F(r.fillArea, 2), F(r.cutVol, 2), F(r.fillVol, 2), F(cc, 2), F(cf, 2) });
            }
            Save(path, "Sections", rows);
        }
    }
}
