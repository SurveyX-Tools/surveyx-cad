using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SurveyX.Core
{
    public static class StationOffset
    {
        /// <summary>Chainage along the alignment and offset (+ right / − left) of a point, by nearest projection.</summary>
        public static (double station, double offset, double distance) Compute(IList<(double x, double y)> align, double px, double py)
        {
            double best = double.MaxValue, bestSt = 0, bestOff = 0, acc = 0;
            for (int i = 1; i < align.Count; i++)
            {
                double ax = align[i - 1].x, ay = align[i - 1].y, bx = align[i].x, by = align[i].y;
                double dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy, L = Math.Sqrt(L2);
                if (L <= 0) continue;
                double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / L2));
                double qx = ax + t * dx, qy = ay + t * dy;
                double dist = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                if (dist < best - 1e-12)
                {
                    best = dist;
                    bestSt = acc + t * L;
                    double cross = dx * (py - ay) - dy * (px - ax); // + left of direction
                    bestOff = cross > 0 ? -dist : dist;
                }
                acc += L;
            }
            return (bestSt, bestOff, best);
        }
    }

    /// <summary>History of daily cut / fill results (one CSV in the settings folder) for cumulative / monthly reports.</summary>
    public static class CutFillLog
    {
        public sealed class Entry
        {
            public DateTime Date;
            public string Area = "";
            public double Cut, Fill, AreaM2, Cell;
            public string Ogl = "", Survey = "";
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string PathOverride;
        public static string FilePath => PathOverride ?? Path.Combine(Settings.Folder, "cutfill_history.csv");

        public static void Add(Entry e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            bool exists = File.Exists(FilePath);
            using (var w = new StreamWriter(FilePath, true, new UTF8Encoding(!exists)))
            {
                if (!exists) w.WriteLine("Date,Area,Cut_m3,Fill_m3,Area_m2,Cell_m,OGL,Survey");
                w.WriteLine(string.Join(",", e.Date.ToString("yyyy-MM-dd", Inv), Q(e.Area), e.Cut.ToString("F3", Inv), e.Fill.ToString("F3", Inv),
                    e.AreaM2.ToString("F2", Inv), e.Cell.ToString("0.###", Inv), Q(e.Ogl), Q(e.Survey)));
            }
        }

        private static string Q(string s) => (s ?? "").Replace(",", " ").Replace("\n", " ");

        public static List<Entry> Read()
        {
            var list = new List<Entry>();
            if (!File.Exists(FilePath)) return list;
            foreach (var line in File.ReadAllLines(FilePath).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length < 6) continue;
                if (!DateTime.TryParseExact(c[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d)) continue;
                var e = new Entry { Date = d, Area = c[1] };
                double.TryParse(c[2], NumberStyles.Float, Inv, out e.Cut);
                double.TryParse(c[3], NumberStyles.Float, Inv, out e.Fill);
                double.TryParse(c[4], NumberStyles.Float, Inv, out e.AreaM2);
                double.TryParse(c[5], NumberStyles.Float, Inv, out e.Cell);
                if (c.Length > 6) e.Ogl = c[6];
                if (c.Length > 7) e.Survey = c[7];
                list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// Entries filtered by area / date. When the same area was calculated more than once on the same day,
        /// only the last calculation of that day is kept (a re-run replaces the earlier one).
        /// </summary>
        public static List<Entry> Filter(string area, DateTime? from, DateTime? to)
        {
            var q = Read().Where(e => (string.IsNullOrWhiteSpace(area) || string.Equals(e.Area.Trim(), area.Trim(), StringComparison.OrdinalIgnoreCase))
                                   && (!from.HasValue || e.Date >= from.Value.Date) && (!to.HasValue || e.Date <= to.Value.Date));
            return q.GroupBy(e => (e.Date, e.Area.Trim().ToUpperInvariant())).Select(g => g.Last()).OrderBy(e => e.Date).ThenBy(e => e.Area).ToList();
        }
    }

    /// <summary>Quantity take-off totals per layer.</summary>
    public sealed class QtoRow
    {
        public string Layer = "";
        public int Lines, Polylines, ClosedAreas, Blocks, Texts, Points, Other;
        public double Length, Area;
        public Dictionary<string, int> BlockNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int Total => Lines + Polylines + ClosedAreas + Blocks + Texts + Points + Other;
    }
}
