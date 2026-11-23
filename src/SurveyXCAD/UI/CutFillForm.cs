using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Cad;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.UI
{
    /// <summary>Daily cut / fill: new survey vs OGL → volumes, colour map, point differences, PDF report, Primavera quantity.</summary>
    public sealed class CutFillForm : SxForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PointSource _ogl, _new;
        private readonly BoundaryPicker _boundary;
        private readonly NumericUpDown _cell = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 50, Increment = 0.25m, Width = 80 };
        private readonly NumericUpDown _maxEdge = new NumericUpDown { DecimalPlaces = 0, Minimum = 0, Maximum = 10000, Width = 80 };
        private readonly DateTimePicker _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 130 };
        private readonly TextBox _area = new TextBox { Width = 220 };
        private readonly Label _summary = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f), Padding = new Padding(10), BackColor = Soft };
        private readonly ComboBox _act = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly ComboBox _which = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly FlowLayoutPanel _actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        private CutFillResult _r;
        private P6Schedule _p6;

        public CutFillForm() : base("CutFillTitle", "HelpCutFill")
        {
            var st = Settings.Current;
            Size = new Size(900, 760);
            MinimumSize = new Size(780, 640);
            _ogl = new PointSource(this, "OglSource", st.LastOglPath);
            _new = new PointSource(this, "NewSurveySource");
            _boundary = new BoundaryPicker(this);
            _cell.Value = (decimal)Math.Max(0, Math.Min(50, st.GridCell));
            _maxEdge.Value = (decimal)Math.Max(0, Math.Min(10000, st.MaxEdge));
            _area.Text = "";

            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            opts.Controls.Add(UiKit.Lbl("GridCell")); opts.Controls.Add(_cell);
            opts.Controls.Add(UiKit.Lbl("MaxEdge")); opts.Controls.Add(_maxEdge);
            opts.Controls.Add(UiKit.Lbl("ReportDate")); opts.Controls.Add(_date);
            opts.Controls.Add(UiKit.Lbl("AreaName")); opts.Controls.Add(_area);

            var calc = UiKit.Btn("Calculate", true);
            calc.Click += (s, e) => Calculate();
            var calcBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            calcBar.Controls.Add(calc);

            var bMap = UiKit.Btn("DrawMap"); bMap.Click += (s, e) => DrawMap();
            var bLbl = UiKit.Btn("DrawPointDiffs"); bLbl.Click += (s, e) => DrawLabels();
            var bPdf = UiKit.Btn("PdfReport", true); bPdf.Click += (s, e) => Pdf();
            var bCsv = UiKit.Btn("SaveCsv"); bCsv.Click += (s, e) => Csv();
            _actions.Controls.AddRange(new Control[] { bPdf, bMap, bLbl, bCsv });

            _which.Items.Add(Lang.T("Cut")); _which.Items.Add(Lang.T("Fill")); _which.SelectedIndex = 1;
            var bAdd = UiKit.Btn("AddToActivity"); bAdd.Click += (s, e) => AddToActivity();
            var p6Bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), BackColor = Soft };
            p6Bar.Controls.Add(UiKit.Lbl("Primavera", true)); p6Bar.Controls.Add(_act); p6Bar.Controls.Add(_which); p6Bar.Controls.Add(bAdd);
            _p6 = P6Store.Load();
            if (_p6 != null) foreach (var a in _p6.Activities) _act.Items.Add(a.Id + " — " + a.Name);
            if (_act.Items.Count > 0) _act.SelectedIndex = 0; else { _act.Items.Add(Lang.T("NoSchedule")); _act.SelectedIndex = 0; _act.Enabled = false; bAdd.Enabled = false; }

            var close = UiKit.Btn("Close"); close.Click += (s, e) => Close(); CancelButton = close;

            Controls.Add(_summary);
            Controls.Add(calcBar);
            Controls.Add(opts);
            Controls.Add(_boundary);
            Controls.Add(_new);
            Controls.Add(_ogl);
            Controls.Add(Header("CutFillTitle"));
            Controls.Add(_actions);
            Controls.Add(p6Bar);
            Controls.Add(UiKit.ButtonBar(close));
            _summary.Text = Lang.T("CutFillIntro");
            SetActions(false);
        }

        private void SetActions(bool on) { foreach (Control c in _actions.Controls) c.Enabled = on; }

        private void Calculate()
        {
            if (!_ogl.EnsureLoaded() || _ogl.Points.Count < 3) { UiKit.Error(Lang.T("ChooseOgl")); return; }
            if (_new.Points.Count < 3) { UiKit.Error(Lang.T("ChooseNewSurvey")); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                double maxEdge = (double)_maxEdge.Value;
                var oglTin = new Tin(_ogl.Points, maxEdge);
                var newTin = new Tin(_new.Points, maxEdge);
                _r = CutFill.Compute(oglTin, newTin, _new.Points, _boundary.Polygon, (double)_cell.Value);
                var st = Settings.Current;
                st.GridCell = (double)_cell.Value; st.MaxEdge = maxEdge;
                if (_ogl.FilePath != null) st.LastOglPath = _ogl.FilePath;
                st.Save();
                var sb = new StringBuilder();
                sb.AppendLine(Lang.T("Cut") + ":   " + _r.CutVolume.ToString("N2") + " m³      (" + _r.CutArea.ToString("N1") + " m²)");
                sb.AppendLine(Lang.T("Fill") + ":   " + _r.FillVolume.ToString("N2") + " m³      (" + _r.FillArea.ToString("N1") + " m²)");
                sb.AppendLine(Lang.T("Net") + ":   " + _r.NetVolume.ToString("N2") + " m³");
                sb.AppendLine(Lang.T("TotalArea") + ":   " + _r.TotalArea.ToString("N1") + " m²");
                sb.AppendLine(Lang.T("MaxCut") + ": " + _r.MaxCut.ToString("0.000") + " m     " + Lang.T("MaxFill") + ": " + _r.MaxFill.ToString("0.000") + " m");
                sb.AppendLine(Lang.T("GridCellUsed") + " " + _r.Cell.ToString("0.###") + " m   ·   " + string.Format(Lang.T("PointsCompared"), _r.Points.Count(p => !double.IsNaN(p.RefZ))));
                _summary.Text = sb.ToString();
                SetActions(true);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void DrawMap()
        {
            string layer = PointDrawer.Layer("CUTFILL_MAP");
            CadDraw.ClearLayer(layer);
            int n = CadDraw.GridMap(_r.DiffGrid, d => ColorScale.CutFillClass(d), ColorScale.CutFillColors, layer);
            var items = Enumerable.Range(0, ColorScale.CutFillColors.Length).Select(i => (ColorScale.CutFillColors[i], ColorScale.CutFillLabel(i, english: true))).ToList();
            double h = Math.Max(Settings.Current.TextHeight, (_r.MaxX - _r.MinX) / 120);
            CadDraw.Legend(new Point3d(_r.MaxX + h * 4, _r.MaxY, 0), "CUT / FILL (m)  " + _date.Value.ToString("yyyy-MM-dd", Inv), items, h, layer);
            UiKit.Info(string.Format(Lang.T("MapDrawn"), n, layer));
        }

        private void DrawLabels()
        {
            var items = _r.Points.Where(p => !double.IsNaN(p.RefZ)).Select(p =>
            {
                double d = p.Diff;
                short aci = (short)(Math.Abs(d) <= ColorScale.CutFillBreaks[4] ? 3 : d < 0 ? 1 : 5);
                return (p.Point.E, p.Point.N, p.Point.Z, (d >= 0 ? "+" : "") + d.ToString("0.000", Inv), aci);
            }).ToList();
            string layer = PointDrawer.Layer("CUTFILL_DIFF");
            CadDraw.ClearLayer(layer);
            CadDraw.DiffLabels(items, Settings.Current.TextHeight, layer);
            UiKit.Info(string.Format(Lang.T("LabelsDrawn"), items.Count, layer));
        }

        private string DefaultName(string ext) =>
            "CutFill_" + (string.IsNullOrWhiteSpace(_area.Text) ? "" : Safe(_area.Text) + "_") + _date.Value.ToString("yyyy-MM-dd", Inv) + ext;

        internal static string Safe(string s) => new string((s ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

        private void Csv()
        {
            var path = UiKit.BrowseSave(this, "CSV|*.csv", DefaultName(".csv"), out _);
            if (path == null) return;
            WriteCsv(path);
            UiKit.Info(Lang.T("Saved") + "\n" + path);
        }

        private void WriteCsv(string path)
        {
            var sb = new StringBuilder("Point,Easting,Northing,New Z,OGL Z,Difference (+fill/-cut),Description\n");
            foreach (var p in _r.Points)
                sb.Append(p.Point.Name).Append(',').Append(p.Point.E.ToString("F3", Inv)).Append(',').Append(p.Point.N.ToString("F3", Inv)).Append(',')
                  .Append(p.Point.Z.ToString("F3", Inv)).Append(',').Append(double.IsNaN(p.RefZ) ? "" : p.RefZ.ToString("F3", Inv)).Append(',')
                  .Append(double.IsNaN(p.RefZ) ? "" : p.Diff.ToString("F3", Inv)).Append(',').Append((p.Point.Desc ?? "").Replace(",", " ")).Append('\n');
            sb.Append('\n').Append("Cut volume (m3),").Append(_r.CutVolume.ToString("F2", Inv)).Append('\n')
              .Append("Fill volume (m3),").Append(_r.FillVolume.ToString("F2", Inv)).Append('\n')
              .Append("Net (m3),").Append(_r.NetVolume.ToString("F2", Inv)).Append('\n')
              .Append("Area (m2),").Append(_r.TotalArea.ToString("F2", Inv)).Append('\n')
              .Append("Grid cell (m),").Append(_r.Cell.ToString("0.###", Inv)).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private void Pdf()
        {
            var path = UiKit.BrowseSave(this, "PDF|*.pdf", DefaultName(".pdf"), out _);
            if (path == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var st = Settings.Current;
                var d = new ReportDoc { Title = Lang.T("CutFillReportTitle") + (string.IsNullOrWhiteSpace(_area.Text) ? "" : " — " + _area.Text) };
                d.Info.Add((Lang.T("Project"), st.ProjectName));
                d.Info.Add((Lang.T("Date"), _date.Value.ToString("yyyy-MM-dd", Inv)));
                d.Info.Add((Lang.T("OglSourceShort"), _ogl.FilePath != null ? Path.GetFileName(_ogl.FilePath) : Lang.T("Drawing")));
                d.Info.Add((Lang.T("NewSurveyShort"), _new.FilePath != null ? Path.GetFileName(_new.FilePath) : Lang.T("Drawing")));
                d.Info.Add((Lang.T("Method"), Lang.T("GridMethod") + " " + _r.Cell.ToString("0.###", Inv) + " m"));
                d.Info.Add((Lang.T("PointsCount"), _new.Points.Count.ToString(Inv)));
                d.Cards.Add((Lang.T("CutVolume"), _r.CutVolume.ToString("N2") + " m³", Color.FromArgb(229, 57, 53)));
                d.Cards.Add((Lang.T("FillVolume"), _r.FillVolume.ToString("N2") + " m³", Color.FromArgb(30, 136, 229)));
                d.Cards.Add((Lang.T("Net"), _r.NetVolume.ToString("N2") + " m³", Color.FromArgb(96, 125, 139)));
                d.Cards.Add((Lang.T("TotalArea"), _r.TotalArea.ToString("N1") + " m²", Color.FromArgb(67, 160, 71)));
                d.Cards.Add((Lang.T("MaxCut"), _r.MaxCut.ToString("0.000") + " m", Color.FromArgb(183, 28, 28)));
                d.Cards.Add((Lang.T("MaxFill"), _r.MaxFill.ToString("0.000") + " m", Color.FromArgb(13, 71, 161)));
                d.Cards.Add((Lang.T("CutArea"), _r.CutArea.ToString("N1") + " m²", Color.FromArgb(239, 154, 154)));
                d.Cards.Add((Lang.T("FillArea"), _r.FillArea.ToString("N1") + " m²", Color.FromArgb(100, 181, 246)));
                d.Map = MapRenderer.CutFill(_r, _boundary.Polygon);
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
                foreach (var p in _r.Points)
                {
                    bool has = !double.IsNaN(p.RefZ);
                    d.Rows.Add(new[]
                    {
                        (k++).ToString(Inv), p.Point.Name, p.Point.E.ToString("F3", Inv), p.Point.N.ToString("F3", Inv), p.Point.Z.ToString("F3", Inv),
                        has ? p.RefZ.ToString("F3", Inv) : "—", has ? (p.Diff >= 0 ? "+" : "") + p.Diff.ToString("F3", Inv) : "—",
                    });
                    if (!has) d.RowColors.Add(null);
                    else
                    {
                        var c = ColorScale.CutFillColors[ColorScale.CutFillClass(p.Diff)];
                        d.RowColors.Add(Color.FromArgb(70, c.r, c.g, c.b).Blend());
                    }
                }
                d.Notes.Add(Lang.T("NoteDiffSign"));
                d.Notes.Add(string.Format(Lang.T("NoteGrid"), _r.Cell.ToString("0.###", Inv)));
                if (_boundary.Polygon != null) d.Notes.Add(Lang.T("NoteBoundary"));
                ReportBuilder.SavePdf(d, path);
                WriteCsv(Path.ChangeExtension(path, ".csv"));
                if (UiKit.Ask(Lang.T("ReportSavedOpen") + "\n" + path)) UiKit.OpenUrl(path);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void AddToActivity()
        {
            if (_p6 == null || _act.SelectedIndex < 0) return;
            var a = _p6.Activities[_act.SelectedIndex];
            double vol = _which.SelectedIndex == 0 ? _r.CutVolume : _r.FillVolume;
            if (!UiKit.Ask(string.Format(Lang.T("ConfirmAddQty"), vol.ToString("N2"), a.Id))) return;
            a.ExecutedQty += vol;
            if (string.IsNullOrEmpty(a.Unit)) a.Unit = "m3";
            P6Store.Save(_p6);
            string pct = a.PlannedQty > 0 ? (a.ExecutedQty / a.PlannedQty * 100).ToString("0.0") + "%" : Lang.T("NoPlannedQty");
            UiKit.Info(string.Format(Lang.T("QtyAdded"), a.Id, a.ExecutedQty.ToString("N2"), pct));
        }
    }

    internal static class ColorExt
    {
        /// <summary>Pre-blends a semi-transparent colour over white (GDI table cells are opaque).</summary>
        public static Color Blend(this Color c)
        {
            double a = c.A / 255.0;
            return Color.FromArgb((int)(c.R * a + 255 * (1 - a)), (int)(c.G * a + 255 * (1 - a)), (int)(c.B * a + 255 * (1 - a)));
        }
    }
}
