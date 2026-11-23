using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SurveyX.Core;

namespace SurveyX.Stress
{
    /// <summary>
    /// Randomised stress test (fixed seed, reproducible). Every comparison counts as one check.
    /// Exits with code 1 if any check fails.
    /// </summary>
    internal static class Program
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static long _checks, _fails;
        private static readonly Dictionary<string, (long checks, long fails, long ms)> Sections = new Dictionary<string, (long, long, long)>();
        private static readonly List<string> FailSamples = new List<string>();
        private static Random R = new Random(20261123);

        private static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) { _fails++; if (FailSamples.Count < 40) FailSamples.Add(what); }
        }

        private static void Section(string name, Action a)
        {
            long c0 = _checks, f0 = _fails;
            var sw = Stopwatch.StartNew();
            try { a(); }
            catch (Exception ex) { _fails++; FailSamples.Add(name + " CRASH: " + ex); }
            Sections[name] = (_checks - c0, _fails - f0, sw.ElapsedMilliseconds);
            Console.WriteLine($"{name,-44} {_checks - c0,12:N0} checks  {_fails - f0,6:N0} fails  {sw.Elapsed.TotalSeconds,7:F1} s");
        }

        private static double U(double a, double b) => a + R.NextDouble() * (b - a);

        private static int Main(string[] args)
        {
            var total = Stopwatch.StartNew();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("SurveyX CAD stress test — seed 20261123\n");

            Section("1. Coordinates vs official PROJ (21,000 pts)", ProjReference);
            Section("2. Coordinate round trips (1,000,000 pts)", RoundTrips);
            Section("3. Cross-system chains (200,000 pts)", Chains);
            Section("4. DMS angle parse/format (100,000)", Angles);
            Section("5. Delaunay triangulation (3,000 sets)", Triangulations);
            Section("6. TIN interpolation on planes", TinPlanes);
            Section("7. Cut & fill volumes (1,500 scenarios)", Volumes);
            Section("8. Contours on planes (500 surfaces)", Contours);
            Section("9. Level check classification (200,000)", Levels);
            Section("10. Chainage / offset (150,000)", StationOffsets);
            Section("11. Point file readers (20,000 files)", Readers);
            Section("12. Writers → readers round trip (3,000)", Writers);
            Section("13. Primavera XER parsing (5,000 files)", Xer);
            Section("14. JSON round trip (100,000 objects)", Json);
            Section("15. Shalaby free commands (200,000 msgs)", Intents);

            Console.WriteLine();
            Console.WriteLine($"TOTAL: {_checks:N0} checks, {_fails:N0} failures, {total.Elapsed.TotalMinutes:F1} min");
            foreach (var f in FailSamples) Console.WriteLine("  FAIL: " + f);
            var sb = new StringBuilder();
            sb.AppendLine($"SurveyX CAD stress test — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            foreach (var kv in Sections) sb.AppendLine($"{kv.Key}\t{kv.Value.checks}\t{kv.Value.fails}\t{kv.Value.ms} ms");
            sb.AppendLine($"TOTAL\t{_checks}\t{_fails}\t{total.ElapsedMilliseconds} ms");
            foreach (var f in FailSamples) sb.AppendLine("FAIL " + f);
            if (args.Length > 0) File.WriteAllText(args[0], sb.ToString());
            return _fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ 1
        private static void ProjReference()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "reference_vectors.csv");
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                var c = line.Split(',');
                var crs = CrsCatalog.Find(c[0]);
                double lon = double.Parse(c[1], Inv), lat = double.Parse(c[2], Inv), e = double.Parse(c[3], Inv), n = double.Parse(c[4], Inv);
                new CoordinateTransformer(CrsCatalog.Wgs84Geo, crs).Transform(lon, lat, out double x, out double y);
                Check(Math.Abs(x - e) < 0.002 && Math.Abs(y - n) < 0.002, $"PROJ fwd {c[0]} {lon},{lat}: dE={x - e:F4} dN={y - n:F4}");
                new CoordinateTransformer(crs, CrsCatalog.Wgs84Geo).Transform(e, n, out double lo, out double la);
                Check(Math.Abs(lo - lon) * 1e5 < 0.002 && Math.Abs(la - lat) * 1.1e5 < 0.002, $"PROJ inv {c[0]} {e},{n}");
            }
        }

        private static readonly Dictionary<string, (double lo0, double lo1, double la0, double la1)> Areas = new Dictionary<string, (double, double, double, double)>
        {
            { "EPSG:3997", (54.6, 56.4, 24.6, 25.9) }, { "EPSG:32640", (54, 57.9, 22.6, 26.1) }, { "EPSG:32639", (51.5, 54, 22.6, 25) },
            { "EPSG:27040", (54, 56.4, 22.6, 26.1) }, { "EPSG:27039", (51.5, 54, 22.6, 25) }, { "EPSG:27040-AD", (54, 56, 22.7, 25) },
            { "EPSG:22992", (29, 33, 22, 31.6) }, { "EPSG:22993", (25, 29, 28, 31.6) }, { "EPSG:22994", (25, 29, 22, 28) }, { "EPSG:22991", (33, 37, 22, 31.6) },
            { "EPSG:32635", (24, 30, 22, 32) }, { "EPSG:32636", (30, 36, 22, 32) }, { "EPSG:32637", (36, 42, 16, 32) }, { "EPSG:32638", (42, 48, 16, 32) },
        };

        // ------------------------------------------------------------------ 2
        private static void RoundTrips()
        {
            var list = Areas.Keys.Select(CrsCatalog.Find).ToList();
            var fwd = list.Select(c => new CoordinateTransformer(CrsCatalog.Wgs84Geo, c)).ToList();
            var inv = list.Select(c => new CoordinateTransformer(c, CrsCatalog.Wgs84Geo)).ToList();
            for (int i = 0; i < 1_000_000; i++)
            {
                int k = i % list.Count;
                var a = Areas[list[k].Id];
                double lon = U(a.lo0, a.lo1), lat = U(a.la0, a.la1);
                fwd[k].Transform(lon, lat, out double e, out double n);
                inv[k].Transform(e, n, out double lo2, out double la2);
                double err = Math.Sqrt(Math.Pow((lo2 - lon) * 101000, 2) + Math.Pow((la2 - lat) * 111000, 2));
                Check(err < 0.001, $"round trip {list[k].Id} {lon:F6},{lat:F6} err {err:F5} m");
            }
        }

        // ------------------------------------------------------------------ 3
        private static void Chains()
        {
            string[][] chains =
            {
                new[] { "EPSG:32640", "EPSG:3997", "EPSG:27040", "EPSG:27040-AD", "EPSG:32640" },
                new[] { "EPSG:22992", "EPSG:32636", "EPSG:22993", "EPSG:22992" },
                new[] { "EPSG:22994", "EPSG:32635", "EPSG:22994" },
                new[] { "EPSG:27039", "EPSG:32639", "EPSG:27039" },
            };
            (double lo0, double lo1, double la0, double la1)[] areas = { (55.0, 55.9, 24.8, 25.4), (30.0, 32.0, 29.0, 31.0), (26.0, 28.5, 23.0, 27.5), (52.0, 53.8, 23.0, 24.5) };
            for (int i = 0; i < 200_000; i++)
            {
                int k = i % chains.Length;
                var ch = chains[k];
                var a = areas[k];
                new CoordinateTransformer(CrsCatalog.Wgs84Geo, CrsCatalog.Find(ch[0])).Transform(U(a.lo0, a.lo1), U(a.la0, a.la1), out double e0, out double n0);
                double e = e0, n = n0;
                for (int j = 1; j < ch.Length; j++)
                    new CoordinateTransformer(CrsCatalog.Find(ch[j - 1]), CrsCatalog.Find(ch[j])).Transform(e, n, out e, out n);
                double err = Math.Sqrt((e - e0) * (e - e0) + (n - n0) * (n - n0));
                Check(err < 0.002, $"chain {string.Join(">", ch)} err {err:F4} m");
            }
        }

        // ------------------------------------------------------------------ 4
        private static void Angles()
        {
            for (int i = 0; i < 100_000; i++)
            {
                double d = U(-179.999, 179.999);
                bool isLat = i % 2 == 0;
                if (isLat) d = U(-89.999, 89.999);
                string s = AngleFormat.ToDms(d, isLat, 5);
                Check(AngleFormat.TryParse(s, out double back) && Math.Abs(back - d) < 1e-8, $"DMS {d} -> {s} -> {back}");
            }
        }

        // ------------------------------------------------------------------ 5
        private static List<(double x, double y)> Hull(List<(double x, double y)> p)
        {
            var pts = p.Distinct().OrderBy(a => a.x).ThenBy(a => a.y).ToList();
            if (pts.Count < 3) return pts;
            double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var lower = new List<(double, double)>();
            foreach (var q in pts) { while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], q) <= 0) lower.RemoveAt(lower.Count - 1); lower.Add(q); }
            var upper = new List<(double, double)>();
            for (int i = pts.Count - 1; i >= 0; i--) { var q = pts[i]; while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], q) <= 0) upper.RemoveAt(upper.Count - 1); upper.Add(q); }
            lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static void Triangulations()
        {
            for (int s = 0; s < 3000; s++)
            {
                int n = s < 1000 ? R.Next(3, 60) : s < 2500 ? R.Next(60, 800) : R.Next(800, 6000);
                int kind = s % 3; // 0 uniform, 1 clustered, 2 utm-scale long thin
                double ox = kind == 2 ? 234000 : 0, oy = kind == 2 ? 2705000 : 0;
                var pts = new List<SurveyPoint>(n);
                double cx = U(0, 100), cy = U(0, 100);
                for (int i = 0; i < n; i++)
                {
                    double x, y;
                    if (kind == 0) { x = U(0, 1000); y = U(0, 1000); }
                    else if (kind == 1) { x = cx + Gauss() * (i % 5 == 0 ? 200 : 5); y = cy + Gauss() * (i % 5 == 0 ? 200 : 5); }
                    else { x = U(0, 2000); y = U(0, 40); }
                    pts.Add(new SurveyPoint { E = ox + x, N = oy + y, Z = U(0, 10) });
                }
                Tin tin;
                try { tin = new Tin(pts); }
                catch (InvalidOperationException) { Check(n < 3, "collinear set reported for n=" + n); continue; }
                var xy = pts.Select(p => (p.E - tin.OX, p.N - tin.OY)).ToList();
                var hull = Hull(xy);
                double hullArea = Geo2D.Area(hull);
                Check(Math.Abs(tin.Area() - hullArea) <= 1e-7 * Math.Max(1, hullArea), $"set {s}: TIN area {tin.Area()} vs hull {hullArea}");
                Check(tin.TriangleCount == 2 * n - hull.Count - 2, $"set {s}: {tin.TriangleCount} triangles, expected {2 * n - hull.Count - 2} (n={n}, h={hull.Count})");
                // empty circumcircle (exhaustive for small sets, sampled for large)
                int tris = tin.TriangleCount;
                int triSample = n <= 300 ? tris : 300;
                for (int t = 0; t < triSample; t++)
                {
                    int ti = n <= 300 ? t : R.Next(tris);
                    int a = tin.Tri[ti * 3], b = tin.Tri[ti * 3 + 1], c = tin.Tri[ti * 3 + 2];
                    Circ(tin.X[a], tin.Y[a], tin.X[b], tin.Y[b], tin.X[c], tin.Y[c], out double ux, out double uy, out double r2);
                    bool ok = true;
                    int lim = n <= 300 ? n : 300;
                    for (int q = 0; q < lim && ok; q++)
                    {
                        int pi = n <= 300 ? q : R.Next(n);
                        if (pi == a || pi == b || pi == c) continue;
                        double d2 = (tin.X[pi] - ux) * (tin.X[pi] - ux) + (tin.Y[pi] - uy) * (tin.Y[pi] - uy);
                        if (d2 < r2 * (1 - 1e-9) - 1e-9) ok = false;
                    }
                    Check(ok, $"set {s}: triangle {ti} circumcircle contains a point");
                }
            }
        }

        private static double Gauss() => Math.Sqrt(-2 * Math.Log(1 - R.NextDouble())) * Math.Cos(2 * Math.PI * R.NextDouble());

        private static void Circ(double ax, double ay, double bx, double by, double cx, double cy, out double ux, out double uy, out double r2)
        {
            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            r2 = (ax - ux) * (ax - ux) + (ay - uy) * (ay - uy);
        }

        // ------------------------------------------------------------------ 6
        private static List<SurveyPoint> Scatter(double x0, double y0, double w, double h, int n, Func<double, double, double> z)
        {
            var l = new List<SurveyPoint>
            {
                new SurveyPoint { E = x0, N = y0 }, new SurveyPoint { E = x0 + w, N = y0 }, new SurveyPoint { E = x0 + w, N = y0 + h }, new SurveyPoint { E = x0, N = y0 + h },
            };
            for (int i = 0; i < n; i++) l.Add(new SurveyPoint { E = x0 + U(0, w), N = y0 + U(0, h) });
            foreach (var p in l) p.Z = z(p.E, p.N);
            return l;
        }

        private static void TinPlanes()
        {
            for (int s = 0; s < 2000; s++)
            {
                double x0 = U(200000, 800000), y0 = U(2400000, 3300000), w = U(20, 800), h = U(20, 800);
                double a = U(-50, 300), bx = U(-0.05, 0.05), by = U(-0.05, 0.05);
                Func<double, double, double> z = (x, y) => a + bx * (x - x0) + by * (y - y0);
                var tin = new Tin(Scatter(x0, y0, w, h, R.Next(20, 400), z));
                for (int i = 0; i < 200; i++)
                {
                    double x = x0 + U(0.001, w - 0.001), y = y0 + U(0.001, h - 0.001);
                    double zi = tin.ZAt(x, y);
                    Check(!double.IsNaN(zi) && Math.Abs(zi - z(x, y)) < 1e-6, $"plane {s}: ZAt err {zi - z(x, y)}");
                }
                Check(double.IsNaN(tin.ZAt(x0 - 1, y0 - 1)) && double.IsNaN(tin.ZAt(x0 + w + 1, y0 + h + 1)), $"plane {s}: outside should be NaN");
            }
        }

        // ------------------------------------------------------------------ 7
        private static void Volumes()
        {
            for (int s = 0; s < 1500; s++)
            {
                int cellsX = R.Next(40, 160), cellsY = R.Next(40, 160);
                double cell = new[] { 0.25, 0.5, 1.0, 2.0 }[R.Next(4)];
                double x0 = Math.Round(U(200000, 800000) / cell) * cell, y0 = Math.Round(U(2400000, 3300000) / cell) * cell;
                double w = cellsX * cell, h = cellsY * cell;
                double a1 = U(5, 20), b1 = U(-0.02, 0.02), c1 = U(-0.02, 0.02);
                double a2 = a1 + U(-1.5, 1.5), b2 = b1 + U(-0.03, 0.03), c2 = c1 + U(-0.03, 0.03);
                Func<double, double, double> z1 = (x, y) => a1 + b1 * (x - x0) + c1 * (y - y0);
                Func<double, double, double> z2 = (x, y) => a2 + b2 * (x - x0) + c2 * (y - y0);
                var ogl = Scatter(x0, y0, w, h, R.Next(50, 600), z1);
                var srv = Scatter(x0, y0, w, h, R.Next(30, 300), z2);
                var r = CutFill.Compute(new Tin(ogl), new Tin(srv), srv, null, cell, 0);
                double area = w * h;
                Check(Math.Abs(r.TotalArea - area) < 1e-6 * area, $"vol {s}: area {r.TotalArea} vs {area}");
                // exact net volume of the difference of two planes = d(centre) × area
                double net = (z2(x0 + w / 2, y0 + h / 2) - z1(x0 + w / 2, y0 + h / 2)) * area;
                Check(Math.Abs(r.NetVolume - net) < 1e-5 * Math.Max(1, Math.Abs(area)), $"vol {s}: net {r.NetVolume} vs {net}");
                // cut / fill split vs fine independent integration (400×400 midpoint)
                double fill = 0, cut = 0; int m = 400;
                for (int j = 0; j < m; j++)
                    for (int i = 0; i < m; i++)
                    {
                        double x = x0 + (i + 0.5) * w / m, y = y0 + (j + 0.5) * h / m, d = z2(x, y) - z1(x, y);
                        if (d > 0) fill += d; else cut -= d;
                    }
                fill *= area / (m * m); cut *= area / (m * m);
                double tol = 0.01 * Math.Max(fill + cut, 1) + 1e-6;
                Check(Math.Abs(r.FillVolume - fill) < tol && Math.Abs(r.CutVolume - cut) < tol, $"vol {s}: fill {r.FillVolume:F3}/{fill:F3} cut {r.CutVolume:F3}/{cut:F3}");
                foreach (var p in r.Points) Check(Math.Abs(p.RefZ - z1(p.Point.E, p.Point.N)) < 1e-6, $"vol {s}: point OGL level");
            }
        }

        // ------------------------------------------------------------------ 8
        private static void Contours()
        {
            for (int s = 0; s < 500; s++)
            {
                double x0 = U(0, 1000), y0 = U(0, 1000), w = U(50, 400), h = U(50, 400);
                double a = U(0, 50), bx = U(-0.05, 0.05), by = U(-0.05, 0.05);
                Func<double, double, double> z = (x, y) => a + bx * (x - x0) + by * (y - y0);
                var tin = new Tin(Scatter(x0, y0, w, h, R.Next(30, 400), z));
                double interval = new[] { 0.1, 0.25, 0.5, 1.0 }[R.Next(4)];
                foreach (var (level, pts) in tin.Contours(interval))
                    foreach (var p in pts) Check(Math.Abs(z(p.x, p.y) - level) < 1e-4, $"contour {s}: point off level {level}");
            }
        }

        // ------------------------------------------------------------------ 9
        private static void Levels()
        {
            var design = Scatter(1000, 2000, 300, 300, 300, (x, y) => 12 + 0.01 * (x - 1000) - 0.004 * (y - 2000));
            var tin = new Tin(design);
            var asb = new List<SurveyPoint>(200_000);
            var expected = new List<CheckStatus>(200_000);
            double tp = 0.010, tm = 0.015;
            for (int i = 0; i < 200_000; i++)
            {
                double x = U(1000.01, 1299.99), y = U(2000.01, 2299.99);
                double err = Math.Round(U(-0.04, 0.04), 4);
                if (Math.Abs(err - tp) < 2e-4 || Math.Abs(err + tm) < 2e-4) err += 0.001; // keep away from the exact limit
                asb.Add(new SurveyPoint { Name = "P" + i, E = x, N = y, Z = 12 + 0.01 * (x - 1000) - 0.004 * (y - 2000) + err });
                expected.Add(err <= tp && err >= -tm ? CheckStatus.Pass : CheckStatus.Fail);
            }
            var r = LevelCheck.AgainstSurface(asb, tin, tp, tm);
            for (int i = 0; i < r.Rows.Count; i++) Check(r.Rows[i].Status == expected[i], $"level {i}: {r.Rows[i].Diff:F4} → {r.Rows[i].Status}");
        }

        // ------------------------------------------------------------------ 10
        private static void StationOffsets()
        {
            for (int s = 0; s < 1500; s++)
            {
                var align = new List<(double x, double y)>();
                double x = U(0, 1000), y = U(0, 1000), ang = U(0, 2 * Math.PI);
                int nv = R.Next(2, 9);
                for (int i = 0; i < nv; i++) { align.Add((x, y)); ang += U(-0.6, 0.6); double L = U(30, 300); x += L * Math.Cos(ang); y += L * Math.Sin(ang); }
                align.Add((x, y));
                double len = Geo2D.Length(align);
                for (int k = 0; k < 100; k++)
                {
                    // build a point from a known chainage / offset, then recover them
                    double ch = U(0.5, len - 0.5), off = U(-20, 20);
                    var p = Geo2D.At(align, ch);
                    double px = p.x + off * p.dy, py = p.y - off * p.dx; // right-hand normal
                    var so = StationOffset.Compute(align, px, py);
                    // checks: never farther than the constructed offset; the foot point lies at that distance;
                    // and when the foot is inside a segment (not at a vertex) the side (+right / −left) is correct.
                    var q = Geo2D.At(align, so.station);
                    double fd = Math.Sqrt((px - q.x) * (px - q.x) + (py - q.y) * (py - q.y));
                    bool atVertex = align.Any(v => Math.Abs(v.x - q.x) < 1e-6 && Math.Abs(v.y - q.y) < 1e-6);
                    double cross = q.dx * (py - q.y) - q.dy * (px - q.x); // > 0 → left
                    bool sideOk = atVertex || Math.Abs(so.offset) < 1e-6 || (cross > 0) == (so.offset < 0);
                    Check(so.distance <= Math.Abs(off) + 1e-7 && Math.Abs(fd - so.distance) < 1e-6 && Math.Abs(Math.Abs(so.offset) - so.distance) < 1e-9 && sideOk,
                        $"setout {s}/{k}: ch {ch:F3} off {off:F3} → {so.station:F3} {so.offset:F3}");
                }
            }
        }

        // ------------------------------------------------------------------ 11
        private static readonly string[] Presets = { "PNEZD", "PENZD", "PNEZ", "PENZ", "NEZ", "ENZ", "PNE", "PEN" };

        private static void Readers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sx_stress_readers");
            Directory.CreateDirectory(dir);
            string[] delims = { ",", "\t", ";", " " };
            for (int f = 0; f < 20_000; f++)
            {
                string preset = Presets[R.Next(Presets.Length)];
                string delim = delims[R.Next(delims.Length)];
                bool header = R.Next(3) == 0;
                int n = R.Next(1, 40);
                var pts = new List<SurveyPoint>();
                var sb = new StringBuilder();
                if (header)
                    sb.AppendLine(string.Join(delim, preset.Select(c => c == 'P' ? "Point" : c == 'N' ? "Northing" : c == 'E' ? "Easting" : c == 'Z' ? "Elevation" : "Code")));
                for (int i = 0; i < n; i++)
                {
                    var p = new SurveyPoint { Name = "PT" + R.Next(1, 99999), E = Math.Round(U(200000, 800000), 3), N = Math.Round(U(2400000, 3300000), 3), Z = Math.Round(U(-5, 400), 3), Desc = delim == " " ? "EG" : "Code " + R.Next(9) };
                    pts.Add(p);
                    sb.AppendLine(string.Join(delim, preset.Select(c =>
                        c == 'P' ? p.Name : c == 'N' ? p.N.ToString("F3", Inv) : c == 'E' ? p.E.ToString("F3", Inv) : c == 'Z' ? p.Z.ToString("F3", Inv) : p.Desc)));
                }
                string path = Path.Combine(dir, "f" + (f % 50) + (delim == "\t" ? ".txt" : ".csv"));
                File.WriteAllText(path, sb.ToString());
                var r = header ? PointFileReader.Read(path) : PointFileReader.Read(path, ColumnMap.FromPreset(preset));
                Check(r.Points.Count == n, $"reader {f} {preset} '{delim}' header={header}: {r.Points.Count}/{n}");
                for (int i = 0; i < Math.Min(n, r.Points.Count); i++)
                {
                    var a = pts[i]; var b = r.Points[i];
                    bool ok = Math.Abs(a.E - b.E) < 1e-6 && Math.Abs(a.N - b.N) < 1e-6;
                    if (preset.Contains('Z')) ok &= Math.Abs(a.Z - b.Z) < 1e-6;
                    if (preset.Contains('P')) ok &= a.Name == b.Name;
                    if (preset.Contains('D')) ok &= a.Desc == b.Desc;
                    Check(ok, $"reader {f} {preset} '{delim}' row {i}");
                }
                // automatic column detection for the common headerless survey layouts
                if (!header && (preset == "PNEZD" || preset == "PENZD" || preset == "PNEZ" || preset == "PENZ") && n >= 3)
                {
                    var auto = PointFileReader.Read(path);
                    bool ok = auto.Points.Count == n && Enumerable.Range(0, n).All(i => Math.Abs(auto.Points[i].E - pts[i].E) < 1e-6 && Math.Abs(auto.Points[i].N - pts[i].N) < 1e-6);
                    Check(ok, $"auto-detect {f} {preset} '{delim}' → {auto.Map?.Label}");
                }
            }
        }

        // ------------------------------------------------------------------ 12
        private static void Writers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sx_stress_writers");
            Directory.CreateDirectory(dir);
            var utm = CrsCatalog.Find("EPSG:32640");
            var toW = new CoordinateTransformer(utm, CrsCatalog.Wgs84Geo);
            var back = new CoordinateTransformer(CrsCatalog.Wgs84Geo, utm);
            var formats = new[] { ExportFormat.Csv, ExportFormat.Txt, ExportFormat.LandXml, ExportFormat.Kml, ExportFormat.GeoJson };
            string[] ext = { ".csv", ".txt", ".xml", ".kml", ".geojson" };
            for (int s = 0; s < 3000; s++)
            {
                int k = s % formats.Length;
                var pts = Enumerable.Range(0, R.Next(1, 60)).Select(i => new SurveyPoint
                {
                    Name = "P" + i + (R.Next(5) == 0 ? "-نقطة" : ""), E = Math.Round(U(220000, 400000), 3), N = Math.Round(U(2650000, 2800000), 3),
                    Z = Math.Round(U(0, 100), 3), Desc = R.Next(4) == 0 ? "كود, \"x\"" : "EG",
                }).ToList();
                string path = Path.Combine(dir, "w" + (s % 20) + ext[k]);
                PointWriters.Write(path, formats[k], pts, "PNEZD", 3, toW);
                var r = PointFileReader.Read(path);
                Check(r.Points.Count == pts.Count, $"writer {s} {formats[k]} count");
                for (int i = 0; i < Math.Min(pts.Count, r.Points.Count); i++)
                {
                    double e = r.Points[i].E, n = r.Points[i].N;
                    if (r.IsGeographic) back.Transform(e, n, out e, out n);
                    bool ok = Math.Abs(e - pts[i].E) < 0.002 && Math.Abs(n - pts[i].N) < 0.002 && Math.Abs(r.Points[i].Z - pts[i].Z) < 0.002 && r.Points[i].Name == pts[i].Name;
                    if (formats[k] != ExportFormat.Kml) ok &= r.Points[i].Desc == pts[i].Desc;
                    Check(ok, $"writer {s} {formats[k]} point {i}");
                }
            }
        }

        // ------------------------------------------------------------------ 13
        private static void Xer()
        {
            for (int s = 0; s < 5000; s++)
            {
                int n = R.Next(1, 60);
                var sb = new StringBuilder("ERMHDR\t21.12\n%T\tPROJECT\n%F\tproj_id\tproj_short_name\tlast_recalc_date\n%R\t1\tPRJ" + s + "\t2026-11-23 08:00\n");
                sb.Append("%T\tPROJWBS\n%F\twbs_id\twbs_name\n%R\t9\tWBS-A\n");
                sb.Append("%T\tTASK\n%F\ttask_id\twbs_id\ttask_code\ttask_name\tstatus_code\tphys_complete_pct\ttarget_start_date\ttarget_end_date\n");
                var ids = new List<(string id, double pct)>();
                for (int i = 0; i < n; i++)
                {
                    string id = "A" + (1000 + i * 10);
                    double pct = Math.Round(U(0, 100), 1);
                    ids.Add((id, pct));
                    sb.Append($"%R\t{i}\t9\t{id}\tActivity {i} – عمل\tTK_Active\t{pct.ToString(Inv)}\t2026-11-{1 + R.Next(20):00} 08:00\t2026-12-{1 + R.Next(27):00} 17:00\n");
                }
                sb.Append("%E\n");
                var sch = PrimaveraReader.ParseXer(sb.ToString());
                Check(sch.Activities.Count == n && sch.ProjectName == "PRJ" + s, $"xer {s} header");
                for (int i = 0; i < Math.Min(n, sch.Activities.Count); i++)
                    Check(sch.Activities[i].Id == ids[i].id && Math.Abs(sch.Activities[i].P6Percent - ids[i].pct) < 1e-9 && sch.Activities[i].Wbs == "WBS-A" && sch.Activities[i].Start.HasValue,
                        $"xer {s} activity {i}");
                var rt = P6Schedule.Deserialize(sch.Serialize());
                Check(rt.Activities.Count == n, $"xer {s} serialize");
            }
        }

        // ------------------------------------------------------------------ 14
        private static object RandomJson(int depth)
        {
            int t = depth > 3 ? R.Next(4) : R.Next(6);
            switch (t)
            {
                case 0: return Math.Round(U(-1e6, 1e6), R.Next(0, 6));
                case 1: return RandomString();
                case 2: return R.Next(2) == 0;
                case 3: return null;
                case 4: { var l = new List<object>(); int n = R.Next(0, 5); for (int i = 0; i < n; i++) l.Add(RandomJson(depth + 1)); return l; }
                default: { var d = new Dictionary<string, object>(); int n = R.Next(0, 5); for (int i = 0; i < n; i++) d["k" + i + RandomString()] = RandomJson(depth + 1); return d; }
            }
        }

        private static string RandomString()
        {
            const string chars = "abcXYZ019 \"\\/\n\t\rقطعوردمالـًًٌٍَُِّْ€✓{}[],:";
            int n = R.Next(0, 12);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(chars[R.Next(chars.Length)]);
            return sb.ToString();
        }

        private static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is double x && b is double y) return Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Abs(x));
            if (a is string s1 && b is string s2) return s1 == s2;
            if (a is bool b1 && b is bool b2) return b1 == b2;
            if (a is List<object> l1 && b is List<object> l2) return l1.Count == l2.Count && l1.Zip(l2, Same).All(v => v);
            if (a is Dictionary<string, object> d1 && b is Dictionary<string, object> d2)
                return d1.Count == d2.Count && d1.All(kv => d2.TryGetValue(kv.Key, out var v) && Same(kv.Value, v));
            return false;
        }

        private static void Json()
        {
            for (int i = 0; i < 100_000; i++)
            {
                var o = RandomJson(0);
                object back = null;
                try { back = MiniJson.Parse(JsonWriter.Write(o)); } catch { }
                Check(Same(o, back), $"json {i}: {JsonWriter.Write(o)}");
            }
        }

        // ------------------------------------------------------------------ 15
        private static void Intents()
        {
            var phrases = new (string text, string cmd)[]
            {
                ("قطع وردم", "SXCUTFILL"), ("القطع والردم", "SXCUTFILL"), ("حفر وردم", "SXCUTFILL"), ("cut fill", "SXCUTFILL"),
                ("حصر كميات", "SXQTO"), ("حصر", "SXQTO"), ("take off", "SXQTO"), ("بريمافيرا", "SXP6"), ("primavera", "SXP6"), ("p6", "SXP6"),
                ("فحص المناسيب", "SXLEVELCHECK"), ("level check", "SXLEVELCHECK"), ("جدول التوقيع", "SXSETOUT"), ("setting out", "SXSETOUT"),
                ("تراكمي", "SXCUMULATIVE"), ("القطاعات", "SXSECTIONS"), ("كنتور", "SXSURFACE"), ("استيراد", "SXIMPORT"), ("تحويل الإحداثيات", "SXCONVERT"),
            };
            string[] fillers = { "افتح", "عايز", "اعمل", "لو سمحت", "please", "open", "ممكن", "شغل", "يا شلبي", "" };
            for (int i = 0; i < 100_000; i++)
            {
                var (text, cmd) = phrases[R.Next(phrases.Length)];
                string msg = (fillers[R.Next(fillers.Length)] + " " + text + " " + (R.Next(3) == 0 ? fillers[R.Next(fillers.Length)] : "")).Trim();
                if (R.Next(4) == 0) msg = msg.Replace('ا', 'أ');
                var m = LocalIntents.Match(msg);
                Check(m != null && m.Command == cmd, $"intent '{msg}' → {m?.Command ?? "AI"} (expected {cmd})");
            }
            // random garbage must never crash, and long sentences must go to the AI
            for (int i = 0; i < 100_000; i++)
            {
                string msg = RandomString() + " " + RandomString();
                bool ok = true;
                try { LocalIntents.Match(msg); } catch { ok = false; }
                if (i % 2 == 0)
                {
                    string longMsg = string.Join(" ", Enumerable.Range(0, 8 + R.Next(10)).Select(_ => "كلمة")) + " قطع وردم";
                    ok &= LocalIntents.Match(longMsg) == null;
                }
                Check(ok, $"intent fuzz {i}");
            }
        }
    }
}
