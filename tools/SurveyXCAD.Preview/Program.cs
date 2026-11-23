using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SurveyX.Core;
using SurveyX.UI;

namespace SurveyX.Preview
{
    internal static class Program
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly StringBuilder Log = new StringBuilder();
        private static string _out;
        private static int _errors;

        [STAThread]
        private static int Main(string[] args)
        {
            _out = args.Length > 0 ? args[0] : "previews";
            Directory.CreateDirectory(_out);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            var st = Settings.Current;
            st.CompanyName = "Sample Contracting LLC";
            st.ProjectName = "Sample Road Project – Zone A";
            st.PreparedBy = "Ahmed Gamal Shalaby";
            st.SubmittedTo = "Site Engineer";
            st.LogoPath = MakeLogo();
            st.TextHeight = 0.5;
            Line("Screen: " + Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height);

            // ---------------------------------------------------------------- synthetic site
            var rnd = new Random(42);
            double E0 = 234000, N0 = 2705000;
            Func<double, double, double> ground = (x, y) => 10 + 0.8 * Math.Sin((x - E0) / 25) + 0.5 * Math.Cos((y - N0) / 18) + 0.002 * (x - E0);
            var ogl = new List<SurveyPoint>(100_000);
            int side = 316; // ≈ 100k points, ~1 m spacing
            for (int j = 0; j < side; j++)
                for (int i = 0; i < side; i++)
                {
                    double x = E0 + i + rnd.NextDouble() * 0.6 - 0.3, y = N0 + j + rnd.NextDouble() * 0.6 - 0.3;
                    ogl.Add(new SurveyPoint { Name = "G" + (j * side + i + 1), E = x, N = y, Z = ground(x, y) + (rnd.NextDouble() - 0.5) * 0.01 });
                }
            // today's survey: west half excavated ~0.4 m, east half filled ~0.6 m, inside a 150 x 150 m block
            Func<double, double, double> change = (x, y) => (x - E0) < 150 ? -0.4 - 0.2 * Math.Sin((y - N0) / 30) : 0.6 + 0.3 * Math.Cos((x - E0) / 20);
            var survey = new List<SurveyPoint>();
            int k = 1;
            for (double y = N0 + 80; y <= N0 + 230; y += 5)
                for (double x = E0 + 80; x <= E0 + 230; x += 5)
                {
                    double xx = x + rnd.NextDouble() - 0.5, yy = y + rnd.NextDouble() - 0.5;
                    survey.Add(new SurveyPoint { Name = (k++).ToString(Inv), E = xx, N = yy, Z = ground(xx, yy) + change(xx, yy), Desc = "EW" });
                }

            var sw = Stopwatch.StartNew();
            var oglTin = new Tin(ogl, 50);
            Line($"OGL TIN: {ogl.Count:N0} points → {oglTin.TriangleCount:N0} triangles in {sw.ElapsedMilliseconds} ms");
            sw.Restart();
            var newTin = new Tin(survey, 50);
            var cf = CutFill.Compute(oglTin, newTin, survey, null, 0);
            Line($"Cut/fill: {survey.Count} survey points, cell {cf.Cell} m, cut {cf.CutVolume:N1} m3, fill {cf.FillVolume:N1} m3, area {cf.TotalArea:N1} m2 in {sw.ElapsedMilliseconds} ms");
            // analytic check of the synthetic change (independent of the TIN): integrate change() on a fine grid over the common area
            double aCut = 0, aFill = 0, s = 0.25;
            for (double y = cf.MinY; y <= cf.MaxY; y += s)
                for (double x = cf.MinX; x <= cf.MaxX; x += s) { double d = change(x, y); if (d > 0) aFill += d * s * s; else aCut -= d * s * s; }
            Line($"Analytic over same extent: cut {aCut:N1}, fill {aFill:N1}  → difference cut {Pct(cf.CutVolume, aCut)}, fill {Pct(cf.FillVolume, aFill)}");

            // level check data: kerb points vs design surface, some out of tolerance
            var design = new List<SurveyPoint>();
            for (double x = 0; x <= 200; x += 2) { design.Add(new SurveyPoint { E = E0 + x, N = N0 + 50, Z = 12 + 0.01 * x }); design.Add(new SurveyPoint { E = E0 + x, N = N0 + 52, Z = 12.02 + 0.01 * x }); }
            var kerb = new List<SurveyPoint>();
            for (int i = 0; i <= 40; i++)
            {
                double x = i * 5, err = (i % 9 == 0) ? 0.018 : (i % 13 == 0 ? -0.022 : (rnd.NextDouble() - 0.5) * 0.012);
                kerb.Add(new SurveyPoint { Name = "K" + (i + 1), E = E0 + x, N = N0 + 51, Z = 12.01 + 0.01 * x + err });
            }
            var lc = LevelCheck.AgainstSurface(kerb, new Tin(design), 0.010, 0.010);
            Line($"Level check: {lc.Pass} pass, {lc.Fail} fail, {lc.NoDesign} no design");

            // primavera
            var sched = PrimaveraReader.ParseXer(SampleXer);
            sched.SourceFile = "SampleProject.xer";
            sched.Find("A1000").PlannedQty = 12000; sched.Find("A1000").ExecutedQty = cf.CutVolume;
            sched.Find("A1010").PlannedQty = 15000; sched.Find("A1010").ExecutedQty = cf.FillVolume;
            foreach (var a in sched.Activities) a.UpdateDrawingPercent(0, false);
            Line("P6: " + string.Join(", ", sched.Activities.Select(a => a.Id + " " + a.EffectivePercent.ToString("0.0", Inv) + "% " + a.State(sched.DataDate))));

            foreach (var lang in new[] { "ar", "en" })
            {
                Lang.Set(lang);
                Step($"report_cutfill_{lang}", () => Report(ReportFactory.CutFill(cf, null, lang == "ar" ? "المنطقة A" : "Zone A", new DateTime(2026, 11, 23), "OGL_RAWDA.csv", "Survey_2026-11-23.csv", survey.Count), $"report_cutfill_{lang}"));
                Step($"report_level_{lang}", () => Report(ReportFactory.LevelCheck(lc, Lang.T("PresetKerb"), Lang.T("ModeSurface")), $"report_level_{lang}"));
                Step($"report_p6_{lang}", () => Report(ReportFactory.Progress(sched), $"report_p6_{lang}"));

                Step($"form_import_{lang}", () => Shot(new ImportForm(), $"form_import_{lang}"));
                Step($"form_convert_{lang}", () => Shot(new ConvertForm(), $"form_convert_{lang}"));
                Step($"form_export_{lang}", () => Shot(new ExportForm(survey), $"form_export_{lang}"));
                Step($"form_cutfill_{lang}", () => Shot(new CutFillForm(), $"form_cutfill_{lang}"));
                Step($"form_level_{lang}", () => Shot(new LevelCheckForm(), $"form_level_{lang}"));
                Step($"form_surface_{lang}", () => Shot(new SurfaceForm(), $"form_surface_{lang}"));
                Step($"form_sections_{lang}", () => Shot(new SectionsForm(), $"form_sections_{lang}"));
                Step($"form_p6_{lang}", () => Shot(new PrimaveraForm(), $"form_p6_{lang}"));
                Step($"form_settings_{lang}", () => Shot(new SettingsForm(), $"form_settings_{lang}"));
                Step($"form_about_{lang}", () => Shot(new AboutForm(), $"form_about_{lang}"));
            }
            Line(_errors == 0 ? "ALL PREVIEWS OK" : _errors + " PREVIEW STEP(S) FAILED");
            File.WriteAllText(Path.Combine(_out, "summary.txt"), Log.ToString(), new UTF8Encoding(true));
            Console.WriteLine(Log.ToString());
            return _errors == 0 ? 0 : 1;
        }

        private static string Pct(double a, double b) => b == 0 ? "n/a" : ((a - b) / b * 100).ToString("+0.00;-0.00", Inv) + "%";

        private static void Line(string s) { Log.AppendLine(s); Console.WriteLine(s); }

        private static void Step(string name, Action a)
        {
            try { var sw = Stopwatch.StartNew(); a(); Line($"OK   {name} ({sw.ElapsedMilliseconds} ms)"); }
            catch (Exception ex) { _errors++; Line($"FAIL {name}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
        }

        private static void Report(ReportDoc d, string name)
        {
            string pdf = Path.Combine(_out, name + ".pdf");
            ReportBuilder.SavePdf(d, pdf);
            var pages = ReportBuilder.RenderPages(d);
            try
            {
                Line($"     {name}: {pages.Count} page(s), PDF {new FileInfo(pdf).Length / 1024} KB");
                for (int i = 0; i < Math.Min(2, pages.Count); i++) SaveScaled(pages[i], Path.Combine(_out, $"{name}_p{i + 1}.png"), 0.55);
            }
            finally { foreach (var p in pages) p.Dispose(); }
        }

        private static void SaveScaled(Bitmap b, string path, double sc)
        {
            using (var small = new Bitmap((int)(b.Width * sc), (int)(b.Height * sc)))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(b, 0, 0, small.Width, small.Height);
                }
                small.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>Shows the window on the CI desktop and captures it (screen capture, falls back to DrawToBitmap).</summary>
        private static void Shot(Form f, string name)
        {
            using (f)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(0, 0);
                f.TopMost = true;
                var scr = Screen.PrimaryScreen.WorkingArea;
                if (f.Width > scr.Width) f.Width = scr.Width;
                if (f.Height > scr.Height) f.Height = scr.Height;
                f.Show();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    bool captured = false;
                    try
                    {
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                        captured = !IsBlank(bmp);
                    }
                    catch { }
                    if (!captured) f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                    bmp.Save(Path.Combine(_out, name + ".png"), ImageFormat.Png);
                    Line($"     {name}: {f.Width}x{f.Height} ({(captured ? "screen" : "DrawToBitmap")})");
                }
                f.Close();
            }
        }

        private static bool IsBlank(Bitmap b)
        {
            var c0 = b.GetPixel(b.Width / 2, b.Height / 2);
            for (int i = 0; i < 50; i++)
            {
                var c = b.GetPixel(b.Width * i / 50, b.Height * ((i * 7) % 50) / 50);
                if (c != c0) return false;
            }
            return true;
        }

        private static string MakeLogo()
        {
            string p = Path.Combine(Path.GetTempPath(), "sx_sample_logo.png");
            using (var b = new Bitmap(360, 160))
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                g.FillEllipse(new SolidBrush(Color.FromArgb(0xE6, 0x7E, 0x22)), 10, 10, 140, 140);
                g.DrawString("SC", new Font("Segoe UI", 48, FontStyle.Bold), Brushes.White, 22, 36);
                g.DrawString("SAMPLE\nCONTRACTING", new Font("Segoe UI", 22, FontStyle.Bold), new SolidBrush(Color.FromArgb(0x2C, 0x3E, 0x50)), 160, 34);
                b.Save(p, ImageFormat.Png);
            }
            return p;
        }

        private const string SampleXer =
            "ERMHDR\t21.12\t2026-11-20\tProject\tadmin\n" +
            "%T\tPROJECT\n%F\tproj_id\tproj_short_name\tlast_recalc_date\n%R\t1\tSAMPLE-ROAD\t2026-11-23 08:00\n" +
            "%T\tPROJWBS\n%F\twbs_id\tproj_id\twbs_name\n%R\t10\t1\tEarthworks\n%R\t11\t1\tRoad works\n" +
            "%T\tTASK\n%F\ttask_id\tproj_id\twbs_id\ttask_code\ttask_name\tstatus_code\tphys_complete_pct\ttarget_start_date\ttarget_end_date\tact_start_date\tact_end_date\n" +
            "%R\t100\t1\t10\tA1000\tExcavation Zone A\tTK_Active\t20\t2026-11-01 08:00\t2026-12-20 17:00\t2026-11-02 08:00\t\n" +
            "%R\t101\t1\t10\tA1010\tEmbankment fill Zone A\tTK_Active\t10\t2026-11-10 08:00\t2026-11-20 17:00\t2026-11-11 08:00\t\n" +
            "%R\t102\t1\t10\tA1020\tOGL survey\tTK_Complete\t100\t2026-10-01 08:00\t2026-10-05 17:00\t2026-10-01 08:00\t2026-10-04 17:00\n" +
            "%R\t103\t1\t11\tR2000\tKerb installation KP8-11\tTK_NotStart\t0\t2026-12-01 08:00\t2026-12-30 17:00\t\t\n" +
            "%R\t104\t1\t11\tR2010\tSubgrade preparation\tTK_NotStart\t0\t2026-11-15 08:00\t2026-11-30 17:00\t\t\n%E\n";
    }
}
