using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SurveyX.Cad;
using SurveyX.Core;

namespace SurveyX.UI
{
    public sealed class AboutForm : SxForm
    {
        public AboutForm() : base("AboutTitle", "HelpAbout")
        {
            Size = new Size(640, 560);
            MinimumSize = new Size(560, 480);

            var body = new TextBox
            {
                Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10.5f),
                Text = AppInfo.Name + "  v" + AppInfo.Version + Environment.NewLine +
                       Lang.T("By") + " " + AppInfo.Author + Environment.NewLine +
                       Lang.T("FreeForever") + Environment.NewLine + Environment.NewLine +
                       Lang.T("CommandList").Replace("\n", Environment.NewLine),
            };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            pad.Controls.Add(body);

            var check = UiKit.Btn("CheckNow", true);
            var page = UiKit.Btn("DownloadPage");
            var close = UiKit.Btn("Close");
            check.Click += async (s, e) =>
            {
                check.Enabled = false;
                try
                {
                    string latest = await Task.Run(() => Updater.GetLatestVersionAsync());
                    if (latest == null) UiKit.Info(Lang.T("NoReleaseYet"));
                    else if (Updater.IsNewer(latest, AppInfo.Version))
                    {
                        if (UiKit.Ask(string.Format(Lang.T("UpdateAvailable"), latest))) UiKit.OpenUrl(AppInfo.ReleasesUrl);
                    }
                    else UiKit.Info(Lang.T("UpToDate"));
                }
                catch (Exception ex) { UiKit.Error(Lang.T("UpdateCheckFailed") + "\n" + ex.Message); }
                finally { check.Enabled = true; }
            };
            page.Click += (s, e) => UiKit.OpenUrl(AppInfo.ReleasesUrl);
            close.Click += (s, e) => Close();
            CancelButton = close;

            Controls.Add(pad);
            Controls.Add(Header("AboutTitle"));
            Controls.Add(UiKit.ButtonBar(close, page, check));
        }
    }
}
