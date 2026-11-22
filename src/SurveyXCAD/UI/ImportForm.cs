using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>Import points: any format, preview, duplicate/outlier check, optional coordinate conversion.</summary>
    public sealed class ImportForm : SxForm
    {
        public List<SurveyPoint> ResultPoints { get; private set; }
        public bool ZoomAfter => _chkZoom.Checked;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly TextBox _txtFile = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
        private readonly ComboBox _cboFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox _cboSrc, _cboDst;
        private readonly CheckBox _chkConvert = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _lblSummary = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(6), BackColor = Soft };
        private readonly NumericUpDown _numHeight = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.01m, Maximum = 100, Increment = 0.1m, Width = 80 };
        private readonly CheckBox _chk3D = new CheckBox { AutoSize = true }, _chkName = new CheckBox { AutoSize = true },
            _chkElev = new CheckBox { AutoSize = true }, _chkDesc = new CheckBox { AutoSize = true }, _chkZoom = new CheckBox { AutoSize = true, Checked = true };

        private List<SurveyPoint> _pts = new List<SurveyPoint>();
        private List<string[]> _raw;
        private bool _fileIsGeographic;
        private string _path;

        public ImportForm() : base("ImportTitle", "HelpImport")
        {
            var st = Settings.Current;
            Size = new Size(1000, 720);
            MinimumSize = new Size(820, 560);
            _cboSrc = UiKit.CrsCombo(st.LastSourceCrs);
            _cboDst = UiKit.CrsCombo(st.LastTargetCrs, includeGeographic: false);

            // ---------------- top: file / format / CRS
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(10, 8, 10, 4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            var btnBrowse = UiKit.Btn("Browse", true);
            btnBrowse.Click += (s, e) => Browse();
            top.Controls.Add(UiKit.Lbl("File"), 0, 0);
            top.Controls.Add(_txtFile, 1, 0);
            top.Controls.Add(btnBrowse, 2, 0);

            _cboFormat.Items.Add(Lang.T("AutoDetect"));
            foreach (var p in ColumnMap.Presets) _cboFormat.Items.Add(p);
            _cboFormat.SelectedIndex = 0;
            _cboFormat.SelectedIndexChanged += (s, e) => Reparse();
            top.Controls.Add(UiKit.Lbl("ColumnOrder"), 0, 1);
            top.Controls.Add(_cboFormat, 1, 1);
            var fmtHint = new Label { Text = Lang.T("ColumnOrderHint"), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 7, 3, 3) };
            top.Controls.Add(fmtHint, 2, 1);
            top.SetColumnSpan(fmtHint, 2);

            top.Controls.Add(UiKit.Lbl("FileCrs"), 0, 2);
            top.Controls.Add(_cboSrc, 1, 2);
            _chkConvert.Text = Lang.T("ConvertTo");
            _chkConvert.CheckedChanged += (s, e) => _cboDst.Enabled = _chkConvert.Checked;
            top.Controls.Add(_chkConvert, 2, 2);
            top.Controls.Add(_cboDst, 3, 2);
            _cboDst.Enabled = false;
            _cboSrc.SelectedIndexChanged += (s, e) => { Analyze(); };

            // ---------------- grid
            SetupGrid();

            // ---------------- right panel: issues + options
            var side = new TableLayoutPanel { Dock = DockStyle.Right, Width = 270, ColumnCount = 1, Padding = new Padding(6), BackColor = Soft, AutoScroll = true };
            side.Controls.Add(UiKit.Lbl("Check", true));
            _lblSummary.Height = 170;
            side.Controls.Add(_lblSummary);
            var bExDup = UiKit.Btn("ExcludeDup"); bExDup.Dock = DockStyle.Top;
            var bExOut = UiKit.Btn("ExcludeOut"); bExOut.Dock = DockStyle.Top;
            var bAll = UiKit.Btn("IncludeAll"); bAll.Dock = DockStyle.Top;
            bExDup.Click += (s, e) => Exclude(PointIssue.DuplicateName | PointIssue.DuplicateCoords, keepFirst: true);
            bExOut.Click += (s, e) => Exclude(PointIssue.OutlierZ | PointIssue.OutlierXY, keepFirst: false);
            bAll.Click += (s, e) => { foreach (var p in _pts) p.Include = true; _grid.Invalidate(); UpdateSummary(); };
            side.Controls.Add(bExDup); side.Controls.Add(bExOut); side.Controls.Add(bAll);

            side.Controls.Add(UiKit.Lbl("DrawOptions", true));
            var hRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            hRow.Controls.Add(UiKit.Lbl("TextHeight")); hRow.Controls.Add(_numHeight);
            side.Controls.Add(hRow);
            _numHeight.Value = (decimal)Math.Max(0.01, Math.Min(100, st.TextHeight));
            _chk3D.Text = Lang.T("Place3D"); _chk3D.Checked = st.Place3D;
            _chkName.Text = Lang.T("ShowName"); _chkName.Checked = st.ShowName;
            _chkElev.Text = Lang.T("ShowElev"); _chkElev.Checked = st.ShowElevation;
            _chkDesc.Text = Lang.T("ShowDesc"); _chkDesc.Checked = st.ShowDesc;
            _chkZoom.Text = Lang.T("ZoomAfter");
            foreach (var c in new Control[] { _chk3D, _chkName, _chkElev, _chkDesc, _chkZoom }) side.Controls.Add(c);

            // ---------------- bottom
            var btnDraw = UiKit.Btn("DrawPoints", true);
            var btnCancel = UiKit.Btn("Cancel");
            btnDraw.Click += (s, e) => Draw();
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = btnDraw; CancelButton = btnCancel;
            var bottom = UiKit.ButtonBar(btnDraw, btnCancel);

            Controls.Add(_grid);
            Controls.Add(side);
            Controls.Add(top);
            Controls.Add(Header("ImportTitle"));
            Controls.Add(bottom);
            UpdateSummary();
        }

        private void SetupGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.VirtualMode = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RightToLeft = RightToLeft.No; // numbers read left-to-right
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", FillWeight = 18 });
            foreach (var h in new[] { "#", Lang.T("ColName"), Lang.T("ColE"), Lang.T("ColN"), Lang.T("ColZ"), Lang.T("ColDesc"), Lang.T("ColIssue") })
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns[1].FillWeight = 30;
            _grid.Columns[7].FillWeight = 140;
            _grid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex >= _pts.Count) return;
                var p = _pts[e.RowIndex];
                int dec = IsGeoSource ? 8 : 3;
                switch (e.ColumnIndex)
                {
                    case 0: e.Value = p.Include; break;
                    case 1: e.Value = e.RowIndex + 1; break;
                    case 2: e.Value = p.Name; break;
                    case 3: e.Value = p.E.ToString("F" + dec, Inv); break;
                    case 4: e.Value = p.N.ToString("F" + dec, Inv); break;
                    case 5: e.Value = p.HasZ ? p.Z.ToString("F3", Inv) : "—"; break;
                    case 6: e.Value = p.Desc; break;
                    case 7: e.Value = PointAnalysis.IssueText(p.Issues); break;
                }
            };
            _grid.CellValuePushed += (s, e) =>
            {
                if (e.ColumnIndex == 0 && e.RowIndex < _pts.Count) { _pts[e.RowIndex].Include = (bool)e.Value; UpdateSummary(); }
            };
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex >= _pts.Count) return;
                var p = _pts[e.RowIndex];
                if (!p.Include) { e.CellStyle.ForeColor = Color.Gray; e.CellStyle.BackColor = Color.FromArgb(0xEE, 0xEE, 0xEE); }
                else if ((p.Issues & (PointIssue.OutlierZ | PointIssue.OutlierXY)) != 0) e.CellStyle.BackColor = Color.FromArgb(0xFF, 0xD6, 0xD6);
                else if ((p.Issues & (PointIssue.DuplicateName | PointIssue.DuplicateCoords)) != 0) e.CellStyle.BackColor = Color.FromArgb(0xFF, 0xF3, 0xC4);
            };
        }

        private bool IsGeoSource => _fileIsGeographic || ((Crs)_cboSrc.SelectedItem).Kind == CrsKind.Geographic;

        private void Browse()
        {
            string f = UiKit.BrowseOpen(this, PointFileReader.OpenFilter);
            if (f == null) return;
            _path = f;
            _txtFile.Text = f;
            LoadFile();
        }

        private void LoadFile()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                ColumnMap map = _cboFormat.SelectedIndex > 0 ? ColumnMap.FromPreset((string)_cboFormat.SelectedItem) : null;
                var res = PointFileReader.Read(_path, map);
                _raw = res.RawRows;
                _fileIsGeographic = res.IsGeographic;
                _cboFormat.Enabled = PointFileReader.IsTabular(_path);
                if (res.IsGeographic)
                {
                    _cboSrc.SelectedItem = CrsCatalog.Wgs84Geo;
                    _chkConvert.Checked = true;
                }
                SetPoints(res);
            }
            catch (Exception ex) { UiKit.Error(Lang.T("ReadError") + "\n" + ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void Reparse()
        {
            if (_raw == null) { if (_path != null) LoadFile(); return; }
            ColumnMap map = _cboFormat.SelectedIndex > 0 ? ColumnMap.FromPreset((string)_cboFormat.SelectedItem) : null;
            SetPoints(PointFileReader.ParseRows(_raw, map));
        }

        private ReadResult _last;

        private void SetPoints(ReadResult res)
        {
            _last = res;
            _pts = res.Points;
            Analyze();
        }

        private void Analyze()
        {
            if (_pts == null) return;
            PointAnalysis.Analyze(_pts, 0.001, IsGeoSource);
            _grid.RowCount = 0;
            _grid.RowCount = _pts.Count;
            _grid.Invalidate();
            UpdateSummary();
        }

        private void Exclude(PointIssue mask, bool keepFirst)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenXY = new HashSet<(long, long)>();
            foreach (var p in _pts)
            {
                if ((p.Issues & mask) == 0) continue;
                if (keepFirst)
                {
                    bool firstName = (p.Issues & PointIssue.DuplicateName) == 0 || seenNames.Add(p.Name ?? "");
                    var key = ((long)Math.Round(p.E * 1000), (long)Math.Round(p.N * 1000));
                    bool firstXY = (p.Issues & PointIssue.DuplicateCoords) == 0 || seenXY.Add(key);
                    if (firstName && firstXY) continue;
                }
                p.Include = false;
            }
            _grid.Invalidate();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_pts == null || _pts.Count == 0)
            {
                _lblSummary.Text = Lang.T("NoFileYet");
                return;
            }
            int inc = _pts.Count(p => p.Include);
            int dupN = _pts.Count(p => (p.Issues & PointIssue.DuplicateName) != 0);
            int dupXY = _pts.Count(p => (p.Issues & PointIssue.DuplicateCoords) != 0);
            int outZ = _pts.Count(p => (p.Issues & PointIssue.OutlierZ) != 0);
            int outXY = _pts.Count(p => (p.Issues & PointIssue.OutlierXY) != 0);
            var zs = _pts.Where(p => p.HasZ).Select(p => p.Z).ToList();
            string fmt = IsGeoSource ? "F6" : "F2";
            _lblSummary.Text =
                string.Format(Lang.T("SumTotal"), _pts.Count, inc) + "\n" +
                (_last?.Map != null ? Lang.T("SumFormat") + " " + _last.Map.Label + "\n" : "") +
                string.Format(Lang.T("SumDup"), dupN, dupXY) + "\n" +
                string.Format(Lang.T("SumOut"), outZ, outXY) + "\n" +
                (zs.Count > 0 ? string.Format(Lang.T("SumZ"), zs.Min().ToString("F3", Inv), zs.Max().ToString("F3", Inv)) + "\n" : "") +
                "E: " + _pts.Min(p => p.E).ToString(fmt, Inv) + " → " + _pts.Max(p => p.E).ToString(fmt, Inv) + "\n" +
                "N: " + _pts.Min(p => p.N).ToString(fmt, Inv) + " → " + _pts.Max(p => p.N).ToString(fmt, Inv) +
                (_last != null && _last.SkippedLines > 0 ? "\n" + string.Format(Lang.T("SkippedLines"), _last.SkippedLines) : "");
        }

        private void Draw()
        {
            var chosen = _pts.Where(p => p.Include).ToList();
            if (chosen.Count == 0) { UiKit.Error(Lang.T("NothingToDraw")); return; }
            var src = (Crs)_cboSrc.SelectedItem;
            if (_fileIsGeographic) src = CrsCatalog.Wgs84Geo;
            var dst = (Crs)_cboDst.SelectedItem;
            if (src.Kind == CrsKind.Geographic && !_chkConvert.Checked)
            {
                UiKit.Error(Lang.T("GeoNeedsConvert"));
                _chkConvert.Checked = true;
                return;
            }
            var output = new List<SurveyPoint>(chosen.Count);
            if (_chkConvert.Checked && !ReferenceEquals(src, dst))
            {
                var t = new CoordinateTransformer(src, dst);
                foreach (var p in chosen)
                {
                    var q = p.Clone();
                    t.Transform(p.E, p.N, out double e, out double n);
                    q.E = e; q.N = n;
                    output.Add(q);
                }
            }
            else output.AddRange(chosen.Select(p => p.Clone()));

            var st = Settings.Current;
            st.TextHeight = (double)_numHeight.Value;
            st.Place3D = _chk3D.Checked;
            st.ShowName = _chkName.Checked;
            st.ShowElevation = _chkElev.Checked;
            st.ShowDesc = _chkDesc.Checked;
            if (!_fileIsGeographic) st.LastSourceCrs = src.Id;
            st.LastTargetCrs = dst.Id;
            st.Save();
            ResultPoints = output;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
