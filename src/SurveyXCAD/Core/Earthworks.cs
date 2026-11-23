using System;
using System.Collections.Generic;
using System.Linq;

namespace SurveyX.Core
{
    public static class Geo2D
    {
        public static bool InPolygon(IList<(double x, double y)> poly, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public static double Area(IList<(double x, double y)> poly)
        {
            double s = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) s += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);
            return Math.Abs(s) / 2;
        }

        public static double Length(IList<(double x, double y)> pl)
        {
            double s = 0;
            for (int i = 1; i < pl.Count; i++) s += Dist(pl[i - 1], pl[i]);
            return s;
        }

        public static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        /// <summary>Point and unit direction at a chainage along a polyline.</summary>
        public static (double x, double y, double dx, double dy) At(IList<(double x, double y)> pl, double ch)
        {
            double acc = 0;
            for (int i = 1; i < pl.Count; i++)
            {
                double L = Dist(pl[i - 1], pl[i]);
                if (L <= 0) continue;
                if (ch <= acc + L || i == pl.Count - 1)
                {
                    double f = Math.Max(0, Math.Min(1, (ch - acc) / L));
                    double dx = (pl[i].x - pl[i - 1].x) / L, dy = (pl[i].y - pl[i - 1].y) / L;
                    return (pl[i - 1].x + f * (pl[i].x - pl[i - 1].x), pl[i - 1].y + f * (pl[i].y - pl[i - 1].y), dx, dy);
                }
                acc += L;
            }
            return (pl[0].x, pl[0].y, 1, 0);
        }

        /// <summary>Chainages at a regular interval plus every vertex, sorted and de-duplicated.</summary>
        public static List<double> Stations(IList<(double x, double y)> pl, double interval, double startCh = 0, bool includeVertices = true)
        {
            double L = Length(pl);
            var set = new SortedSet<double>();
            if (interval > 0)
                for (double c = 0; c < L - 1e-6; c += interval) set.Add(Math.Round(c, 6));
            set.Add(Math.Round(L, 6));
            if (includeVertices)
            {
                double acc = 0;
                for (int i = 1; i < pl.Count; i++) { acc += Dist(pl[i - 1], pl[i]); set.Add(Math.Round(acc, 6)); }
            }
            set.Add(0);
            return set.ToList();
        }

        public static string FormatChainage(double ch, double startCh = 0)
        {
            double c = ch + startCh;
            int km = (int)Math.Floor(c / 1000);
            double m = c - km * 1000;
            return km + "+" + m.ToString("000.000", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ===================================================================== cut / fill
    public sealed class PointDiff
    {
        public SurveyPoint Point;
        public double RefZ = double.NaN; // OGL / design level at the point
        public double Diff => double.IsNaN(RefZ) ? double.NaN : Point.Z - RefZ; // + fill / above design, − cut / below
    }

    public sealed class CutFillResult
    {
        public double Cell;
        public double CutVolume, FillVolume, CutArea, FillArea, BalancedArea;
        public double MaxCut, MaxFill; // depths (positive numbers)
        public double NetVolume => FillVolume - CutVolume;
        public double TotalArea => CutArea + FillArea + BalancedArea;
        public ElevationGrid DiffGrid;  // new − OGL per cell (NaN outside)
        public List<PointDiff> Points = new List<PointDiff>();
        public double MinX, MinY, MaxX, MaxY;
    }

    public static class CutFill
    {
        public static double AutoCell(double width, double height)
        {
            double area = Math.Max(1, width * height);
            double cell = Math.Sqrt(area / 2_000_000.0);     // ≤ ~2M cells
            double[] nice = { 0.1, 0.2, 0.25, 0.5, 1, 2, 2.5, 5, 10 };
            foreach (var n in nice) if (n >= cell) return n;
            return Math.Ceiling(cell);
        }

        /// <param name="balanceTol">|difference| below this counts as "on level" (no cut / no fill) for areas only.</param>
        public static CutFillResult Compute(Tin ogl, Tin surveyed, IList<SurveyPoint> surveyedPoints,
            IList<(double x, double y)> boundary, double cell, double balanceTol = 0.005)
        {
            double minX = Math.Max(ogl.MinX, surveyed.MinX), maxX = Math.Min(ogl.MaxX, surveyed.MaxX);
            double minY = Math.Max(ogl.MinY, surveyed.MinY), maxY = Math.Min(ogl.MaxY, surveyed.MaxY);
            if (boundary != null && boundary.Count >= 3)
            {
                minX = Math.Max(minX, boundary.Min(p => p.x)); maxX = Math.Min(maxX, boundary.Max(p => p.x));
                minY = Math.Max(minY, boundary.Min(p => p.y)); maxY = Math.Min(maxY, boundary.Max(p => p.y));
            }
            if (minX >= maxX || minY >= maxY) throw new InvalidOperationException(Lang.T("NoOverlap"));
            if (cell <= 0) cell = AutoCell(maxX - minX, maxY - minY);

            ElevationGrid.Frame(minX, minY, maxX, maxY, cell, out double x0, out double y0, out int nx, out int ny);
            var g1 = ogl.ToGrid(x0, y0, nx, ny, cell);
            var g2 = surveyed.ToGrid(x0, y0, nx, ny, cell);
            var r = new CutFillResult { Cell = cell, DiffGrid = new ElevationGrid(x0, y0, nx, ny, cell) };
            double ca = cell * cell;
            r.MinX = double.MaxValue; r.MinY = double.MaxValue; r.MaxX = double.MinValue; r.MaxY = double.MinValue;
            bool useB = boundary != null && boundary.Count >= 3;
            for (int j = 0; j < ny; j++)
            {
                double cy = g1.CY(j);
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    float a = g1.Z[k], b = g2.Z[k];
                    if (float.IsNaN(a) || float.IsNaN(b)) continue;
                    double cx = g1.CX(i);
                    if (useB && !Geo2D.InPolygon(boundary, cx, cy)) continue;
                    double d = b - (double)a;
                    r.DiffGrid.Z[k] = (float)d;
                    if (d > 0) { r.FillVolume += d * ca; if (d > r.MaxFill) r.MaxFill = d; }
                    else if (d < 0) { r.CutVolume += -d * ca; if (-d > r.MaxCut) r.MaxCut = -d; }
                    if (d > balanceTol) r.FillArea += ca;
                    else if (d < -balanceTol) r.CutArea += ca;
                    else r.BalancedArea += ca;
                    if (cx < r.MinX) r.MinX = cx; if (cx > r.MaxX) r.MaxX = cx;
                    if (cy < r.MinY) r.MinY = cy; if (cy > r.MaxY) r.MaxY = cy;
                }
            }
            if (r.TotalArea <= 0) throw new InvalidOperationException(Lang.T("NoOverlap"));
            if (surveyedPoints != null)
                foreach (var p in surveyedPoints.Where(p => p.HasZ))
                {
                    if (useB && !Geo2D.InPolygon(boundary, p.E, p.N)) continue;
                    r.Points.Add(new PointDiff { Point = p, RefZ = ogl.ZAt(p.E, p.N) });
                }
            return r;
        }
    }

    // ===================================================================== level check
    public enum CheckStatus { Pass, Fail, NoDesign }

    public sealed class LevelCheckRow
    {
        public SurveyPoint Point;
        public double DesignZ = double.NaN;
        public double Diff => double.IsNaN(DesignZ) ? double.NaN : Point.Z - DesignZ;
        public CheckStatus Status;
    }

    public sealed class LevelCheckResult
    {
        public List<LevelCheckRow> Rows = new List<LevelCheckRow>();
        public double TolPlus, TolMinus;
        public int Pass => Rows.Count(r => r.Status == CheckStatus.Pass);
        public int Fail => Rows.Count(r => r.Status == CheckStatus.Fail);
        public int NoDesign => Rows.Count(r => r.Status == CheckStatus.NoDesign);
        public double PassPercent => Pass + Fail > 0 ? 100.0 * Pass / (Pass + Fail) : 0;
        public IEnumerable<double> Diffs => Rows.Where(r => r.Status != CheckStatus.NoDesign).Select(r => r.Diff);
        public double MaxAbove => Diffs.DefaultIfEmpty(0).Max();
        public double MaxBelow => Diffs.DefaultIfEmpty(0).Min();
        public double Mean => Diffs.DefaultIfEmpty(0).Average();
        public double StdDev
        {
            get
            {
                var d = Diffs.ToList();
                if (d.Count < 2) return 0;
                double m = d.Average();
                return Math.Sqrt(d.Sum(x => (x - m) * (x - m)) / (d.Count - 1));
            }
        }
    }

    public static class LevelCheck
    {
        /// <summary>Design level from a design surface (TIN).</summary>
        public static LevelCheckResult AgainstSurface(IList<SurveyPoint> asBuilt, Tin design, double tolPlus, double tolMinus)
            => Build(asBuilt, p => design.ZAt(p.E, p.N), tolPlus, tolMinus);

        /// <summary>Design level by matching point names (design list: same name as the as-built point).</summary>
        public static LevelCheckResult AgainstNames(IList<SurveyPoint> asBuilt, IList<SurveyPoint> designPts, double tolPlus, double tolMinus)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in designPts.Where(d => d.HasZ)) map[d.Name ?? ""] = d.Z;
            return Build(asBuilt, p => map.TryGetValue(p.Name ?? "", out double z) ? z : double.NaN, tolPlus, tolMinus);
        }

        /// <summary>Design level from the nearest design point within a search radius.</summary>
        public static LevelCheckResult AgainstNearest(IList<SurveyPoint> asBuilt, IList<SurveyPoint> designPts, double radius, double tolPlus, double tolMinus)
        {
            var d = designPts.Where(p => p.HasZ).ToList();
            double cs = Math.Max(radius, 0.01);
            var grid = new Dictionary<(long, long), List<SurveyPoint>>();
            foreach (var p in d)
            {
                var k = ((long)Math.Floor(p.E / cs), (long)Math.Floor(p.N / cs));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<SurveyPoint>();
                l.Add(p);
            }
            return Build(asBuilt, p =>
            {
                long ki = (long)Math.Floor(p.E / cs), kj = (long)Math.Floor(p.N / cs);
                double best = double.MaxValue, z = double.NaN;
                for (long a = -1; a <= 1; a++)
                    for (long b = -1; b <= 1; b++)
                        if (grid.TryGetValue((ki + a, kj + b), out var l))
                            foreach (var q in l)
                            {
                                double dd = (q.E - p.E) * (q.E - p.E) + (q.N - p.N) * (q.N - p.N);
                                if (dd < best && dd <= radius * radius) { best = dd; z = q.Z; }
                            }
                return z;
            }, tolPlus, tolMinus);
        }

        private static LevelCheckResult Build(IList<SurveyPoint> pts, Func<SurveyPoint, double> designZ, double tolPlus, double tolMinus)
        {
            var r = new LevelCheckResult { TolPlus = Math.Abs(tolPlus), TolMinus = Math.Abs(tolMinus) };
            foreach (var p in pts.Where(p => p.HasZ))
            {
                var row = new LevelCheckRow { Point = p, DesignZ = designZ(p) };
                if (double.IsNaN(row.DesignZ)) row.Status = CheckStatus.NoDesign;
                else
                {
                    double d = Math.Round(row.Diff, 6);
                    row.Status = d <= r.TolPlus + 1e-9 && d >= -r.TolMinus - 1e-9 ? CheckStatus.Pass : CheckStatus.Fail;
                }
                r.Rows.Add(row);
            }
            return r;
        }
    }

    // ===================================================================== sections
    public sealed class SectionLine
    {
        public double Chainage;
        public double CX, CY, DirX, DirY;      // centre and along-direction
        public double[] Offsets;               // − left, + right
        public double[][] Z;                   // per surface, per offset (NaN = no data)
        public double CutArea, FillArea;       // between surface 0 (existing) and surface 1 (design / new)
    }

    public sealed class SectionVolumes
    {
        public List<(double ch, double cutArea, double fillArea, double cutVol, double fillVol)> Rows = new List<(double, double, double, double, double)>();
        public double TotalCut => Rows.Sum(r => r.cutVol);
        public double TotalFill => Rows.Sum(r => r.fillVol);
    }

    public static class Sections
    {
        /// <summary>Long section: elevations of each surface at each chainage.</summary>
        public static List<(double ch, double[] z)> LongSection(IList<(double x, double y)> align, IList<Tin> surfaces, double interval)
        {
            var res = new List<(double, double[])>();
            foreach (var ch in Geo2D.Stations(align, interval))
            {
                var p = Geo2D.At(align, ch);
                res.Add((ch, surfaces.Select(s => s.ZAt(p.x, p.y)).ToArray()));
            }
            return res;
        }

        public static List<SectionLine> CrossSections(IList<(double x, double y)> align, IList<Tin> surfaces, double interval,
            double left, double right, double step)
        {
            var list = new List<SectionLine>();
            if (step <= 0) step = 1;
            var offs = new List<double>();
            for (double o = -Math.Abs(left); o <= Math.Abs(right) + 1e-9; o += step) offs.Add(Math.Round(o, 6));
            foreach (var ch in Geo2D.Stations(align, interval, 0, includeVertices: false))
            {
                var p = Geo2D.At(align, ch);
                // right-hand normal of the direction (dx,dy) is (dy,-dx)
                var s = new SectionLine { Chainage = ch, CX = p.x, CY = p.y, DirX = p.dx, DirY = p.dy, Offsets = offs.ToArray(), Z = new double[surfaces.Count][] };
                for (int k = 0; k < surfaces.Count; k++)
                {
                    s.Z[k] = new double[offs.Count];
                    for (int i = 0; i < offs.Count; i++)
                        s.Z[k][i] = surfaces[k].ZAt(p.x + offs[i] * p.dy, p.y - offs[i] * p.dx);
                }
                if (surfaces.Count >= 2) Areas(s);
                list.Add(s);
            }
            return list;
        }

        private static void Areas(SectionLine s)
        {
            double cut = 0, fill = 0;
            for (int i = 1; i < s.Offsets.Length; i++)
            {
                double e0 = s.Z[0][i - 1], e1 = s.Z[0][i], d0 = s.Z[1][i - 1], d1 = s.Z[1][i];
                if (double.IsNaN(e0) || double.IsNaN(e1) || double.IsNaN(d0) || double.IsNaN(d1)) continue;
                double w = s.Offsets[i] - s.Offsets[i - 1];
                double h0 = d0 - e0, h1 = d1 - e1; // + fill, − cut
                if (h0 >= 0 && h1 >= 0) fill += (h0 + h1) / 2 * w;
                else if (h0 <= 0 && h1 <= 0) cut += -(h0 + h1) / 2 * w;
                else
                {
                    double f = h0 / (h0 - h1); // crossing
                    double wa = w * f, wb = w - wa;
                    if (h0 > 0) { fill += h0 / 2 * wa; cut += -h1 / 2 * wb; }
                    else { cut += -h0 / 2 * wa; fill += h1 / 2 * wb; }
                }
            }
            s.CutArea = cut; s.FillArea = fill;
        }

        /// <summary>Average end-area volumes between consecutive cross sections.</summary>
        public static SectionVolumes EndArea(IList<SectionLine> secs)
        {
            var v = new SectionVolumes();
            for (int i = 0; i < secs.Count; i++)
            {
                double cv = 0, fv = 0;
                if (i > 0)
                {
                    double L = secs[i].Chainage - secs[i - 1].Chainage;
                    cv = (secs[i].CutArea + secs[i - 1].CutArea) / 2 * L;
                    fv = (secs[i].FillArea + secs[i - 1].FillArea) / 2 * L;
                }
                v.Rows.Add((secs[i].Chainage, secs[i].CutArea, secs[i].FillArea, cv, fv));
            }
            return v;
        }
    }

    // ===================================================================== colour scales
    public static class ColorScale
    {
        /// <summary>Elevation ramp blue→green→yellow→red, t in [0,1]. Returns RGB.</summary>
        public static (byte r, byte g, byte b) Elevation(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            (double, double, double)[] stops = { (0.19, 0.30, 0.80), (0.10, 0.65, 0.85), (0.30, 0.75, 0.30), (0.98, 0.85, 0.20), (0.90, 0.30, 0.15) };
            double x = t * (stops.Length - 1);
            int i = Math.Min(stops.Length - 2, (int)Math.Floor(x));
            double f = x - i;
            var a = stops[i]; var b = stops[i + 1];
            return ((byte)(255 * (a.Item1 + f * (b.Item1 - a.Item1))), (byte)(255 * (a.Item2 + f * (b.Item2 - a.Item2))), (byte)(255 * (a.Item3 + f * (b.Item3 - a.Item3))));
        }

        /// <summary>Class limits for cut / fill depth (new − OGL, metres). 9 classes: 4 cut, on-level, 4 fill.</summary>
        public static readonly double[] CutFillBreaks = { -1.0, -0.5, -0.2, -0.05, 0.05, 0.2, 0.5, 1.0 };

        public static readonly (byte r, byte g, byte b)[] CutFillColors =
        {
            (183, 28, 28), (229, 57, 53), (239, 154, 154), (255, 224, 178), // cut: deep → shallow
            (129, 199, 132),                                                // on level
            (187, 222, 251), (100, 181, 246), (30, 136, 229), (13, 71, 161), // fill: shallow → deep
        };

        public static int CutFillClass(double d)
        {
            for (int i = 0; i < CutFillBreaks.Length; i++) if (d < CutFillBreaks[i]) return i;
            return CutFillBreaks.Length;
        }

        public static string CutFillLabel(int c, bool english = false)
        {
            Func<string, string> L = k => english ? Lang.En(k) : Lang.T(k);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("0.00", inv);
            string Rng(double a, double b) => english || !Lang.IsArabic ? F(a) + " – " + F(b) : "\u202A" + F(a) + " – " + F(b) + "\u202C";
            if (c == 0) return L("Cut") + " > " + F(-CutFillBreaks[0]);
            if (c < 4) return L("Cut") + " " + Rng(-CutFillBreaks[c], -CutFillBreaks[c - 1]);
            if (c == 4) return L("OnLevel") + " ±" + F(CutFillBreaks[4]);
            if (c < 8) return L("Fill") + " " + Rng(CutFillBreaks[c - 1], CutFillBreaks[c]);
            return L("Fill") + " > " + F(CutFillBreaks[7]);
        }
    }
}
