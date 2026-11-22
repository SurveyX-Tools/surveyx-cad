using System;
using System.Collections.Generic;
using System.Linq;

namespace SurveyX.Core
{
    public sealed class AnalysisSummary
    {
        public int Total, DuplicateNames, DuplicateCoords, OutliersZ, OutliersXY, MissingZ;
        public double MinE, MaxE, MinN, MaxN, MinZ, MaxZ, MeanZ;
        public bool HasIssues => DuplicateNames + DuplicateCoords + OutliersZ + OutliersXY + MissingZ > 0;
    }

    /// <summary>Detects duplicate names / coordinates and gross outliers using robust statistics (median + MAD).</summary>
    public static class PointAnalysis
    {
        public static AnalysisSummary Analyze(IList<SurveyPoint> pts, double coordTolerance = 0.001, bool geographic = false)
        {
            var s = new AnalysisSummary { Total = pts.Count };
            foreach (var p in pts) p.Issues &= PointIssue.MissingZ;
            if (pts.Count == 0) return s;

            // duplicate names
            var byName = new Dictionary<string, List<SurveyPoint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pts)
            {
                if (!byName.TryGetValue(p.Name ?? "", out var l)) byName[p.Name ?? ""] = l = new List<SurveyPoint>();
                l.Add(p);
            }
            foreach (var l in byName.Values.Where(v => v.Count > 1))
                foreach (var p in l) p.Issues |= PointIssue.DuplicateName;

            // duplicate coordinates (grid hashing)
            double tol = geographic ? coordTolerance / 111000.0 : coordTolerance;
            var grid = new Dictionary<(long, long), List<SurveyPoint>>();
            foreach (var p in pts)
            {
                var key = ((long)Math.Floor(p.E / tol), (long)Math.Floor(p.N / tol));
                bool dup = false;
                for (long dx = -1; dx <= 1 && !dup; dx++)
                    for (long dy = -1; dy <= 1 && !dup; dy++)
                        if (grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var cell))
                            foreach (var q in cell)
                                if (Math.Abs(q.E - p.E) <= tol && Math.Abs(q.N - p.N) <= tol)
                                {
                                    q.Issues |= PointIssue.DuplicateCoords; p.Issues |= PointIssue.DuplicateCoords; dup = true; break;
                                }
                if (!grid.TryGetValue(key, out var c2)) grid[key] = c2 = new List<SurveyPoint>();
                c2.Add(p);
            }

            // Z outliers: |z - median| > max(5 * 1.4826 * MAD, 2 m)  (2 m floor avoids flagging flat sites)
            var zs = pts.Where(p => p.HasZ).Select(p => p.Z).ToList();
            if (zs.Count >= 8)
            {
                double med = Median(zs);
                double mad = Median(zs.Select(z => Math.Abs(z - med)).ToList()) * 1.4826;
                double lim = Math.Max(5 * mad, 2.0);
                foreach (var p in pts.Where(p => p.HasZ && Math.Abs(p.Z - med) > lim)) p.Issues |= PointIssue.OutlierZ;
            }

            // XY outliers: distance from median centre > max(8 * median distance, 500 m)
            if (pts.Count >= 8)
            {
                double mE = Median(pts.Select(p => p.E).ToList()), mN = Median(pts.Select(p => p.N).ToList());
                var d = pts.Select(p => Math.Sqrt((p.E - mE) * (p.E - mE) + (p.N - mN) * (p.N - mN))).ToList();
                double md = Median(d);
                double lim = Math.Max(8 * md, geographic ? 0.005 : 500);
                for (int i = 0; i < pts.Count; i++) if (d[i] > lim) pts[i].Issues |= PointIssue.OutlierXY;
            }

            s.DuplicateNames = pts.Count(p => (p.Issues & PointIssue.DuplicateName) != 0);
            s.DuplicateCoords = pts.Count(p => (p.Issues & PointIssue.DuplicateCoords) != 0);
            s.OutliersZ = pts.Count(p => (p.Issues & PointIssue.OutlierZ) != 0);
            s.OutliersXY = pts.Count(p => (p.Issues & PointIssue.OutlierXY) != 0);
            s.MissingZ = pts.Count(p => (p.Issues & PointIssue.MissingZ) != 0);
            s.MinE = pts.Min(p => p.E); s.MaxE = pts.Max(p => p.E);
            s.MinN = pts.Min(p => p.N); s.MaxN = pts.Max(p => p.N);
            if (zs.Count > 0) { s.MinZ = zs.Min(); s.MaxZ = zs.Max(); s.MeanZ = zs.Average(); }
            return s;
        }

        public static double Median(List<double> v)
        {
            if (v.Count == 0) return 0;
            var a = v.ToArray();
            Array.Sort(a);
            int m = a.Length / 2;
            return a.Length % 2 == 1 ? a[m] : (a[m - 1] + a[m]) / 2;
        }

        public static string IssueText(PointIssue i)
        {
            var parts = new List<string>();
            if ((i & PointIssue.DuplicateName) != 0) parts.Add(Lang.T("IssueDupName"));
            if ((i & PointIssue.DuplicateCoords) != 0) parts.Add(Lang.T("IssueDupXY"));
            if ((i & PointIssue.OutlierZ) != 0) parts.Add(Lang.T("IssueOutZ"));
            if ((i & PointIssue.OutlierXY) != 0) parts.Add(Lang.T("IssueOutXY"));
            if ((i & PointIssue.MissingZ) != 0) parts.Add(Lang.T("IssueNoZ"));
            return string.Join(" | ", parts);
        }
    }
}
