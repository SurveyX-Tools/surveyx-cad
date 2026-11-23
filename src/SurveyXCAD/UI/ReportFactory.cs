using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>Builds the report documents (shared by the windows and the automated preview tests).</summary>
    public static class ReportFactory
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ReportDoc CutFill(CutFillResult r, IList<(double x, double y)> boundary, string area, DateTime date, string oglName, string newName, int newCount)
        {
            var st = Settings.Current;
            var d = new ReportDoc { Title = Lang.T("CutFillReportTitle") + (string.IsNullOrWhiteSpace(area) ? "" : " — " + area) };
            d.Info.Add((Lang.T("Project"), st.ProjectName));
            d.Info.Add((Lang.T("Date"), date.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("OglSourceShort"), oglName));
            d.Info.Add((Lang.T("NewSurveyShort"), newName));
            d.Info.Add((Lang.T("Method"), string.Format(Lang.T("GridMethodFmt"), r.Cell.ToString("0.###", Inv))));
            d.Info.Add((Lang.T("PointsCount"), newCount.ToString(Inv)));
            d.Cards.Add((Lang.T("CutVolume"), r.CutVolume.ToString("N2") + " m³", Color.FromArgb(229, 57, 53)));
            d.Cards.Add((Lang.T("FillVolume"), r.FillVolume.ToString("N2") + " m³", Color.FromArgb(30, 136, 229)));
            d.Cards.Add((Lang.T("Net"), r.NetVolume.ToString("N2") + " m³", Color.FromArgb(96, 125, 139)));
            d.Cards.Add((Lang.T("TotalArea"), r.TotalArea.ToString("N1") + " m²", Color.FromArgb(67, 160, 71)));
            d.Cards.Add((Lang.T("MaxCut"), r.MaxCut.ToString("0.000") + " m", Color.FromArgb(183, 28, 28)));
            d.Cards.Add((Lang.T("MaxFill"), r.MaxFill.ToString("0.000") + " m", Color.FromArgb(13, 71, 161)));
            d.Cards.Add((Lang.T("CutArea"), r.CutArea.ToString("N1") + " m²", Color.FromArgb(239, 154, 154)));
            d.Cards.Add((Lang.T("FillArea"), r.FillArea.ToString("N1") + " m²", Color.FromArgb(100, 181, 246)));
            d.Map = MapRenderer.CutFill(r, boundary);
            d.MapTitle = Lang.T("CutFillMap");
            for (int i = 0; i < ColorScale.CutFillColors.Length; i++)
            {
                var c = ColorScale.CutFillColors[i];
                d.Legend.Add((Color.FromArgb(c.r, c.g, c.b), ColorScale.CutFillLabel(i)));
            }
            d.TableTitle = Lang.T("PointDiffTable");
            d.Headers = new[] { "#", Lang.T("ColName"), "E", "N", Lang.T("NewZ"), Lang.T("OglZ"), Lang.T("Diff") };
            d.ColWeights = new[] { 0.6f, 1.2f, 1.6f, 1.8f, 1.2f, 1.2f, 1.1f };
            int k = 1;
            foreach (var p in r.Points)
            {
                bool has = !double.IsNaN(p.RefZ);
                d.Rows.Add(new[]
                {
                    (k++).ToString(Inv), p.Point.Name, p.Point.E.ToString("F3", Inv), p.Point.N.ToString("F3", Inv), p.Point.Z.ToString("F3", Inv),
                    has ? p.RefZ.ToString("F3", Inv) : "—", has ? LevelCheckForm.Signed(p.Diff) : "—",
                });
                if (!has) d.RowColors.Add(null);
                else
                {
                    var c = ColorScale.CutFillColors[ColorScale.CutFillClass(p.Diff)];
                    d.RowColors.Add(Color.FromArgb(70, c.r, c.g, c.b).Blend());
                }
            }
            d.Notes.Add(Lang.T("NoteDiffSign"));
            d.Notes.Add(string.Format(Lang.T("NoteGrid"), r.Cell.ToString("0.###", Inv)));
            if (boundary != null) d.Notes.Add(Lang.T("NoteBoundary"));
            return d;
        }

        public static ReportDoc LevelCheck(LevelCheckResult r, string element, string modeText)
        {
            var st = Settings.Current;
            var d = new ReportDoc { Title = Lang.T("LevelReportTitle") + (string.IsNullOrWhiteSpace(element) ? "" : " — " + element) };
            d.Info.Add((Lang.T("Project"), st.ProjectName));
            d.Info.Add((Lang.T("Date"), DateTime.Today.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("Tolerance"), "+" + (r.TolPlus * 1000).ToString("0") + " / −" + (r.TolMinus * 1000).ToString("0") + " mm"));
            d.Info.Add((Lang.T("DesignMode"), modeText));
            d.Cards.Add((Lang.T("Checked"), (r.Pass + r.Fail).ToString(Inv), Color.FromArgb(96, 125, 139)));
            d.Cards.Add((Lang.T("Pass"), r.Pass.ToString(Inv), Color.FromArgb(67, 160, 71)));
            d.Cards.Add((Lang.T("Fail"), r.Fail.ToString(Inv), Color.FromArgb(229, 57, 53)));
            d.Cards.Add((Lang.T("PassRate"), r.PassPercent.ToString("0.0") + "%", Color.FromArgb(30, 136, 229)));
            d.Cards.Add((Lang.T("MaxAbove"), LevelCheckForm.Signed(r.MaxAbove), Color.FromArgb(30, 136, 229)));
            d.Cards.Add((Lang.T("MaxBelow"), LevelCheckForm.Signed(r.MaxBelow), Color.FromArgb(229, 57, 53)));
            d.Cards.Add((Lang.T("MeanDiff"), LevelCheckForm.Signed(r.Mean), Color.FromArgb(96, 125, 139)));
            d.Cards.Add((Lang.T("StdDev"), r.StdDev.ToString("0.000", Inv), Color.FromArgb(96, 125, 139)));
            var pts = r.Rows.Where(x => x.Status != CheckStatus.NoDesign)
                .Select(x => (x.Point.E, x.Point.N, x.Status == CheckStatus.Pass ? Color.FromArgb(67, 160, 71) : Color.FromArgb(229, 57, 53))).ToList();
            d.Map = MapRenderer.Points(pts, null);
            d.MapTitle = Lang.T("LevelMap");
            d.Legend.Add((Color.FromArgb(67, 160, 71), Lang.T("Pass")));
            d.Legend.Add((Color.FromArgb(229, 57, 53), Lang.T("Fail")));
            d.TableTitle = Lang.T("LevelTable");
            d.Headers = new[] { "#", Lang.T("ColName"), "E", "N", Lang.T("AsBuiltZ"), Lang.T("DesignZ"), Lang.T("Diff"), Lang.T("Result") };
            d.ColWeights = new[] { 0.5f, 0.9f, 1.5f, 1.7f, 1.2f, 1.2f, 1.2f, 1f };
            int k = 1;
            foreach (var row in r.Rows)
            {
                bool has = row.Status != CheckStatus.NoDesign;
                d.Rows.Add(new[] { (k++).ToString(Inv), row.Point.Name, row.Point.E.ToString("F3", Inv), row.Point.N.ToString("F3", Inv), row.Point.Z.ToString("F3", Inv),
                    has ? row.DesignZ.ToString("F3", Inv) : "—", has ? LevelCheckForm.Signed(row.Diff) : "—", LevelCheckForm.StatusText(row.Status) });
                d.RowColors.Add(row.Status == CheckStatus.Pass ? Color.FromArgb(0xE3, 0xF5, 0xE4) : row.Status == CheckStatus.Fail ? Color.FromArgb(0xFF, 0xD6, 0xD6) : (Color?)null);
            }
            d.Notes.Add(Lang.T("NoteLevelSign"));
            return d;
        }

        public static ReportDoc Progress(P6Schedule s)
        {
            var st = Settings.Current;
            var d = new ReportDoc { Title = Lang.T("P6ReportTitle") };
            d.Info.Add((Lang.T("Project"), string.IsNullOrEmpty(st.ProjectName) ? s.ProjectName : st.ProjectName));
            d.Info.Add((Lang.T("Date"), DateTime.Today.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("P6File"), s.SourceFile));
            d.Info.Add((Lang.T("DataDate"), s.DataDate?.ToString("yyyy-MM-dd", Inv) ?? "—"));
            var groups = s.Activities.GroupBy(a => a.State(s.DataDate)).ToDictionary(g => g.Key, g => g.Count());
            foreach (ActivityState stt in Enum.GetValues(typeof(ActivityState)))
            {
                var c = P6Colors.StateColor(stt);
                d.Cards.Add((PrimaveraForm.StateText(stt), (groups.TryGetValue(stt, out int k) ? k : 0).ToString(Inv), Color.FromArgb(c.r, c.g, c.b)));
            }
            d.TableTitle = Lang.T("P6Table");
            d.Headers = new[] { Lang.T("P6Id"), Lang.T("P6Name"), Lang.T("P6Finish"), Lang.T("P6Pct"), Lang.T("PlannedQty"), Lang.T("ExecutedQty"), Lang.T("DrawingPct"), Lang.T("Status") };
            d.ColWeights = new[] { 1f, 2.4f, 1.1f, 0.7f, 1.2f, 1.2f, 0.9f, 1f };
            foreach (var a in s.Activities)
            {
                var c = P6Colors.StateColor(a.State(s.DataDate));
                d.Rows.Add(new[] { a.Id, a.Name, a.Finish?.ToString("yyyy-MM-dd", Inv) ?? "", a.P6Percent.ToString("0.#", Inv),
                    a.PlannedQty > 0 ? a.PlannedQty.ToString("N0", Inv) + " " + a.Unit : "", a.ExecutedQty > 0 ? a.ExecutedQty.ToString("N0", Inv) + " " + a.Unit : "",
                    a.DrawingPercent >= 0 ? a.DrawingPercent.ToString("0.#", Inv) + "%" : "—", PrimaveraForm.StateText(a.State(s.DataDate)) });
                d.RowColors.Add(Color.FromArgb(60, c.r, c.g, c.b).Blend());
            }
            d.Notes.Add(Lang.T("NoteP6"));
            return d;
        }
        public static ReportDoc Cumulative(IList<CutFillLog.Entry> e, string area)
        {
            var st = Settings.Current;
            var d = new ReportDoc { Title = Lang.T("CumTitle") + (string.IsNullOrWhiteSpace(area) ? "" : " — " + area) };
            d.Info.Add((Lang.T("Project"), st.ProjectName));
            d.Info.Add((Lang.T("Date"), DateTime.Today.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("Period"), e.First().Date.ToString("yyyy-MM-dd", Inv) + " → " + e.Last().Date.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("Days"), e.Count.ToString(Inv)));
            double tc = e.Sum(x => x.Cut), tf = e.Sum(x => x.Fill);
            d.Cards.Add((Lang.T("CumCut"), tc.ToString("N2") + " m³", Color.FromArgb(229, 57, 53)));
            d.Cards.Add((Lang.T("CumFill"), tf.ToString("N2") + " m³", Color.FromArgb(30, 136, 229)));
            d.Cards.Add((Lang.T("Net"), (tf - tc).ToString("N2") + " m³", Color.FromArgb(96, 125, 139)));
            d.Cards.Add((Lang.T("Days"), e.Count.ToString(Inv), Color.FromArgb(67, 160, 71)));
            d.TableTitle = Lang.T("CumTable");
            d.Headers = new[] { Lang.T("Date"), Lang.T("AreaShort"), Lang.T("Cut"), Lang.T("Fill"), Lang.T("CumCut"), Lang.T("CumFill") };
            d.ColWeights = new[] { 1.2f, 1.4f, 1f, 1f, 1.2f, 1.2f };
            double cc = 0, cf = 0;
            foreach (var x in e)
            {
                cc += x.Cut; cf += x.Fill;
                d.Rows.Add(new[] { x.Date.ToString("yyyy-MM-dd", Inv), x.Area, x.Cut.ToString("N2"), x.Fill.ToString("N2"), cc.ToString("N2"), cf.ToString("N2") });
            }
            d.Notes.Add(Lang.T("NoteCum"));
            return d;
        }

        public static ReportDoc SectionVolumes(SectionVolumes v, IList<(double x, double y)> align, double st0, double interval, double left, double right, int sections)
        {
            var st = Settings.Current;
            var d = new ReportDoc { Title = Lang.T("SectionVolTitle") };
            d.Info.Add((Lang.T("Project"), st.ProjectName));
            d.Info.Add((Lang.T("Date"), DateTime.Today.ToString("yyyy-MM-dd", Inv)));
            d.Info.Add((Lang.T("Chainage"), Geo2D.FormatChainage(0, st0) + " → " + Geo2D.FormatChainage(Geo2D.Length(align), st0)));
            d.Info.Add((Lang.T("Interval"), interval.ToString(Inv) + " m"));
            d.Info.Add((Lang.T("Width"), "L " + left.ToString(Inv) + " / R " + right.ToString(Inv) + " m"));
            d.Cards.Add((Lang.T("CutVolume"), v.TotalCut.ToString("N2") + " m³", Color.FromArgb(229, 57, 53)));
            d.Cards.Add((Lang.T("FillVolume"), v.TotalFill.ToString("N2") + " m³", Color.FromArgb(30, 136, 229)));
            d.Cards.Add((Lang.T("Net"), (v.TotalFill - v.TotalCut).ToString("N2") + " m³", Color.FromArgb(96, 125, 139)));
            d.Cards.Add((Lang.T("Sections"), sections.ToString(Inv), Color.FromArgb(67, 160, 71)));
            d.TableTitle = Lang.T("EndAreaTable");
            d.Headers = new[] { Lang.T("Chainage"), Lang.T("CutArea"), Lang.T("FillArea"), Lang.T("CutVolume"), Lang.T("FillVolume"), Lang.T("CumCut"), Lang.T("CumFill") };
            double cc = 0, cf = 0;
            foreach (var r in v.Rows)
            {
                cc += r.cutVol; cf += r.fillVol;
                d.Rows.Add(new[] { Geo2D.FormatChainage(r.ch, st0), r.cutArea.ToString("F2", Inv), r.fillArea.ToString("F2", Inv), r.cutVol.ToString("F2", Inv), r.fillVol.ToString("F2", Inv), cc.ToString("F2", Inv), cf.ToString("F2", Inv) });
            }
            d.Notes.Add(Lang.T("NoteEndArea"));
            return d;
        }
    }
}
