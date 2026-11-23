using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using SurveyX.Core;

namespace SurveyX.UI
{
    public sealed class ReportDoc
    {
        public string Title = "";
        public List<(string k, string v)> Info = new List<(string, string)>();
        public List<(string label, string value, Color color)> Cards = new List<(string, string, Color)>();
        public Bitmap Map;
        public string MapTitle = "";
        public List<(Color c, string label)> Legend = new List<(Color, string)>();
        public string TableTitle = "";
        public string[] Headers;
        public float[] ColWeights;
        public List<string[]> Rows = new List<string[]>();
        public List<Color?> RowColors = new List<Color?>();
        public List<string> Notes = new List<string>();
    }

    /// <summary>Renders A4 report pages with GDI+ (correct Arabic shaping) and saves them as PDF.</summary>
    public static class ReportBuilder
    {
        private const int W = 1240, H = 1754, M = 70; // A4 @150 dpi
        private static readonly Color Accent = Color.FromArgb(0x15, 0x65, 0xC0);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int MaxTableRows = 1500;

        private sealed class Ctx
        {
            public List<Bitmap> Pages = new List<Bitmap>();
            public Graphics G;
            public int Y;
            public bool Rtl;
            public Font Body, Small, Bold, Title, Big;
        }

        public static void SavePdf(ReportDoc d, string path)
        {
            var bitmaps = RenderPages(d);
            try
            {
                var pages = new List<PdfWriter.Page>();
                var jpg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                foreach (var b in bitmaps)
                {
                    using (var ms = new MemoryStream())
                    {
                        b.Save(ms, jpg, ep);
                        pages.Add(new PdfWriter.Page { Jpeg = ms.ToArray(), PixelWidth = b.Width, PixelHeight = b.Height });
                    }
                }
                PdfWriter.Write(path, pages, d.Title, Settings.Current.PreparedBy);
            }
            finally { foreach (var b in bitmaps) b.Dispose(); }
        }

        /// <summary>Renders the report as A4 page bitmaps (150 dpi). Caller disposes them.</summary>
        public static List<Bitmap> RenderPages(ReportDoc d)
        {
            var c = new Ctx { Rtl = Lang.IsArabic };
            c.Body = new Font("Segoe UI", 21, GraphicsUnit.Pixel);
            c.Small = new Font("Segoe UI", 17, GraphicsUnit.Pixel);
            c.Bold = new Font("Segoe UI", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            c.Title = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
            c.Big = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
            try
            {
                NewPage(c, d, first: true);
                Info(c, d);
                Cards(c, d);
                MapBlock(c, d);
                Table(c, d);
                Notes(c, d);
                c.G.Dispose();
                Footers(c);
                return c.Pages;
            }
            catch
            {
                foreach (var b in c.Pages) b.Dispose();
                throw;
            }
            finally
            {
                c.Body.Dispose(); c.Small.Dispose(); c.Bold.Dispose(); c.Title.Dispose(); c.Big.Dispose();
            }
        }

        // ------------------------------------------------------------------ helpers
        private static Rectangle R(Ctx c, int x, int y, int w, int h) => c.Rtl ? new Rectangle(W - x - w, y, w, h) : new Rectangle(x, y, w, h);

        private static StringFormat Fmt(Ctx c, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
        {
            var f = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter };
            if (c.Rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            return f;
        }

        private static void Text(Ctx c, string s, Font f, Color col, Rectangle r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
        {
            using (var b = new SolidBrush(col))
            using (var sf = Fmt(c, h, v))
                c.G.DrawString(s ?? "", f, b, r, sf);
        }

        private static void Ensure(Ctx c, ReportDoc d, int need)
        {
            if (c.Y + need > H - M - 40) { c.G.Dispose(); NewPage(c, d, first: false); }
        }

        private static void NewPage(Ctx c, ReportDoc d, bool first)
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
            bmp.SetResolution(150, 150);
            c.Pages.Add(bmp);
            c.G = Graphics.FromImage(bmp);
            c.G.Clear(Color.White);
            c.G.SmoothingMode = SmoothingMode.AntiAlias;
            c.G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            c.G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            c.Y = M;
            var st = Settings.Current;

            int logoW = 0;
            if (!string.IsNullOrEmpty(st.LogoPath) && File.Exists(st.LogoPath))
            {
                try
                {
                    using (var logo = Image.FromFile(st.LogoPath))
                    {
                        int maxW = first ? 240 : 150, maxH = first ? 120 : 70;
                        double sc = Math.Min((double)maxW / logo.Width, (double)maxH / logo.Height);
                        int lw = (int)(logo.Width * sc), lh = (int)(logo.Height * sc);
                        c.G.DrawImage(logo, R(c, M, c.Y, lw, lh));
                        logoW = lw + 24;
                    }
                }
                catch { }
            }
            int hh = first ? 120 : 70;
            if (first)
            {
                Text(c, string.IsNullOrEmpty(st.CompanyName) ? AppInfo.Name : st.CompanyName, c.Big, Color.FromArgb(40, 40, 40), R(c, M + logoW, c.Y, W - 2 * M - logoW, 50));
                Text(c, d.Title, c.Title, Accent, R(c, M + logoW, c.Y + 52, W - 2 * M - logoW, 56));
            }
            else
            {
                Text(c, d.Title, c.Bold, Accent, R(c, M + logoW, c.Y, W - 2 * M - logoW, hh));
            }
            c.Y += hh + 12;
            using (var p = new Pen(Accent, 4)) c.G.DrawLine(p, M, c.Y, W - M, c.Y);
            c.Y += 22;
        }

        private static void Info(Ctx c, ReportDoc d)
        {
            if (d.Info.Count == 0) return;
            int colW = (W - 2 * M) / 2, rowH = 38;
            for (int i = 0; i < d.Info.Count; i++)
            {
                int col = i % 2, x = M + col * colW;
                if (col == 0) Ensure(c, d, rowH);
                var (k, v) = d.Info[i];
                Text(c, k, c.Small, Color.Gray, R(c, x, c.Y, 210, rowH));
                Text(c, v, c.Bold, Color.Black, R(c, x + 210, c.Y, colW - 220, rowH));
                if (col == 1 || i == d.Info.Count - 1) c.Y += rowH;
            }
            c.Y += 16;
        }

        private static void Cards(Ctx c, ReportDoc d)
        {
            if (d.Cards.Count == 0) return;
            int per = Math.Min(4, d.Cards.Count), gap = 18;
            int cw = (W - 2 * M - gap * (per - 1)) / per, ch = 120;
            for (int i = 0; i < d.Cards.Count; i++)
            {
                int col = i % per;
                if (col == 0) Ensure(c, d, ch + gap);
                var (label, value, color) = d.Cards[i];
                var r = R(c, M + col * (cw + gap), c.Y, cw, ch);
                using (var bg = new SolidBrush(Color.FromArgb(0xF4, 0xF7, 0xFB))) c.G.FillRectangle(bg, r);
                using (var bar = new SolidBrush(color)) c.G.FillRectangle(bar, r.X, r.Y, r.Width, 8);
                Text(c, label, c.Small, Color.DimGray, new Rectangle(r.X + 14, r.Y + 14, r.Width - 28, 36));
                Text(c, value, c.Big, Color.FromArgb(30, 30, 30), new Rectangle(r.X + 14, r.Y + 50, r.Width - 28, 60));
                if (col == per - 1 || i == d.Cards.Count - 1) c.Y += ch + gap;
            }
            c.Y += 8;
        }

        private static void MapBlock(Ctx c, ReportDoc d)
        {
            if (d.Map == null) return;
            int maxW = W - 2 * M, maxH = 820;
            double sc = Math.Min((double)maxW / d.Map.Width, (double)maxH / d.Map.Height);
            int mw = (int)(d.Map.Width * sc), mh = (int)(d.Map.Height * sc);
            int legendRows = (d.Legend.Count + 2) / 3;
            Ensure(c, d, mh + 50 + legendRows * 34 + 20);
            if (!string.IsNullOrEmpty(d.MapTitle)) { Text(c, d.MapTitle, c.Bold, Accent, R(c, M, c.Y, maxW, 36)); c.Y += 40; }
            c.G.DrawImage(d.Map, new Rectangle(M + (maxW - mw) / 2, c.Y, mw, mh));
            using (var p = new Pen(Color.Silver)) c.G.DrawRectangle(p, M + (maxW - mw) / 2, c.Y, mw, mh);
            c.Y += mh + 12;
            int lw = maxW / 3;
            for (int i = 0; i < d.Legend.Count; i++)
            {
                int col = i % 3;
                var (lc, ll) = d.Legend[i];
                var box = R(c, M + col * lw, c.Y + 6, 26, 22);
                using (var b = new SolidBrush(lc)) c.G.FillRectangle(b, box);
                c.G.DrawRectangle(Pens.Gray, box);
                Text(c, ll, c.Small, Color.Black, R(c, M + col * lw + 34, c.Y, lw - 40, 34));
                if (col == 2 || i == d.Legend.Count - 1) c.Y += 34;
            }
            c.Y += 16;
        }

        private static void Table(Ctx c, ReportDoc d)
        {
            if (d.Headers == null || d.Rows.Count == 0) return;
            int tw = W - 2 * M, rh = 34;
            var weights = d.ColWeights ?? d.Headers.Select(_ => 1f).ToArray();
            float sum = weights.Sum();
            var widths = weights.Select(w => (int)(tw * w / sum)).ToArray();
            void HeaderRow()
            {
                int x = M;
                using (var hb = new SolidBrush(Accent)) c.G.FillRectangle(hb, M, c.Y, tw, rh + 4);
                for (int i = 0; i < d.Headers.Length; i++)
                {
                    Text(c, d.Headers[i], c.Bold, Color.White, R(c, x + 6, c.Y, widths[i] - 12, rh + 4), StringAlignment.Center);
                    x += widths[i];
                }
                c.Y += rh + 4;
            }
            Ensure(c, d, rh * 3 + 50);
            if (!string.IsNullOrEmpty(d.TableTitle)) { Text(c, d.TableTitle, c.Bold, Accent, R(c, M, c.Y, tw, 36)); c.Y += 40; }
            HeaderRow();
            int n = Math.Min(d.Rows.Count, MaxTableRows);
            for (int r = 0; r < n; r++)
            {
                if (c.Y + rh > H - M - 40) { c.G.Dispose(); NewPage(c, d, first: false); HeaderRow(); }
                Color bg = d.RowColors.Count > r && d.RowColors[r].HasValue ? d.RowColors[r].Value : (r % 2 == 0 ? Color.White : Color.FromArgb(0xF6, 0xF8, 0xFA));
                using (var b = new SolidBrush(bg)) c.G.FillRectangle(b, M, c.Y, tw, rh);
                int x = M;
                for (int i = 0; i < d.Headers.Length && i < d.Rows[r].Length; i++)
                {
                    Text(c, d.Rows[r][i], c.Small, Color.Black, R(c, x + 6, c.Y, widths[i] - 12, rh), StringAlignment.Center);
                    x += widths[i];
                }
                using (var p = new Pen(Color.FromArgb(0xE0, 0xE0, 0xE0))) c.G.DrawLine(p, M, c.Y + rh, M + tw, c.Y + rh);
                c.Y += rh;
            }
            if (d.Rows.Count > n)
            {
                c.Y += 8;
                Text(c, string.Format(Lang.T("TableTruncated"), n, d.Rows.Count), c.Small, Color.DimGray, R(c, M, c.Y, tw, 30));
                c.Y += 30;
            }
            c.Y += 16;
        }

        private static void Notes(Ctx c, ReportDoc d)
        {
            var st = Settings.Current;
            var all = new List<string>(d.Notes);
            foreach (var note in all)
            {
                Ensure(c, d, 34);
                Text(c, "• " + note, c.Small, Color.FromArgb(60, 60, 60), R(c, M, c.Y, W - 2 * M, 32));
                c.Y += 32;
            }
            // signatures
            Ensure(c, d, 150);
            c.Y += 30;
            int sw = (W - 2 * M) / 2;
            Text(c, Lang.T("PreparedBy") + ": " + st.PreparedBy, c.Body, Color.Black, R(c, M, c.Y, sw - 20, 36));
            Text(c, Lang.T("SubmittedTo") + ": " + st.SubmittedTo, c.Body, Color.Black, R(c, M + sw, c.Y, sw - 20, 36));
            c.Y += 70;
            using (var p = new Pen(Color.Gray) { DashStyle = DashStyle.Dash })
            {
                var a = R(c, M, c.Y, sw - 80, 1); c.G.DrawLine(p, a.Left, a.Top, a.Right, a.Top);
                var b = R(c, M + sw, c.Y, sw - 80, 1); c.G.DrawLine(p, b.Left, b.Top, b.Right, b.Top);
            }
            Text(c, Lang.T("Signature"), c.Small, Color.Gray, R(c, M, c.Y + 4, sw - 80, 28));
            Text(c, Lang.T("Signature"), c.Small, Color.Gray, R(c, M + sw, c.Y + 4, sw - 80, 28));
        }

        private static void Footers(Ctx c)
        {
            int n = c.Pages.Count;
            for (int i = 0; i < n; i++)
            {
                using (var g = Graphics.FromImage(c.Pages[i]))
                {
                    c.G = g;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawLine(Pens.LightGray, M, H - M + 4, W - M, H - M + 4);
                    string left = AppInfo.Name + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv);
                    string right = string.Format(Lang.T("PageOf"), i + 1, n);
                    Text(c, left, c.Small, Color.Gray, R(c, M, H - M + 10, (W - 2 * M) / 2, 30));
                    Text(c, right, c.Small, Color.Gray, R(c, M + (W - 2 * M) / 2, H - M + 10, (W - 2 * M) / 2, 30), StringAlignment.Far);
                }
            }
            c.G = null;
        }
    }

    /// <summary>Renders plan-view maps for reports.</summary>
    public static class MapRenderer
    {
        public static Bitmap CutFill(CutFillResult r, IList<(double x, double y)> boundary, int maxPx = 1600)
        {
            var g = r.DiffGrid;
            return Render(r.MinX - g.Cell, r.MinY - g.Cell, r.MaxX + g.Cell, r.MaxY + g.Cell, maxPx, (gr, toPx, scale) =>
            {
                var brushes = ColorScale.CutFillColors.Select(cc => new SolidBrush(Color.FromArgb(cc.r, cc.g, cc.b))).ToArray();
                float cs = (float)Math.Max(1, g.Cell * scale + 0.6);
                for (int j = 0; j < g.NY; j++)
                    for (int i = 0; i < g.NX; i++)
                    {
                        float d = g.Z[j * g.NX + i];
                        if (float.IsNaN(d)) continue;
                        var p = toPx(g.X0 + i * g.Cell, g.Y0 + (j + 1) * g.Cell);
                        gr.FillRectangle(brushes[ColorScale.CutFillClass(d)], p.X, p.Y, cs, cs);
                    }
                foreach (var b in brushes) b.Dispose();
                DrawBoundary(gr, toPx, boundary);
            });
        }

        public static Bitmap Points(IList<(double x, double y, Color c)> pts, IList<(double x, double y)> boundary, int maxPx = 1600)
        {
            if (pts.Count == 0) return null;
            double pad = Math.Max(1, Math.Max(pts.Max(p => p.x) - pts.Min(p => p.x), pts.Max(p => p.y) - pts.Min(p => p.y)) * 0.05);
            return Render(pts.Min(p => p.x) - pad, pts.Min(p => p.y) - pad, pts.Max(p => p.x) + pad, pts.Max(p => p.y) + pad, maxPx, (gr, toPx, scale) =>
            {
                DrawBoundary(gr, toPx, boundary);
                foreach (var p in pts)
                {
                    var q = toPx(p.x, p.y);
                    using (var b = new SolidBrush(p.c)) gr.FillEllipse(b, q.X - 6, q.Y - 6, 12, 12);
                    gr.DrawEllipse(Pens.White, q.X - 6, q.Y - 6, 12, 12);
                }
            });
        }

        public static Bitmap ElevationGrid(ElevationGrid g, double zMin, double zMax, int maxPx = 1600)
        {
            return Render(g.X0, g.Y0, g.X0 + g.NX * g.Cell, g.Y0 + g.NY * g.Cell, maxPx, (gr, toPx, scale) =>
            {
                float cs = (float)Math.Max(1, g.Cell * scale + 0.6);
                for (int j = 0; j < g.NY; j++)
                    for (int i = 0; i < g.NX; i++)
                    {
                        float z = g.Z[j * g.NX + i];
                        if (float.IsNaN(z)) continue;
                        var c = ColorScale.Elevation((z - zMin) / Math.Max(1e-9, zMax - zMin));
                        var p = toPx(g.X0 + i * g.Cell, g.Y0 + (j + 1) * g.Cell);
                        using (var b = new SolidBrush(Color.FromArgb(c.r, c.g, c.b))) gr.FillRectangle(b, p.X, p.Y, cs, cs);
                    }
            });
        }

        private static void DrawBoundary(Graphics gr, Func<double, double, PointF> toPx, IList<(double x, double y)> boundary)
        {
            if (boundary == null || boundary.Count < 2) return;
            using (var pen = new Pen(Color.Black, 3))
                gr.DrawPolygon(pen, boundary.Select(p => toPx(p.x, p.y)).ToArray());
        }

        private static Bitmap Render(double x0, double y0, double x1, double y1, int maxPx, Action<Graphics, Func<double, double, PointF>, double> draw)
        {
            double w = Math.Max(1e-6, x1 - x0), h = Math.Max(1e-6, y1 - y0);
            double scale = maxPx / Math.Max(w, h);
            int pw = Math.Max(200, (int)(w * scale)), ph = Math.Max(200, (int)(h * scale));
            var bmp = new Bitmap(pw + 120, ph + 60, PixelFormat.Format24bppRgb);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.Clear(Color.White);
                gr.SmoothingMode = SmoothingMode.None;
                PointF ToPx(double x, double y) => new PointF((float)((x - x0) * scale) + 20, (float)((y1 - y) * scale) + 20);
                draw(gr, ToPx, scale);
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                // north arrow
                float ax = bmp.Width - 60, ay = 30;
                gr.FillPolygon(Brushes.Black, new[] { new PointF(ax, ay), new PointF(ax - 14, ay + 40), new PointF(ax, ay + 30), new PointF(ax + 14, ay + 40) });
                using (var f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel)) gr.DrawString("N", f, Brushes.Black, ax - 8, ay + 42);
                // scale bar
                double nice = NiceLength(w / 5);
                float len = (float)(nice * scale);
                float sx = 20, sy = bmp.Height - 30;
                gr.FillRectangle(Brushes.Black, sx, sy, len, 6);
                using (var f = new Font("Segoe UI", 16, GraphicsUnit.Pixel)) gr.DrawString(nice.ToString("0.##", CultureInfo.InvariantCulture) + " m", f, Brushes.Black, sx + len + 8, sy - 8);
            }
            return bmp;
        }

        private static double NiceLength(double v)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(v, 1e-6))));
            foreach (var m in new[] { 1, 2, 5, 10 }) if (m * p >= v) return m * p;
            return 10 * p;
        }
    }
}
