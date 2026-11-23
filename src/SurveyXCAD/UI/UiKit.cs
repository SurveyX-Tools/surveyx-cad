using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SurveyX.Core;

namespace SurveyX.UI
{
    /// <summary>Shared look &amp; feel: Arabic RTL support, help button, colours, small factory helpers.</summary>
    public class SxForm : Form
    {
        public static readonly Color Accent = Brand.Navy;
        public static readonly Color Soft = Color.FromArgb(0xF1, 0xF6, 0xF8);

        protected readonly string HelpKey;

        public SxForm(string titleKey, string helpKey)
        {
            HelpKey = helpKey;
            Text = AppInfo.Name + " — " + Lang.T(titleKey);
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = Color.White;
            KeyPreview = true;
            if (Lang.IsArabic)
            {
                RightToLeft = RightToLeft.Yes;
                RightToLeftLayout = true;
            }
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F1) ShowHelp(); };
            if (Brand.Icon != null) { Icon = Brand.Icon; ShowIcon = true; }
        }

        public void ShowHelp() => UiKit.Info(Lang.T(HelpKey), Lang.T("Help"));

        /// <summary>Header strip with title and a ❓ help button.</summary>
        protected Control Header(string subtitleKey)
        {
            var wrap = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Accent };
            var strip = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Brand.Teal };
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Accent, ColumnCount = 3, Padding = new Padding(10, 6, 12, 6) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            var logo = new PictureBox { Image = Brand.LogoSized(40), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.None, Margin = new Padding(0) };
            var lbl = new Label
            {
                Text = Lang.T(subtitleKey), ForeColor = Color.White, Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false,
            };
            var help = new Button
            {
                Text = "؟", Width = 38, Height = 38, FlatStyle = FlatStyle.Flat, ForeColor = Brand.TealLight, BackColor = Accent,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold), Cursor = Cursors.Hand, Anchor = AnchorStyles.None,
            };
            help.FlatAppearance.BorderColor = Brand.Teal;
            help.Click += (s, e) => ShowHelp();
            new ToolTip().SetToolTip(help, Lang.T("HelpTip"));
            p.Controls.Add(logo, 0, 0);
            p.Controls.Add(lbl, 1, 0);
            p.Controls.Add(help, 2, 0);
            wrap.Controls.Add(p);
            wrap.Controls.Add(strip);
            return wrap;
        }
    }

    public static class UiKit
    {
        public static MessageBoxOptions Rtl => Lang.IsArabic ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : (MessageBoxOptions)0;

        public static void Info(string msg, string title = null) =>
            MessageBox.Show(msg, title ?? AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);

        public static void Error(string msg) =>
            MessageBox.Show(msg, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);

        public static bool Ask(string msg) =>
            MessageBox.Show(msg, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, Rtl) == DialogResult.Yes;

        /// <summary>Schedule stored in the current drawing, or null (also null when running outside AutoCAD).</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static P6Schedule TryLoadSchedule()
        {
            try { return LoadScheduleCore(); }
            catch (Exception) { return null; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static P6Schedule LoadScheduleCore() => SurveyX.Cad.P6Store.Load();

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Error(ex.Message); }
        }

        public static Label Lbl(string key, bool bold = false) => new Label
        {
            Text = Lang.T(key), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 7, 3, 3), Font = bold ? new Font("Segoe UI", 9.75f, FontStyle.Bold) : null, UseMnemonic = false,
        };

        /// <summary>Label + control kept together on one line (never split by wrapping).</summary>
        public static FlowLayoutPanel Pair(string key, Control c)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 10, 0) };
            f.Controls.Add(Lbl(key));
            f.Controls.Add(c);
            return f;
        }

        /// <summary>Adds an empty last row that takes the spare height, so real rows keep their natural size and alignment.</summary>
        public static void EndRows(TableLayoutPanel t)
        {
            int used = t.Controls.Cast<Control>().Select(ctl => t.GetRow(ctl)).DefaultIfEmpty(-1).Max() + 1;
            t.RowCount = Math.Max(t.RowCount, used) + 1;
            for (int i = 0; i < t.RowCount - 1; i++)
                if (i < t.RowStyles.Count) t.RowStyles[i] = new RowStyle(SizeType.AutoSize); else t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(new Panel { Height = 1, Margin = new Padding(0) }, 0, t.RowCount - 1);
        }

        public static Button Btn(string key, bool primary = false)
        {
            var b = new Button
            {
                Text = Lang.T(key), AutoSize = true, MinimumSize = new Size(96, 32), Padding = new Padding(8, 2, 8, 2),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(4), UseMnemonic = false,
            };
            if (primary) { b.BackColor = SxForm.Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = SxForm.Accent; }
            else { b.BackColor = Color.White; b.FlatAppearance.BorderColor = Color.Silver; }
            return b;
        }

        public static ComboBox CrsCombo(string selectedId, bool includeGeographic = true)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 520 };
            foreach (var c in CrsCatalog.All.Where(c => includeGeographic || c.Kind == CrsKind.Projected)) cb.Items.Add(c);
            var sel = CrsCatalog.Find(selectedId);
            cb.SelectedItem = sel != null && cb.Items.Contains(sel) ? (object)sel : cb.Items[0];
            return cb;
        }

        public static FlowLayoutPanel ButtonBar(params Control[] buttons)
        {
            var f = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                Padding = new Padding(8), BackColor = SxForm.Soft,
            };
            f.Controls.AddRange(buttons);
            return f;
        }

        public static string BrowseOpen(IWin32Window owner, string filter)
        {
            using (var d = new OpenFileDialog { Filter = filter, InitialDirectory = Settings.Current.LastFolder })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                Settings.Current.LastFolder = System.IO.Path.GetDirectoryName(d.FileName);
                return d.FileName;
            }
        }

        public static string BrowseSave(IWin32Window owner, string filter, string defName, out int filterIndex)
        {
            filterIndex = 1;
            using (var d = new SaveFileDialog { Filter = filter, FileName = defName, InitialDirectory = Settings.Current.LastFolder })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                Settings.Current.LastFolder = System.IO.Path.GetDirectoryName(d.FileName);
                filterIndex = d.FilterIndex;
                return d.FileName;
            }
        }
    }
}
