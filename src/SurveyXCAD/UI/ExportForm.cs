using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SurveyX.Core;

namespace SurveyX.UI
{
    public sealed class ExportForm : SxForm
    {
        private readonly IList<SurveyPoint> _pts;
        private readonly ComboBox _cboFmt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox _cboOrder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown _numDec = new NumericUpDown { Minimum = 0, Maximum = 8, Width = 70 };
        private readonly ComboBox _cboCrs;
        private readonly CheckBox _chkHeader = new CheckBox { AutoSize = true, Checked = true };

        public ExportForm(IList<SurveyPoint> pts) : base("ExportTitle", "HelpExport")
        {
            _pts = pts;
            Size = new Size(620, 420);
            MinimumSize = new Size(560, 400);
            _cboCrs = UiKit.CrsCombo(Settings.Current.LastTargetCrs, includeGeographic: false);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var count = new Label { Text = string.Format(Lang.T("PointsSelected"), pts.Count), AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(3, 3, 3, 10) };
            t.Controls.Add(count, 0, 0); t.SetColumnSpan(count, 2);

            foreach (var f in new[] { "CSV", "TXT (Tab)", "LandXML", "KML (Google Earth)", "GeoJSON" }) _cboFmt.Items.Add(f);
            _cboFmt.SelectedIndex = 0;
            _cboFmt.SelectedIndexChanged += (s, e) => UpdateEnabled();
            t.Controls.Add(UiKit.Lbl("Format"), 0, 1); t.Controls.Add(_cboFmt, 1, 1);

            foreach (var o in new[] { "PNEZD", "PENZD", "PNEZ", "PENZ", "NEZ", "ENZ" }) _cboOrder.Items.Add(o);
            _cboOrder.SelectedIndex = 0;
            t.Controls.Add(UiKit.Lbl("ColumnOrder"), 0, 2); t.Controls.Add(_cboOrder, 1, 2);

            _numDec.Value = Math.Max(0, Math.Min(8, Settings.Current.Decimals));
            t.Controls.Add(UiKit.Lbl("Decimals"), 0, 3); t.Controls.Add(_numDec, 1, 3);

            _chkHeader.Text = Lang.T("WriteHeader");
            t.Controls.Add(_chkHeader, 1, 4);

            t.Controls.Add(UiKit.Lbl("DrawingCrs"), 0, 5); t.Controls.Add(_cboCrs, 1, 5);
            var hint = new Label { Text = Lang.T("DrawingCrsHint"), AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) };
            t.Controls.Add(hint, 1, 6);

            UiKit.EndRows(t);
            var save = UiKit.Btn("SaveFile", true);
            var cancel = UiKit.Btn("Cancel");
            save.Click += (s, e) => DoSave();
            cancel.Click += (s, e) => Close();
            AcceptButton = save; CancelButton = cancel;

            Controls.Add(t);
            Controls.Add(Header("ExportTitle"));
            Controls.Add(UiKit.ButtonBar(save, cancel));
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            int i = _cboFmt.SelectedIndex;
            _cboOrder.Enabled = _chkHeader.Enabled = i <= 1;
            _cboCrs.Enabled = i >= 3;
            _numDec.Enabled = i <= 2;
        }

        private void DoSave()
        {
            var fmt = (ExportFormat)_cboFmt.SelectedIndex;
            string ext = new[] { ".csv", ".txt", ".xml", ".kml", ".geojson" }[_cboFmt.SelectedIndex];
            string filter = new[] { "CSV|*.csv", "TXT|*.txt", "LandXML|*.xml", "KML|*.kml", "GeoJSON|*.geojson" }[_cboFmt.SelectedIndex];
            string path = UiKit.BrowseSave(this, filter, "points" + ext, out _);
            if (path == null) return;
            try
            {
                CoordinateTransformer toWgs = null;
                if (fmt == ExportFormat.Kml || fmt == ExportFormat.GeoJson)
                {
                    var crs = (Crs)_cboCrs.SelectedItem;
                    toWgs = new CoordinateTransformer(crs, CrsCatalog.Wgs84Geo);
                    Settings.Current.LastTargetCrs = crs.Id;
                }
                PointWriters.Write(path, fmt, _pts, (string)_cboOrder.SelectedItem, (int)_numDec.Value, toWgs, _chkHeader.Checked);
                Settings.Current.Save();
                UiKit.Info(string.Format(Lang.T("Exported"), _pts.Count) + "\n" + path);
                Close();
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
        }
    }
}
