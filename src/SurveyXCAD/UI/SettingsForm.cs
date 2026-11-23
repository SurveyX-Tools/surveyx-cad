using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SurveyX.Core;

namespace SurveyX.UI
{
    public sealed class SettingsForm : SxForm
    {
        private readonly ComboBox _cboLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly NumericUpDown _numH = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.01m, Maximum = 100, Increment = 0.1m, Width = 90 };
        private readonly NumericUpDown _numDec = new NumericUpDown { Minimum = 0, Maximum = 8, Width = 90 };
        private readonly NumericUpDown _numRows = new NumericUpDown { Minimum = 5, Maximum = 500, Width = 90 };
        private readonly TextBox _txtPrefix = new TextBox { Width = 120 };
        private readonly CheckBox _chkUpd = new CheckBox { AutoSize = true };
        private readonly TextBox _company = new TextBox { Width = 320 }, _project = new TextBox { Width = 320 },
            _prepared = new TextBox { Width = 320 }, _submitted = new TextBox { Width = 320 }, _logo = new TextBox { Width = 250, ReadOnly = true };
        private readonly PictureBox _logoPreview = new PictureBox { Width = 120, Height = 60, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

        public SettingsForm() : base("SettingsTitle", "HelpSettings")
        {
            var st = Settings.Current;
            Size = new Size(640, 700);
            MinimumSize = new Size(600, 640);

            _cboLang.Items.Add("العربية");
            _cboLang.Items.Add("English");
            _cboLang.SelectedIndex = st.Language == "en" ? 1 : 0;
            _numH.Value = (decimal)Math.Max(0.01, Math.Min(100, st.TextHeight));
            _numDec.Value = Math.Max(0, Math.Min(8, st.Decimals));
            _numRows.Value = Math.Max(5, Math.Min(500, st.TableRowsPerColumn));
            _txtPrefix.Text = st.LayerPrefix;
            _chkUpd.Text = Lang.T("CheckUpdates");
            _chkUpd.Checked = st.CheckUpdates;
            _company.Text = st.CompanyName; _project.Text = st.ProjectName; _prepared.Text = st.PreparedBy; _submitted.Text = st.SubmittedTo;
            SetLogo(st.LogoPath);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int r = 0;
            void Row(string key, Control c) { t.Controls.Add(UiKit.Lbl(key), 0, r); t.Controls.Add(c, 1, r++); }
            void Section(string key) { var l = UiKit.Lbl(key, true); l.ForeColor = Accent; l.Margin = new Padding(3, 14, 3, 4); t.Controls.Add(l, 0, r); t.SetColumnSpan(l, 2); r++; }

            Section("SecGeneral");
            Row("Language", _cboLang);
            Row("TextHeight", _numH);
            Row("Decimals", _numDec);
            Row("LayerPrefix", _txtPrefix);
            Row("RowsPerTable", _numRows);
            t.Controls.Add(_chkUpd, 1, r++);

            Section("SecReports");
            Row("CompanyName", _company);
            var logoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var bLogo = UiKit.Btn("Browse"); bLogo.Click += (s, e) =>
            {
                var f = UiKit.BrowseOpen(this, "Images|*.png;*.jpg;*.jpeg;*.bmp");
                if (f != null) SetLogo(f);
            };
            var bNoLogo = UiKit.Btn("Remove"); bNoLogo.Click += (s, e) => SetLogo("");
            logoRow.Controls.AddRange(new Control[] { _logoPreview, bLogo, bNoLogo });
            Row("CompanyLogo", logoRow);
            Row("ProjectName", _project);
            Row("PreparedBy", _prepared);
            Row("SubmittedTo", _submitted);

            var ok = UiKit.Btn("Save", true);
            var cancel = UiKit.Btn("Cancel");
            ok.Click += (s, e) =>
            {
                string prefix = (_txtPrefix.Text ?? "").Trim();
                if (prefix.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' }) >= 0)
                {
                    UiKit.Error(Lang.T("BadPrefix")); return;
                }
                st.Language = _cboLang.SelectedIndex == 1 ? "en" : "ar";
                st.TextHeight = (double)_numH.Value;
                st.Decimals = (int)_numDec.Value;
                st.LayerPrefix = prefix;
                st.TableRowsPerColumn = (int)_numRows.Value;
                st.CheckUpdates = _chkUpd.Checked;
                st.CompanyName = _company.Text.Trim();
                st.ProjectName = _project.Text.Trim();
                st.PreparedBy = _prepared.Text.Trim();
                st.SubmittedTo = _submitted.Text.Trim();
                st.LogoPath = _logo.Text;
                st.Save();
                Lang.Set(st.Language);
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => Close();
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(t);
            Controls.Add(Header("SettingsTitle"));
            Controls.Add(UiKit.ButtonBar(ok, cancel));
        }

        private void SetLogo(string path)
        {
            var old = _logoPreview.Image;
            _logoPreview.Image = null;
            old?.Dispose();
            _logo.Text = "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                // copy the logo next to the settings so the report keeps working if the original file moves
                Directory.CreateDirectory(Settings.Folder);
                string dest = Path.Combine(Settings.Folder, "logo" + Path.GetExtension(path).ToLowerInvariant());
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dest, true);
                using (var fs = new FileStream(dest, FileMode.Open, FileAccess.Read))
                using (var img = Image.FromStream(fs))
                    _logoPreview.Image = new Bitmap(img);
                _logo.Text = dest;
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
        }
    }
}
