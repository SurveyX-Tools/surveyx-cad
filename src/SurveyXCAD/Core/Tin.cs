using System;
using System.Collections.Generic;
using System.Linq;

namespace SurveyX.Core
{
    /// <summary>Delaunay triangulation (port of the Delaunator sweep-hull algorithm, O(n log n)).</summary>
    public static class Delaunay
    {
        private static readonly double Eps = Math.Pow(2, -52);

        /// <summary>Returns triangle vertex indices (3 per triangle, counter-clockwise) for points xs/ys.</summary>
        public static int[] Triangulate(double[] xs, double[] ys)
        {
            int n = xs.Length;
            if (n < 3) return new int[0];
            int maxTri = Math.Max(2 * n - 5, 0);
            var T = new int[maxTri * 3];
            var H = new int[maxTri * 3];
            int hashSize = (int)Math.Ceiling(Math.Sqrt(n));
            var hullPrev = new int[n]; var hullNext = new int[n]; var hullTri = new int[n];
            var hullHash = new int[hashSize];
            for (int i = 0; i < hashSize; i++) hullHash[i] = -1;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (xs[i] < minX) minX = xs[i]; if (xs[i] > maxX) maxX = xs[i];
                if (ys[i] < minY) minY = ys[i]; if (ys[i] > maxY) maxY = ys[i];
            }
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            int i0 = 0, i1 = 0, i2 = 0;
            double minD = double.MaxValue;
            for (int i = 0; i < n; i++) { double d = D2(xs[i], ys[i], cx, cy); if (d < minD) { i0 = i; minD = d; } }
            minD = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (i == i0) continue;
                double d = D2(xs[i], ys[i], xs[i0], ys[i0]);
                if (d < minD && d > 0) { i1 = i; minD = d; }
            }
            double minR = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                double r = CircumR(xs[i0], ys[i0], xs[i1], ys[i1], xs[i], ys[i]);
                if (r < minR) { i2 = i; minR = r; }
            }
            if (minR == double.MaxValue || double.IsInfinity(minR)) return new int[0]; // all collinear

            if (Orient(xs[i0], ys[i0], xs[i1], ys[i1], xs[i2], ys[i2])) { int t = i1; i1 = i2; i2 = t; }
            CircumC(xs[i0], ys[i0], xs[i1], ys[i1], xs[i2], ys[i2], out double ccx, out double ccy);

            var ids = new int[n]; var dists = new double[n];
            for (int i = 0; i < n; i++) { ids[i] = i; dists[i] = D2(xs[i], ys[i], ccx, ccy); }
            Array.Sort(dists, ids);

            int hullStart = i0;
            hullNext[i0] = hullPrev[i2] = i1;
            hullNext[i1] = hullPrev[i0] = i2;
            hullNext[i2] = hullPrev[i1] = i0;
            hullTri[i0] = 0; hullTri[i1] = 1; hullTri[i2] = 2;

            int HashKey(double x, double y)
            {
                double dx = x - ccx, dy = y - ccy;
                double s = Math.Abs(dx) + Math.Abs(dy);
                double p = s > 0 ? dx / s : 0;
                double a = (dy > 0 ? 3 - p : 1 + p) / 4;
                return ((int)Math.Floor(a * hashSize) % hashSize + hashSize) % hashSize;
            }
            hullHash[HashKey(xs[i0], ys[i0])] = i0;
            hullHash[HashKey(xs[i1], ys[i1])] = i1;
            hullHash[HashKey(xs[i2], ys[i2])] = i2;

            int tl = 0;
            void Link(int a, int b) { H[a] = b; if (b != -1) H[b] = a; }
            int Add(int a0, int a1, int a2, int a, int b, int c)
            {
                int t = tl;
                T[t] = a0; T[t + 1] = a1; T[t + 2] = a2;
                Link(t, a); Link(t + 1, b); Link(t + 2, c);
                tl += 3;
                return t;
            }
            var stack = new Stack<int>();
            int Legalize(int a)
            {
                int ar;
                while (true)
                {
                    int b = H[a];
                    int a0 = a - a % 3;
                    ar = a0 + (a + 2) % 3;
                    if (b == -1)
                    {
                        if (stack.Count == 0) break;
                        a = stack.Pop();
                        continue;
                    }
                    int b0 = b - b % 3;
                    int al = a0 + (a + 1) % 3;
                    int bl = b0 + (b + 2) % 3;
                    int p0 = T[ar], pr = T[a], pl = T[al], p1 = T[bl];
                    if (InCircle(xs[p0], ys[p0], xs[pr], ys[pr], xs[pl], ys[pl], xs[p1], ys[p1]))
                    {
                        T[a] = p1; T[b] = p0;
                        int hbl = H[bl];
                        if (hbl == -1)
                        {
                            int e = hullStart;
                            do
                            {
                                if (hullTri[e] == bl) { hullTri[e] = a; break; }
                                e = hullPrev[e];
                            } while (e != hullStart);
                        }
                        Link(a, hbl);
                        Link(b, H[ar]);
                        Link(ar, bl);
                        int br = b0 + (b + 1) % 3;
                        stack.Push(br);
                    }
                    else
                    {
                        if (stack.Count == 0) break;
                        a = stack.Pop();
                    }
                }
                return ar;
            }

            Add(i0, i1, i2, -1, -1, -1);
            double xp = 0, yp = 0;
            for (int k = 0; k < n; k++)
            {
                int i = ids[k];
                double x = xs[i], y = ys[i];
                if (k > 0 && Math.Abs(x - xp) <= Eps && Math.Abs(y - yp) <= Eps) continue;
                xp = x; yp = y;
                if (i == i0 || i == i1 || i == i2) continue;

                int start = 0;
                int key = HashKey(x, y);
                for (int j = 0; j < hashSize; j++)
                {
                    start = hullHash[(key + j) % hashSize];
                    if (start != -1 && start != hullNext[start]) break;
                }
                start = hullPrev[start];
                int e = start, q;
                while (true)
                {
                    q = hullNext[e];
                    if (Orient(x, y, xs[e], ys[e], xs[q], ys[q])) break;
                    e = q;
                    if (e == start) { e = -1; break; }
                }
                if (e == -1) continue; // near-duplicate point

                int t = Add(e, i, hullNext[e], -1, -1, hullTri[e]);
                hullTri[i] = Legalize(t + 2);
                hullTri[e] = t;

                int nn = hullNext[e];
                while (true)
                {
                    q = hullNext[nn];
                    if (!Orient(x, y, xs[nn], ys[nn], xs[q], ys[q])) break;
                    t = Add(nn, i, q, hullTri[i], -1, hullTri[nn]);
                    hullTri[i] = Legalize(t + 2);
                    hullNext[nn] = nn;
                    nn = q;
                }
                if (e == start)
                {
                    while (true)
                    {
                        q = hullPrev[e];
                        if (!Orient(x, y, xs[q], ys[q], xs[e], ys[e])) break;
                        t = Add(q, i, e, -1, hullTri[e], hullTri[q]);
                        Legalize(t + 2);
                        hullTri[q] = t;
                        hullNext[e] = e;
                        e = q;
                    }
                }
                hullStart = hullPrev[i] = e;
                hullNext[e] = hullPrev[nn] = i;
                hullNext[i] = nn;
                hullHash[HashKey(x, y)] = i;
                hullHash[HashKey(xs[e], ys[e])] = e;
            }
            var result = new int[tl];
            Array.Copy(T, result, tl);
            return result;
        }

        private static double D2(double ax, double ay, double bx, double by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

        private static bool Orient(double px, double py, double qx, double qy, double rx, double ry) =>
            (qy - py) * (rx - qx) - (qx - px) * (ry - qy) < 0;

        private static bool InCircle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            double dx = ax - px, dy = ay - py, ex = bx - px, ey = by - py, fx = cx - px, fy = cy - py;
            double ap = dx * dx + dy * dy, bp = ex * ex + ey * ey, cp = fx * fx + fy * fy;
            return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0;
        }

        private static double CircumR(double ax, double ay, double bx, double by, double cx, double cy)
        {
            double dx = bx - ax, dy = by - ay, ex = cx - ax, ey = cy - ay;
            double bl = dx * dx + dy * dy, cl = ex * ex + ey * ey, den = dx * ey - dy * ex;
            if (den == 0) return double.MaxValue;
            double d = 0.5 / den;
            double x = (ey * bl - dy * cl) * d, y = (dx * cl - ex * bl) * d;
            return x * x + y * y;
        }

        private static void CircumC(double ax, double ay, double bx, double by, double cx, double cy, out double ox, out double oy)
        {
            double dx = bx - ax, dy = by - ay, ex = cx - ax, ey = cy - ay;
            double bl = dx * dx + dy * dy, cl = ex * ex + ey * ey, d = 0.5 / (dx * ey - dy * ex);
            ox = ax + (ey * bl - dy * cl) * d;
            oy = ay + (dx * cl - ex * bl) * d;
        }
    }

    /// <summary>Triangulated surface with fast elevation lookup. Coordinates are stored relative to an origin for precision.</summary>
    public sealed class Tin
    {
        public double OX { get; }
        public double OY { get; }
        public double[] X { get; }   // local (minus origin)
        public double[] Y { get; }
        public double[] Z { get; }
        public int[] Tri { get; private set; }
        public int Count => X.Length;
        public int TriangleCount => Tri.Length / 3;
        public double MinX, MinY, MaxX, MaxY, MinZ, MaxZ; // world

        private int[][] _buckets;
        private double _bucketSize;
        private int _bnx, _bny;

        public Tin(IList<SurveyPoint> pts, double maxEdge = 0)
        {
            var use = pts.Where(p => p.HasZ).ToList();
            if (use.Count < 3) throw new InvalidOperationException(Lang.T("TinNeeds3"));
            OX = Math.Floor(use.Average(p => p.E));
            OY = Math.Floor(use.Average(p => p.N));
            X = use.Select(p => p.E - OX).ToArray();
            Y = use.Select(p => p.N - OY).ToArray();
            Z = use.Select(p => p.Z).ToArray();
            MinX = use.Min(p => p.E); MaxX = use.Max(p => p.E);
            MinY = use.Min(p => p.N); MaxY = use.Max(p => p.N);
            MinZ = Z.Min(); MaxZ = Z.Max();
            Tri = Delaunay.Triangulate(X, Y);
            if (Tri.Length == 0) throw new InvalidOperationException(Lang.T("TinCollinear"));
            if (maxEdge > 0) FilterLongEdges(maxEdge);
            BuildIndex();
        }

        private void FilterLongEdges(double maxEdge)
        {
            double m2 = maxEdge * maxEdge;
            var keep = new List<int>(Tri.Length);
            for (int t = 0; t < Tri.Length; t += 3)
            {
                int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
                if (E2(a, b) <= m2 && E2(b, c) <= m2 && E2(c, a) <= m2) { keep.Add(a); keep.Add(b); keep.Add(c); }
            }
            Tri = keep.ToArray();
        }

        private double E2(int a, int b) => (X[a] - X[b]) * (X[a] - X[b]) + (Y[a] - Y[b]) * (Y[a] - Y[b]);

        private void BuildIndex()
        {
            double w = MaxX - MinX, h = MaxY - MinY;
            int nt = Math.Max(1, TriangleCount);
            _bucketSize = Math.Max(Math.Sqrt(Math.Max(w * h, 1e-6) / nt) * 2, 1e-3);
            _bnx = Math.Max(1, (int)Math.Ceiling(w / _bucketSize) + 1);
            _bny = Math.Max(1, (int)Math.Ceiling(h / _bucketSize) + 1);
            var lists = new List<int>[_bnx * _bny];
            double lx0 = MinX - OX, ly0 = MinY - OY;
            for (int t = 0; t < Tri.Length; t += 3)
            {
                int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
                double x0 = Math.Min(X[a], Math.Min(X[b], X[c])), x1 = Math.Max(X[a], Math.Max(X[b], X[c]));
                double y0 = Math.Min(Y[a], Math.Min(Y[b], Y[c])), y1 = Math.Max(Y[a], Math.Max(Y[b], Y[c]));
                int i0 = Clamp((int)((x0 - lx0) / _bucketSize), _bnx), i1 = Clamp((int)((x1 - lx0) / _bucketSize), _bnx);
                int j0 = Clamp((int)((y0 - ly0) / _bucketSize), _bny), j1 = Clamp((int)((y1 - ly0) / _bucketSize), _bny);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        int k = j * _bnx + i;
                        (lists[k] ?? (lists[k] = new List<int>())).Add(t);
                    }
            }
            _buckets = lists.Select(l => l?.ToArray()).ToArray();
        }

        private static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        /// <summary>Elevation at world XY, or NaN outside the surface.</summary>
        public double ZAt(double wx, double wy)
        {
            double x = wx - OX, y = wy - OY;
            int i = (int)Math.Floor((wx - MinX) / _bucketSize), j = (int)Math.Floor((wy - MinY) / _bucketSize);
            if (i < 0 || j < 0 || i >= _bnx || j >= _bny) return double.NaN;
            var list = _buckets[j * _bnx + i];
            if (list == null) return double.NaN;
            foreach (int t in list)
                if (Interp(t, x, y, out double z)) return z;
            return double.NaN;
        }

        private bool Interp(int t, double x, double y, out double z)
        {
            int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
            double d = (Y[b] - Y[c]) * (X[a] - X[c]) + (X[c] - X[b]) * (Y[a] - Y[c]);
            z = double.NaN;
            if (Math.Abs(d) < 1e-12) return false;
            double l1 = ((Y[b] - Y[c]) * (x - X[c]) + (X[c] - X[b]) * (y - Y[c])) / d;
            double l2 = ((Y[c] - Y[a]) * (x - X[c]) + (X[a] - X[c]) * (y - Y[c])) / d;
            double l3 = 1 - l1 - l2;
            const double tol = -1e-9;
            if (l1 < tol || l2 < tol || l3 < tol) return false;
            z = l1 * Z[a] + l2 * Z[b] + l3 * Z[c];
            return true;
        }

        /// <summary>Planimetric area covered by the triangles.</summary>
        public double Area()
        {
            double s = 0;
            for (int t = 0; t < Tri.Length; t += 3)
            {
                int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
                s += Math.Abs((X[b] - X[a]) * (Y[c] - Y[a]) - (X[c] - X[a]) * (Y[b] - Y[a])) / 2;
            }
            return s;
        }

        /// <summary>Samples the surface on a regular grid (cell centres).</summary>
        public ElevationGrid ToGrid(double x0, double y0, int nx, int ny, double cell)
        {
            var g = new ElevationGrid(x0, y0, nx, ny, cell);
            for (int t = 0; t < Tri.Length; t += 3)
            {
                int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
                double wx0 = Math.Min(X[a], Math.Min(X[b], X[c])) + OX, wx1 = Math.Max(X[a], Math.Max(X[b], X[c])) + OX;
                double wy0 = Math.Min(Y[a], Math.Min(Y[b], Y[c])) + OY, wy1 = Math.Max(Y[a], Math.Max(Y[b], Y[c])) + OY;
                int i0 = Math.Max(0, (int)Math.Ceiling((wx0 - x0) / cell - 0.5)), i1 = Math.Min(nx - 1, (int)Math.Floor((wx1 - x0) / cell - 0.5));
                int j0 = Math.Max(0, (int)Math.Ceiling((wy0 - y0) / cell - 0.5)), j1 = Math.Min(ny - 1, (int)Math.Floor((wy1 - y0) / cell - 0.5));
                for (int j = j0; j <= j1; j++)
                {
                    double ly = y0 + (j + 0.5) * cell - OY;
                    for (int i = i0; i <= i1; i++)
                    {
                        double lx = x0 + (i + 0.5) * cell - OX;
                        if (float.IsNaN(g.Z[j * nx + i]) && Interp(t, lx, ly, out double z)) g.Z[j * nx + i] = (float)z;
                    }
                }
            }
            return g;
        }

        /// <summary>World-coordinate triangle corners (for drawing 3D faces).</summary>
        public IEnumerable<(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz)> Triangles()
        {
            for (int t = 0; t < Tri.Length; t += 3)
            {
                int a = Tri[t], b = Tri[t + 1], c = Tri[t + 2];
                yield return (X[a] + OX, Y[a] + OY, Z[a], X[b] + OX, Y[b] + OY, Z[b], X[c] + OX, Y[c] + OY, Z[c]);
            }
        }

        /// <summary>Contour polylines (world XY) at the given interval, chained from triangle segments.</summary>
        public List<(double level, List<(double x, double y)> pts)> Contours(double interval, double baseLevel = 0)
        {
            var result = new List<(double, List<(double, double)>)>();
            if (interval <= 0) return result;
            double first = Math.Ceiling((MinZ - baseLevel) / interval) * interval + baseLevel;
            for (double lev = first; lev <= MaxZ; lev += interval)
            {
                var segs = new List<((double, double) p, (double, double) q)>();
                for (int t = 0; t < Tri.Length; t += 3)
                {
                    int[] v = { Tri[t], Tri[t + 1], Tri[t + 2] };
                    var cut = new List<(double, double)>(2);
                    for (int e = 0; e < 3; e++)
                    {
                        int a = v[e], b = v[(e + 1) % 3];
                        if (a > b) { int tmp = a; a = b; b = tmp; } // canonical edge direction → identical shared points
                        double za = Z[a] == lev ? lev + 1e-9 : Z[a], zb = Z[b] == lev ? lev + 1e-9 : Z[b];
                        if ((za - lev) * (zb - lev) < 0)
                        {
                            double f = (lev - za) / (zb - za);
                            cut.Add((Math.Round(X[a] + f * (X[b] - X[a]) + OX, 6), Math.Round(Y[a] + f * (Y[b] - Y[a]) + OY, 6)));
                        }
                    }
                    if (cut.Count == 2 && !cut[0].Equals(cut[1])) segs.Add((cut[0], cut[1]));
                }
                foreach (var chain in Chain(segs)) result.Add((lev, chain));
            }
            return result;
        }

        private static List<List<(double, double)>> Chain(List<((double, double) p, (double, double) q)> segs)
        {
            var adj = new Dictionary<(double, double), List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (!adj.TryGetValue(segs[i].p, out var a)) adj[segs[i].p] = a = new List<int>();
                a.Add(i);
                if (!adj.TryGetValue(segs[i].q, out var b)) adj[segs[i].q] = b = new List<int>();
                b.Add(i);
            }
            var used = new bool[segs.Count];
            var chains = new List<List<(double, double)>>();
            for (int s = 0; s < segs.Count; s++)
            {
                if (used[s]) continue;
                used[s] = true;
                var line = new LinkedList<(double, double)>();
                line.AddLast(segs[s].p); line.AddLast(segs[s].q);
                // extend forward then backward
                for (int dir = 0; dir < 2; dir++)
                {
                    while (true)
                    {
                        var end = dir == 0 ? line.Last.Value : line.First.Value;
                        int next = -1;
                        foreach (int k in adj[end]) if (!used[k]) { next = k; break; }
                        if (next < 0) break;
                        used[next] = true;
                        var other = segs[next].p.Equals(end) ? segs[next].q : segs[next].p;
                        if (dir == 0) line.AddLast(other); else line.AddFirst(other);
                    }
                }
                chains.Add(line.ToList());
            }
            return chains;
        }
    }

    /// <summary>Regular grid of elevations (cell centres). NaN = no data.</summary>
    public sealed class ElevationGrid
    {
        public double X0, Y0, Cell;
        public int NX, NY;
        public float[] Z;

        public ElevationGrid(double x0, double y0, int nx, int ny, double cell)
        {
            X0 = x0; Y0 = y0; NX = nx; NY = ny; Cell = cell;
            Z = new float[nx * ny];
            for (int i = 0; i < Z.Length; i++) Z[i] = float.NaN;
        }

        public double CX(int i) => X0 + (i + 0.5) * Cell;
        public double CY(int j) => Y0 + (j + 0.5) * Cell;

        public static void Frame(double minX, double minY, double maxX, double maxY, double cell, out double x0, out double y0, out int nx, out int ny)
        {
            x0 = Math.Floor(minX / cell) * cell;
            y0 = Math.Floor(minY / cell) * cell;
            nx = Math.Max(1, (int)Math.Ceiling((maxX - x0) / cell));
            ny = Math.Max(1, (int)Math.Ceiling((maxY - y0) / cell));
        }
    }
}
