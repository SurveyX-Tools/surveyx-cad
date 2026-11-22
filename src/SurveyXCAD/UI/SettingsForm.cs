using System;
using System.Drawing;
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

        public SettingsForm() : base("SettingsTitle", "HelpSettings")
        {
            var st = Settings.Current;
            Size = new Size(520, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            _cboLang.Items.Add("العربية");
            _cboLang.Items.Add("English");
            _cboLang.SelectedIndex = st.Language == "en" ? 1 : 0;
            _numH.Value = (decimal)Math.Max(0.01, Math.Min(100, st.TextHeight));
            _numDec.Value = Math.Max(0, Math.Min(8, st.Decimals));
            _numRows.Value = Math.Max(5, Math.Min(500, st.TableRowsPerColumn));
            _txtPrefix.Text = st.LayerPrefix;
            _chkUpd.Text = Lang.T("CheckUpdates");
            _chkUpd.Checked = st.CheckUpdates;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int r = 0;
            t.Controls.Add(UiKit.Lbl("Language"), 0, r); t.Controls.Add(_cboLang, 1, r++);
            t.Controls.Add(UiKit.Lbl("TextHeight"), 0, r); t.Controls.Add(_numH, 1, r++);
            t.Controls.Add(UiKit.Lbl("Decimals"), 0, r); t.Controls.Add(_numDec, 1, r++);
            t.Controls.Add(UiKit.Lbl("LayerPrefix"), 0, r); t.Controls.Add(_txtPrefix, 1, r++);
            t.Controls.Add(UiKit.Lbl("RowsPerTable"), 0, r); t.Controls.Add(_numRows, 1, r++);
            t.Controls.Add(_chkUpd, 1, r++);

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
    }
}
