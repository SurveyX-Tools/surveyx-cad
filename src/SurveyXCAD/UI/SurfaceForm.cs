using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Cad;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>Surface (TIN) from points: 3D faces / Civil 3D surface, contours, elevation colour map, report.</summary>
    public sealed class SurfaceForm : SxForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PointSource _src;
        private readonly NumericUpDown _maxEdge = new NumericUpDown { Minimum = 0, Maximum = 10000, Width = 80 };
        private readonly CheckBox _faces = new CheckBox { AutoSize = true }, _civil = new CheckBox { AutoSize = true }, _cogo = new CheckBox { AutoSize = true },
            _contours = new CheckBox { AutoSize = true, Checked = true }, _labels = new CheckBox { AutoSize = true, Checked = true }, _map = new CheckBox { AutoSize = true };
        private readonly TextBox _name = new TextBox { Width = 200, Text = "SX_Surface" };
        private readonly NumericUpDown _interval = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.05m, Maximum = 100, Increment = 0.25m, Width = 80 };
        private readonly NumericUpDown _major = new NumericUpDown { Minimum = 0, Maximum = 50, Value = 5, Width = 60 };
        private readonly NumericUpDown _cell = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 100, Increment = 0.5m, Width = 80 };
        private readonly Label _info = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Soft, Font = new Font("Segoe UI", 10.5f) };
        private Tin _tin;

        public SurfaceForm() : base("SurfaceTitle", "HelpSurface")
        {
            var st = Settings.Current;
            Size = new Size(780, 640);
            MinimumSize = new Size(700, 580);
            _src = new PointSource(this, "SurfacePoints") { Densify = 1.0 };
            _src.Changed += (s, e) => _info.Text = Lang.T("PressBuild");
            _maxEdge.Value = (decimal)Math.Max(0, Math.Min(10000, st.MaxEdge));
            _interval.Value = (decimal)Math.Max(0.05, Math.Min(100, st.ContourInterval));
            _cell.Value = (decimal)Math.Max(0, Math.Min(100, st.GridCell));
            _faces.Text = Lang.T("DrawFaces");
            _civil.Text = Lang.T("MakeCivilSurface");
            _cogo.Text = Lang.T("MakeCogo");
            _civil.Enabled = _cogo.Enabled = CivilBridge.Available;
            _civil.Checked = CivilBridge.Available;
            _faces.Checked = !CivilBridge.Available;
            _contours.Text = Lang.T("DrawContours");
            _labels.Text = Lang.T("LabelMajor");
            _map.Text = Lang.T("ElevationMap");

            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
            Row(t, UiKit.Lbl("MaxEdge"), _maxEdge);
            Row(t, _faces);
            Row(t, _civil, _name);
            Row(t, _cogo);
            Row(t, _contours, UiKit.Lbl("Interval"), _interval, UiKit.Lbl("MajorEvery"), _major, _labels);
            Row(t, _map, UiKit.Lbl("GridCell"), _cell);

            var build = UiKit.Btn("Build", true); build.Click += (s, e) => Build();
            var pdf = UiKit.Btn("PdfReport"); pdf.Click += (s, e) => Pdf();
            var close = UiKit.Btn("Close"); close.Click += (s, e) => Close(); CancelButton = close;

            Controls.Add(_info);
            Controls.Add(t);
            Controls.Add(_src);
            Controls.Add(Header("SurfaceTitle"));
            Controls.Add(UiKit.ButtonBar(close, pdf, build));
            _info.Text = Lang.T("SurfaceIntro");
        }

        private static void Row(TableLayoutPanel t, params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            f.Controls.AddRange(cs);
            t.Controls.Add(f);
        }

        private bool EnsureTin()
        {
            if (_src.Points.Count < 3) { UiKit.Error(Lang.T("ChooseSurfacePoints")); return false; }
            if (_tin == null || _tin.Count != _src.Points.Count)
                _tin = new Tin(_src.Points, (double)_maxEdge.Value);
            return true;
        }

        private void Build()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                _tin = null;
                if (!EnsureTin()) return;
                var st = Settings.Current;
                st.MaxEdge = (double)_maxEdge.Value; st.ContourInterval = (double)_interval.Value; st.GridCell = (double)_cell.Value; st.Save();
                var msg = new System.Text.StringBuilder();
                msg.AppendLine(string.Format(Lang.T("TinStats"), _tin.Count, _tin.TriangleCount, _tin.Area().ToString("N1"), _tin.MinZ.ToString("F3", Inv), _tin.MaxZ.ToString("F3", Inv)));
                if (_faces.Checked)
                {
                    string layer = PointDrawer.Layer("TIN");
                    CadDraw.ClearLayer(layer);
                    msg.AppendLine(string.Format(Lang.T("FacesDrawn"), CadDraw.Faces(_tin, layer), layer));
                }
                if (_civil.Checked && CivilBridge.Available)
                {
                    var pc = new Point3dCollection();
                    foreach (var p in _src.Points) pc.Add(new Point3d(p.E, p.N, p.Z));
                    msg.AppendLine(Lang.T("CivilSurfaceMade") + " " + CivilBridge.CreateTinSurface(string.IsNullOrWhiteSpace(_name.Text) ? "SX_Surface" : _name.Text.Trim(), pc));
                }
                if (_cogo.Checked && CivilBridge.Available)
                    msg.AppendLine(string.Format(Lang.T("CogoMade"), CivilBridge.CreateCogoPoints(_src.Points)));
                if (_contours.Checked)
                {
                    string minor = PointDrawer.Layer("CONTOUR_MINOR"), major = PointDrawer.Layer("CONTOUR_MAJOR");
                    CadDraw.ClearLayer(minor); CadDraw.ClearLayer(major);
                    int n = CadDraw.Contours(_tin, (double)_interval.Value, (int)_major.Value, minor, major, _labels.Checked, st.TextHeight);
                    msg.AppendLine(string.Format(Lang.T("ContoursDrawn"), n));
                }
                if (_map.Checked)
                {
                    var g = Grid();
                    string layer = PointDrawer.Layer("ELEV_MAP");
                    CadDraw.ClearLayer(layer);
                    double zmin = _tin.MinZ, zmax = _tin.MaxZ;
                    const int bands = 10;
                    var colors = Enumerable.Range(0, bands).Select(i => ColorScale.Elevation((i + 0.5) / bands)).ToArray();
                    int n = CadDraw.GridMap(g, z => Math.Max(0, Math.Min(bands - 1, (int)((z - zmin) / Math.Max(1e-9, zmax - zmin) * bands))), colors, layer);
                    var items = Enumerable.Range(0, bands).Select(i =>
                        (colors[i], (zmin + (zmax - zmin) * i / bands).ToString("0.00", Inv) + " – " + (zmin + (zmax - zmin) * (i + 1) / bands).ToString("0.00", Inv))).ToList();
                    double h = Math.Max(st.TextHeight, (_tin.MaxX - _tin.MinX) / 120);
                    CadDraw.Legend(new Point3d(_tin.MaxX + h * 4, _tin.MaxY, 0), "ELEVATION (m)", items, h, layer);
                    msg.AppendLine(string.Format(Lang.T("MapDrawn"), n, layer));
                }
                _info.Text = msg.ToString();
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private ElevationGrid Grid()
        {
            double cell = (double)_cell.Value;
            if (cell <= 0) cell = CutFill.AutoCell(_tin.MaxX - _tin.MinX, _tin.MaxY - _tin.MinY);
            ElevationGrid.Frame(_tin.MinX, _tin.MinY, _tin.MaxX, _tin.MaxY, cell, out double x0, out double y0, out int nx, out int ny);
            return _tin.ToGrid(x0, y0, nx, ny, cell);
        }

        private void Pdf()
        {
            try
            {
                if (!EnsureTin()) return;
                var path = UiKit.BrowseSave(this, "PDF|*.pdf", "Surface_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".pdf", out _);
                if (path == null) return;
                Cursor = Cursors.WaitCursor;
                var st = Settings.Current;
                var zs = _src.Points.Select(p => p.Z).ToList();
                var d = new ReportDoc { Title = Lang.T("SurfaceReportTitle") };
                d.Info.Add((Lang.T("Project"), st.ProjectName));
                d.Info.Add((Lang.T("Date"), DateTime.Today.ToString("yyyy-MM-dd", Inv)));
                d.Info.Add((Lang.T("Source"), _src.FilePath != null ? System.IO.Path.GetFileName(_src.FilePath) : Lang.T("Drawing")));
                d.Info.Add((Lang.T("MaxEdge"), _maxEdge.Value.ToString(Inv) + " m"));
                d.Cards.Add((Lang.T("PointsCount"), _tin.Count.ToString("N0"), Color.FromArgb(96, 125, 139)));
                d.Cards.Add((Lang.T("Triangles"), _tin.TriangleCount.ToString("N0"), Color.FromArgb(96, 125, 139)));
                d.Cards.Add((Lang.T("Area2D"), _tin.Area().ToString("N1") + " m²", Color.FromArgb(67, 160, 71)));
                d.Cards.Add((Lang.T("MeanZ"), zs.Average().ToString("F3", Inv), Color.FromArgb(30, 136, 229)));
                d.Cards.Add((Lang.T("MinZ"), _tin.MinZ.ToString("F3", Inv), Color.FromArgb(30, 136, 229)));
                d.Cards.Add((Lang.T("MaxZ"), _tin.MaxZ.ToString("F3", Inv), Color.FromArgb(229, 57, 53)));
                d.Map = MapRenderer.ElevationGrid(Grid(), _tin.MinZ, _tin.MaxZ);
                d.MapTitle = Lang.T("ElevationMap");
                for (int i = 0; i < 10; i++)
                {
                    var c = ColorScale.Elevation((i + 0.5) / 10);
                    d.Legend.Add((Color.FromArgb(c.r, c.g, c.b), (_tin.MinZ + (_tin.MaxZ - _tin.MinZ) * i / 10).ToString("0.00", Inv) + " – " + (_tin.MinZ + (_tin.MaxZ - _tin.MinZ) * (i + 1) / 10).ToString("0.00", Inv)));
                }
                ReportBuilder.SavePdf(d, path);
                if (UiKit.Ask(Lang.T("ReportSavedOpen") + "\n" + path)) UiKit.OpenUrl(path);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }
    }
}
