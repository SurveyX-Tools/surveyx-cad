using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SurveyX.Cad;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>As-built levels vs design (kerbs, layers, slabs) with tolerance → pass / fail.</summary>
    public sealed class LevelCheckForm : SxForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PointSource _asb, _design;
        private readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly NumericUpDown _plus = new NumericUpDown { DecimalPlaces = 3, Minimum = 0, Maximum = 1, Increment = 0.005m, Width = 80 };
        private readonly NumericUpDown _minus = new NumericUpDown { DecimalPlaces = 3, Minimum = 0, Maximum = 1, Increment = 0.005m, Width = 80 };
        private readonly NumericUpDown _radius = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.01m, Maximum = 100, Value = 0.5m, Width = 70 };
        private readonly ComboBox _preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly TextBox _element = new TextBox { Width = 200 };
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _summary = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8), BackColor = Soft, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        private readonly FlowLayoutPanel _actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        private LevelCheckResult _r;

        private static readonly (string key, double plus, double minus)[] Presets =
        {
            ("PresetCustom", 0, 0), ("PresetKerb", 0.010, 0.010), ("PresetAsphalt", 0.010, 0.010), ("PresetBase", 0.015, 0.015),
            ("PresetSubbase", 0.020, 0.020), ("PresetSubgrade", 0.025, 0.025), ("PresetEarth", 0.050, 0.050),
        };

        public LevelCheckForm() : base("LevelCheckTitle", "HelpLevelCheck")
        {
            var st = Settings.Current;
            Size = new Size(1000, 780);
            MinimumSize = new Size(860, 640);
            _asb = new PointSource(this, "AsBuiltSource");
            _design = new PointSource(this, "DesignSource", st.LastDesignPath) { Densify = 1.0 };
            _mode.Items.Add(Lang.T("ModeSurface"));
            _mode.Items.Add(Lang.T("ModeName"));
            _mode.Items.Add(Lang.T("ModeNearest"));
            _mode.SelectedIndex = 0;
            _mode.SelectedIndexChanged += (s, e) => _radius.Enabled = _mode.SelectedIndex == 2;
            _radius.Enabled = false;
            _plus.Value = (decimal)Math.Min(1, Math.Abs(st.TolPlus));
            _minus.Value = (decimal)Math.Min(1, Math.Abs(st.TolMinus));
            foreach (var p in Presets) _preset.Items.Add(Lang.T(p.key) + (p.plus > 0 ? "  ±" + (p.plus * 1000).ToString("0") + " mm" : ""));
            _preset.SelectedIndex = 0;
            _preset.SelectedIndexChanged += (s, e) =>
            {
                var p = Presets[_preset.SelectedIndex];
                if (p.plus > 0) { _plus.Value = (decimal)p.plus; _minus.Value = (decimal)p.minus; _element.Text = Lang.T(p.key); }
            };

            var o1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            o1.Controls.Add(UiKit.Pair("DesignMode", _mode));
            o1.Controls.Add(UiKit.Pair("Radius", _radius));
            var o2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            o2.Controls.Add(UiKit.Pair("Element", _preset)); o2.Controls.Add(_element);
            o2.Controls.Add(UiKit.Pair("TolPlus", _plus));
            o2.Controls.Add(UiKit.Pair("TolMinus", _minus));
            var check = UiKit.Btn("CheckLevels", true);
            check.Click += (s, e) => Run();
            o2.Controls.Add(check);

            SetupGrid();
            var bLbl = UiKit.Btn("DrawResults"); bLbl.Click += (s, e) => Draw();
            var bPdf = UiKit.Btn("PdfReport", true); bPdf.Click += (s, e) => Pdf();
            var bCsv = UiKit.Btn("SaveCsv"); bCsv.Click += (s, e) => Csv();
            _actions.Controls.AddRange(new Control[] { bPdf, bLbl, bCsv });
            var close = UiKit.Btn("Close"); close.Click += (s, e) => Close(); CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(_summary);
            Controls.Add(o2);
            Controls.Add(o1);
            Controls.Add(_design);
            Controls.Add(_asb);
            Controls.Add(Header("LevelCheckTitle"));
            Controls.Add(_actions);
            Controls.Add(UiKit.ButtonBar(close));
            _summary.Text = Lang.T("LevelCheckIntro");
            foreach (Control c in _actions.Controls) c.Enabled = false;
        }

        private void SetupGrid()
        {
            _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; _grid.BackgroundColor = Color.White; _grid.VirtualMode = true;
            _grid.RightToLeft = RightToLeft.No;
            foreach (var h in new[] { Lang.T("ColName"), "E", "N", Lang.T("AsBuiltZ"), Lang.T("DesignZ"), Lang.T("Diff"), Lang.T("Result") })
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.CellValueNeeded += (s, e) =>
            {
                if (_r == null || e.RowIndex >= _r.Rows.Count) return;
                var r = _r.Rows[e.RowIndex];
                switch (e.ColumnIndex)
                {
                    case 0: e.Value = r.Point.Name; break;
                    case 1: e.Value = r.Point.E.ToString("F3", Inv); break;
                    case 2: e.Value = r.Point.N.ToString("F3", Inv); break;
                    case 3: e.Value = r.Point.Z.ToString("F3", Inv); break;
                    case 4: e.Value = double.IsNaN(r.DesignZ) ? "—" : r.DesignZ.ToString("F3", Inv); break;
                    case 5: e.Value = double.IsNaN(r.DesignZ) ? "—" : (r.Diff >= 0 ? "+" : "") + r.Diff.ToString("F3", Inv); break;
                    case 6: e.Value = StatusText(r.Status); break;
                }
            };
            _grid.CellFormatting += (s, e) =>
            {
                if (_r == null || e.RowIndex >= _r.Rows.Count) return;
                var st = _r.Rows[e.RowIndex].Status;
                e.CellStyle.BackColor = st == CheckStatus.Pass ? Color.FromArgb(0xE3, 0xF5, 0xE4) : st == CheckStatus.Fail ? Color.FromArgb(0xFF, 0xD6, 0xD6) : Color.FromArgb(0xEE, 0xEE, 0xEE);
            };
        }

        internal static string StatusText(CheckStatus s) => s == CheckStatus.Pass ? Lang.T("Pass") : s == CheckStatus.Fail ? Lang.T("Fail") : Lang.T("NoDesignShort");

        private void Run()
        {
            if (_asb.Points.Count == 0) { UiKit.Error(Lang.T("ChooseAsBuilt")); return; }
            if (!_design.EnsureLoaded() || _design.Points.Count == 0) { UiKit.Error(Lang.T("ChooseDesign")); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                double tp = (double)_plus.Value, tm = (double)_minus.Value;
                switch (_mode.SelectedIndex)
                {
                    case 0: _r = LevelCheck.AgainstSurface(_asb.Points, new Tin(_design.Points, Settings.Current.MaxEdge), tp, tm); break;
                    case 1: _r = LevelCheck.AgainstNames(_asb.Points, _design.Points, tp, tm); break;
                    default: _r = LevelCheck.AgainstNearest(_asb.Points, _design.Points, (double)_radius.Value, tp, tm); break;
                }
                var st = Settings.Current;
                st.TolPlus = tp; st.TolMinus = tm;
                if (_design.FilePath != null) st.LastDesignPath = _design.FilePath;
                st.Save();
                _grid.RowCount = 0;
                _grid.RowCount = _r.Rows.Count;
                _summary.Text = string.Format(Lang.T("LevelSummary"), _r.Pass + _r.Fail, _r.Pass, _r.Fail, _r.PassPercent.ToString("0.0"), _r.NoDesign) + "\n" +
                                string.Format(Lang.T("LevelStats"), Signed(_r.MaxAbove), Signed(_r.MaxBelow), Signed(_r.Mean), _r.StdDev.ToString("0.000", Inv));
                _summary.ForeColor = _r.Fail == 0 ? Color.DarkGreen : Color.DarkRed;
                foreach (Control c in _actions.Controls) c.Enabled = true;
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        internal static string Signed(double v)
        {
            v = Math.Round(v, 3);
            if (v == 0) return "0.000";
            return (v > 0 ? "+" : "") + v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void Draw()
        {
            var items = _r.Rows.Where(r => r.Status != CheckStatus.NoDesign)
                .Select(r => (r.Point.E, r.Point.N, r.Point.Z, Signed(r.Diff), (short)(r.Status == CheckStatus.Pass ? 3 : 1))).ToList();
            string layer = PointDrawer.Layer("LEVEL_CHECK");
            CadDraw.ClearLayer(layer);
            CadDraw.DiffLabels(items, Settings.Current.TextHeight, layer);
            UiKit.Info(string.Format(Lang.T("LabelsDrawn"), items.Count, layer));
        }

        private string FileNameFor(string ext) => "LevelCheck_" + (string.IsNullOrWhiteSpace(_element.Text) ? "" : CutFillForm.Safe(_element.Text) + "_") + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ext;

        private void Csv()
        {
            var path = UiKit.BrowseSave(this, "CSV|*.csv", FileNameFor(".csv"), out _);
            if (path == null) return;
            WriteCsv(path);
            UiKit.Info(Lang.T("Saved") + "\n" + path);
        }

        private void WriteCsv(string path)
        {
            var sb = new StringBuilder("Point,Easting,Northing,As-built Z,Design Z,Difference,Result,Description\n");
            foreach (var r in _r.Rows)
                sb.Append(r.Point.Name).Append(',').Append(r.Point.E.ToString("F3", Inv)).Append(',').Append(r.Point.N.ToString("F3", Inv)).Append(',')
                  .Append(r.Point.Z.ToString("F3", Inv)).Append(',').Append(double.IsNaN(r.DesignZ) ? "" : r.DesignZ.ToString("F3", Inv)).Append(',')
                  .Append(double.IsNaN(r.DesignZ) ? "" : r.Diff.ToString("F3", Inv)).Append(',').Append(r.Status).Append(',').Append((r.Point.Desc ?? "").Replace(",", " ")).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private void Pdf()
        {
            var path = UiKit.BrowseSave(this, "PDF|*.pdf", FileNameFor(".pdf"), out _);
            if (path == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var st = Settings.Current;
                var d = ReportFactory.LevelCheck(_r, _element.Text, (string)_mode.SelectedItem);
                ReportBuilder.SavePdf(d, path);
                WriteCsv(Path.ChangeExtension(path, ".csv"));
                if (UiKit.Ask(Lang.T("ReportSavedOpen") + "\n" + path)) UiKit.OpenUrl(path);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }
    }
}
