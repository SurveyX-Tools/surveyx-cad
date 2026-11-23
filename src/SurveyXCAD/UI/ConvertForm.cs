using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.UI
{
    /// <summary>Coordinate conversion: single point, whole file, or points in the drawing.</summary>
    public sealed class ConvertForm : SxForm
    {
        public bool TransformDrawing { get; private set; }
        public Crs SourceCrs => (Crs)_cboSrc.SelectedItem;
        public Crs TargetCrs => (Crs)_cboDst.SelectedItem;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly ComboBox _cboSrc, _cboDst;
        private readonly TextBox _txtX = new TextBox { Dock = DockStyle.Fill }, _txtY = new TextBox { Dock = DockStyle.Fill };
        private readonly Label _lblX = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _lblY = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        private readonly TextBox _txtOut = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.White,
            Font = new Font("Consolas", 11f), RightToLeft = RightToLeft.No, ScrollBars = ScrollBars.Vertical,
        };
        private readonly TextBox _txtIn = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
        private readonly TextBox _txtOutFile = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
        private readonly Label _lblFileStatus = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DarkGreen };

        public ConvertForm() : base("ConvertTitle", "HelpConvert")
        {
            var st = Settings.Current;
            Size = new Size(760, 600);
            MinimumSize = new Size(640, 520);
            _cboSrc = UiKit.CrsCombo(st.LastSourceCrs);
            _cboDst = UiKit.CrsCombo(st.LastTargetCrs);
            _cboSrc.SelectedIndexChanged += (s, e) => UpdateLabels();

            // CRS selectors
            var crs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(10, 8, 10, 4) };
            crs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            crs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            crs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var swap = UiKit.Btn("Swap");
            swap.Click += (s, e) => { var a = _cboSrc.SelectedItem; _cboSrc.SelectedItem = _cboDst.SelectedItem; _cboDst.SelectedItem = a; };
            crs.Controls.Add(UiKit.Lbl("From"), 0, 0); crs.Controls.Add(_cboSrc, 1, 0); crs.Controls.Add(swap, 2, 0);
            crs.SetRowSpan(swap, 2);
            crs.Controls.Add(UiKit.Lbl("To"), 0, 1); crs.Controls.Add(_cboDst, 1, 1);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            if (Lang.IsArabic) { tabs.RightToLeftLayout = true; }
            tabs.TabPages.Add(SinglePage());
            tabs.TabPages.Add(FilePage());
            tabs.TabPages.Add(DrawingPage());

            var close = UiKit.Btn("Close");
            close.Click += (s, e) => { Save(); DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = close;

            Controls.Add(tabs);
            Controls.Add(crs);
            Controls.Add(Header("ConvertTitle"));
            Controls.Add(UiKit.ButtonBar(close));
            UpdateLabels();
        }

        private void Save()
        {
            Settings.Current.LastSourceCrs = SourceCrs.Id;
            Settings.Current.LastTargetCrs = TargetCrs.Id;
            Settings.Current.Save();
        }

        private TabPage SinglePage()
        {
            var page = new TabPage(Lang.T("TabSingle")) { Padding = new Padding(8), BackColor = Color.White };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(_lblX, 0, 0); t.Controls.Add(_txtX, 1, 0);
            t.Controls.Add(_lblY, 0, 1); t.Controls.Add(_txtY, 1, 1);
            _txtX.RightToLeft = RightToLeft.No; _txtY.RightToLeft = RightToLeft.No;

            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var bConv = UiKit.Btn("Convert", true);
            var bPick = UiKit.Btn("PickInDrawing");
            var bCopy = UiKit.Btn("Copy");
            bConv.Click += (s, e) => ConvertSingle();
            bPick.Click += (s, e) => PickPoint();
            bCopy.Click += (s, e) => { if (_txtOut.Text.Length > 0) Clipboard.SetText(_txtOut.Text); };
            bar.Controls.AddRange(new Control[] { bConv, bPick, bCopy });
            t.Controls.Add(bar, 0, 2); t.SetColumnSpan(bar, 2);
            t.Controls.Add(_txtOut, 0, 3); t.SetColumnSpan(_txtOut, 2);
            page.Controls.Add(t);
            _txtX.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ConvertSingle(); };
            _txtY.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ConvertSingle(); };
            return page;
        }

        private void UpdateLabels()
        {
            bool geo = SourceCrs.Kind == CrsKind.Geographic;
            _lblX.Text = geo ? Lang.T("Longitude") : Lang.T("Easting");
            _lblY.Text = geo ? Lang.T("Latitude") : Lang.T("Northing");
        }

        private void ConvertSingle()
        {
            bool geo = SourceCrs.Kind == CrsKind.Geographic;
            double x, y;
            bool ok = geo ? AngleFormat.TryParse(_txtX.Text, out x) & AngleFormat.TryParse(_txtY.Text, out y)
                          : PointFileReader.TryNum(_txtX.Text, out x) & PointFileReader.TryNum(_txtY.Text, out y);
            if (!ok) { UiKit.Error(Lang.T("BadNumber")); return; }
            var tr = new CoordinateTransformer(SourceCrs, TargetCrs);
            tr.Transform(x, y, out double xo, out double yo);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(TargetCrs.NameEn + "  [" + TargetCrs.Id + "]");
            sb.AppendLine(new string('-', 48));
            if (TargetCrs.Kind == CrsKind.Geographic)
            {
                sb.AppendLine("Latitude  : " + yo.ToString("F9", Inv) + "   " + AngleFormat.ToDms(yo, true));
                sb.AppendLine("Longitude : " + xo.ToString("F9", Inv) + "   " + AngleFormat.ToDms(xo, false));
            }
            else
            {
                sb.AppendLine("Easting  E : " + xo.ToString("F4", Inv));
                sb.AppendLine("Northing N : " + yo.ToString("F4", Inv));
                // scale factor / convergence at the point
                var toGeo = new CoordinateTransformer(TargetCrs, new Crs("tmp", "", "", "", TargetCrs.Datum, null));
                toGeo.Transform(xo, yo, out double lon, out double lat);
                TargetCrs.Projection.ScaleAndConvergence(lat * Math.PI / 180, lon * Math.PI / 180, out double k, out double g);
                sb.AppendLine("Scale factor k : " + k.ToString("F8", Inv));
                sb.AppendLine("Convergence    : " + g.ToString("F6", Inv) + "°");
            }
            if (TargetCrs.Kind == CrsKind.Projected || SourceCrs.Kind == CrsKind.Projected)
            {
                var g84 = new CoordinateTransformer(SourceCrs, CrsCatalog.Wgs84Geo);
                g84.Transform(x, y, out double lo2, out double la2);
                sb.AppendLine();
                sb.AppendLine("WGS 84: " + la2.ToString("F8", Inv) + ", " + lo2.ToString("F8", Inv) + "  (Google Maps)");
            }
            _txtOut.Text = sb.ToString();
            Save();
        }

        private void PickPoint()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            using (ed.StartUserInteraction(this))
            {
                var r = ed.GetPoint("\nPick a point: ");
                if (r.Status != PromptStatus.OK) return;
                var w = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                _txtX.Text = w.X.ToString("F4", Inv);
                _txtY.Text = w.Y.ToString("F4", Inv);
            }
            if (SourceCrs.Kind == CrsKind.Geographic)
            {
                UiKit.Info(Lang.T("PickNeedsProjected"));
                return;
            }
            ConvertSingle();
        }

        private TabPage FilePage()
        {
            var page = new TabPage(Lang.T("TabFile")) { Padding = new Padding(8), BackColor = Color.White };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var bIn = UiKit.Btn("Browse");
            var bOut = UiKit.Btn("Browse");
            bIn.Click += (s, e) =>
            {
                var f = UiKit.BrowseOpen(this, PointFileReader.OpenFilter);
                if (f == null) return;
                _txtIn.Text = f;
                _txtOutFile.Text = Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_" + TargetCrs.Id.Replace(":", "") + ".csv");
            };
            bOut.Click += (s, e) =>
            {
                var f = UiKit.BrowseSave(this, "CSV|*.csv", Path.GetFileName(_txtOutFile.Text), out _);
                if (f != null) _txtOutFile.Text = f;
            };
            t.Controls.Add(UiKit.Lbl("InputFile"), 0, 0); t.Controls.Add(_txtIn, 1, 0); t.Controls.Add(bIn, 2, 0);
            t.Controls.Add(UiKit.Lbl("OutputFile"), 0, 1); t.Controls.Add(_txtOutFile, 1, 1); t.Controls.Add(bOut, 2, 1);
            var hint = new Label { Text = Lang.T("FileConvertHint"), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(640, 0), Margin = new Padding(3, 10, 3, 6) };
            t.Controls.Add(hint, 0, 2); t.SetColumnSpan(hint, 3);
            var go = UiKit.Btn("ConvertFile", true);
            go.Click += (s, e) => ConvertFile();
            t.Controls.Add(go, 0, 3); t.SetColumnSpan(go, 3);
            _lblFileStatus.Height = 60;
            t.Controls.Add(_lblFileStatus, 0, 4); t.SetColumnSpan(_lblFileStatus, 3);
            UiKit.EndRows(t);
            page.Controls.Add(t);
            return page;
        }

        private void ConvertFile()
        {
            if (string.IsNullOrEmpty(_txtIn.Text) || string.IsNullOrEmpty(_txtOutFile.Text)) { UiKit.Error(Lang.T("ChooseFiles")); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                var res = PointFileReader.Read(_txtIn.Text);
                var src = res.IsGeographic ? CrsCatalog.Wgs84Geo : SourceCrs;
                var t = new CoordinateTransformer(src, TargetCrs);
                foreach (var p in res.Points)
                {
                    t.Transform(p.E, p.N, out double e, out double n);
                    p.E = e; p.N = n;
                }
                bool geoOut = TargetCrs.Kind == CrsKind.Geographic;
                PointWriters.WriteDelimited(_txtOutFile.Text, res.Points, "PNEZD", geoOut ? 9 : Math.Max(3, Settings.Current.Decimals), ",", true, geoOut);
                _lblFileStatus.ForeColor = Color.DarkGreen;
                _lblFileStatus.Text = string.Format(Lang.T("FileConverted"), res.Points.Count) + "\n" + _txtOutFile.Text;
                Save();
            }
            catch (Exception ex)
            {
                _lblFileStatus.ForeColor = Color.DarkRed;
                _lblFileStatus.Text = ex.Message;
            }
            finally { Cursor = Cursors.Default; }
        }

        private TabPage DrawingPage()
        {
            var page = new TabPage(Lang.T("TabDrawing")) { Padding = new Padding(12), BackColor = Color.White };
            var lbl = new Label { Text = Lang.T("DrawingConvertHint"), Dock = DockStyle.Top, AutoSize = false, Height = 110 };
            var go = UiKit.Btn("SelectAndTransform", true);
            go.Dock = DockStyle.Top;
            go.Click += (s, e) =>
            {
                if (ReferenceEquals(SourceCrs, TargetCrs)) { UiKit.Error(Lang.T("SameCrs")); return; }
                if (SourceCrs.Kind == CrsKind.Geographic || TargetCrs.Kind == CrsKind.Geographic) { UiKit.Error(Lang.T("DrawingNeedsProjected")); return; }
                Save();
                TransformDrawing = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            page.Controls.Add(go);
            page.Controls.Add(lbl);
            return page;
        }
    }
}
