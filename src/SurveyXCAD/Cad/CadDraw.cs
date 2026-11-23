using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Drawing helpers for maps, legends, surfaces, contours and sections.</summary>
    public static class CadDraw
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

        public static void ClearLayer(string layer)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(layer))
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase)) { e.UpgradeOpen(); e.Erase(); }
                    }
                }
                tr.Commit();
            }
        }

        private static BlockTableRecord Ms(Transaction tr) =>
            (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);

        private static void Add(Transaction tr, BlockTableRecord ms, Entity e, string layer)
        {
            e.Layer = layer;
            ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }

        public static DBText Txt(string s, Point3d at, double h, ObjectId style, TextHorizontalMode hm = TextHorizontalMode.TextLeft, TextVerticalMode vm = TextVerticalMode.TextBase, double rot = 0)
        {
            var t = new DBText { TextString = s ?? "", Height = h, TextStyleId = style, Rotation = rot };
            t.Position = at;
            if (hm != TextHorizontalMode.TextLeft || vm != TextVerticalMode.TextBase)
            {
                t.HorizontalMode = hm; t.VerticalMode = vm; t.AlignmentPoint = at;
            }
            return t;
        }

        /// <summary>Colour map from a grid; horizontal runs of the same class are merged into one SOLID.</summary>
        public static int GridMap(ElevationGrid g, Func<float, int> classOf, (byte r, byte g, byte b)[] colors, string layer, double z = 0)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 7);
                var ms = Ms(tr);
                var cols = colors.Select(c => Color.FromRgb(c.r, c.g, c.b)).ToArray();
                for (int j = 0; j < g.NY; j++)
                {
                    int i = 0;
                    while (i < g.NX)
                    {
                        float v = g.Z[j * g.NX + i];
                        if (float.IsNaN(v)) { i++; continue; }
                        int cls = classOf(v);
                        int s = i;
                        while (i < g.NX && !float.IsNaN(g.Z[j * g.NX + i]) && classOf(g.Z[j * g.NX + i]) == cls) i++;
                        double x0 = g.X0 + s * g.Cell, x1 = g.X0 + i * g.Cell, y0 = g.Y0 + j * g.Cell, y1 = y0 + g.Cell;
                        var so = new Solid(new Point3d(x0, y0, z), new Point3d(x1, y0, z), new Point3d(x0, y1, z), new Point3d(x1, y1, z));
                        so.Color = cols[Math.Max(0, Math.Min(cols.Length - 1, cls))];
                        Add(tr, ms, so, layer);
                        n++;
                    }
                }
                tr.Commit();
            }
            return n;
        }

        public static void Legend(Point3d at, string title, IList<((byte r, byte g, byte b) c, string label)> items, double h, string layer)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 7);
                var st = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = Ms(tr);
                Add(tr, ms, Txt(title, at, h * 1.3, st), layer);
                double y = at.Y - h * 2.5;
                foreach (var (c, label) in items)
                {
                    var so = new Solid(new Point3d(at.X, y, 0), new Point3d(at.X + h * 2, y, 0), new Point3d(at.X, y + h * 1.4, 0), new Point3d(at.X + h * 2, y + h * 1.4, 0))
                    { Color = Color.FromRgb(c.r, c.g, c.b) };
                    Add(tr, ms, so, layer);
                    Add(tr, ms, Txt(label, new Point3d(at.X + h * 2.6, y + h * 0.2, 0), h, st), layer);
                    y -= h * 2.0;
                }
                tr.Commit();
            }
        }

        public static int Faces(Tin tin, string layer)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 8);
                var ms = Ms(tr);
                foreach (var t in tin.Triangles())
                {
                    var a = new Point3d(t.ax, t.ay, t.az); var b = new Point3d(t.bx, t.by, t.bz); var c = new Point3d(t.cx, t.cy, t.cz);
                    Add(tr, ms, new Face(a, b, c, c, true, true, true, true), layer);
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        public static int Contours(Tin tin, double interval, int majorEvery, string layerMinor, string layerMajor, bool label, double textH)
        {
            int n = 0;
            var lines = tin.Contours(interval);
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layerMinor, 8);
                PointDrawer.EnsureLayer(tr, Db, layerMajor, 1);
                var st = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = Ms(tr);
                foreach (var (level, pts) in lines)
                {
                    if (pts.Count < 2) continue;
                    bool major = majorEvery > 0 && Math.Abs(Math.Round(level / interval) % majorEvery) < 1e-9;
                    var pl = new Polyline();
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].x, pts[i].y), 0, 0, 0);
                    pl.Elevation = level;
                    if (pts.Count > 2 && pts[0].x == pts[pts.Count - 1].x && pts[0].y == pts[pts.Count - 1].y) pl.Closed = true;
                    Add(tr, ms, pl, major ? layerMajor : layerMinor);
                    if (major && label && pts.Count > 3)
                    {
                        int m = pts.Count / 2;
                        double ang = Math.Atan2(pts[m + 1].y - pts[m].y, pts[m + 1].x - pts[m].x);
                        if (ang > Math.PI / 2) ang -= Math.PI; else if (ang < -Math.PI / 2) ang += Math.PI;
                        var t = Txt(level.ToString("0.0#", Inv), new Point3d(pts[m].x, pts[m].y, level), textH, st, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, ang);
                        Add(tr, ms, t, layerMajor);
                    }
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>Coloured point labels (e.g. +0.012 in green / −0.034 in red).</summary>
        public static void DiffLabels(IList<(double x, double y, double z, string text, short aci)> items, double h, string layer)
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 7);
                var st = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = Ms(tr);
                foreach (var it in items)
                {
                    var c = new Circle(new Point3d(it.x, it.y, 0), Vector3d.ZAxis, h * 0.35) { ColorIndex = it.aci };
                    Add(tr, ms, c, layer);
                    var t = Txt(it.text, new Point3d(it.x + h * 0.6, it.y - h * 0.5, 0), h, st);
                    t.ColorIndex = it.aci;
                    Add(tr, ms, t, layer);
                }
                tr.Commit();
            }
        }

        // ------------------------------------------------------------------ sections
        private static readonly short[] SurfaceAci = { 3, 1, 5, 6, 30 }; // existing green, design red, …

        /// <summary>Long section drawing: frame, grid, one line per surface and a level table. Returns width.</summary>
        public static double LongSection(Point3d o, List<(double ch, double[] z)> data, string[] names, double vex, double startCh, double h, string layer)
        {
            var all = data.SelectMany(d => d.z).Where(z => !double.IsNaN(z)).ToList();
            if (all.Count == 0) throw new InvalidOperationException(Lang.T("NoSectionData"));
            double datum = Math.Floor(all.Min() - 1);
            double top = Math.Ceiling(all.Max() + 1);
            double L = data.Last().ch;
            double Y(double z) => o.Y + (z - datum) * vex;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 7);
                PointDrawer.EnsureLayer(tr, Db, layer + "_GRID", 9);
                var st = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = Ms(tr);
                // grid
                for (double z = datum; z <= top + 1e-9; z += 1)
                {
                    Add(tr, ms, new Line(new Point3d(o.X, Y(z), 0), new Point3d(o.X + L, Y(z), 0)), layer + "_GRID");
                    Add(tr, ms, Txt(z.ToString("0.0", Inv), new Point3d(o.X - h, Y(z), 0), h, st, TextHorizontalMode.TextRight, TextVerticalMode.TextVerticalMid), layer);
                }
                Add(tr, ms, Txt(Lang.En("Datum") + " " + datum.ToString("0.000", Inv) + "   V×" + vex.ToString("0.#", Inv), new Point3d(o.X, Y(top) + h * 2, 0), h * 1.2, st), layer);
                // surfaces
                for (int k = 0; k < data[0].z.Length; k++)
                {
                    var pl = new Polyline();
                    int vi = 0;
                    foreach (var d in data)
                    {
                        if (double.IsNaN(d.z[k])) continue;
                        pl.AddVertexAt(vi++, new Point2d(o.X + d.ch, Y(d.z[k])), 0, 0, 0);
                    }
                    if (vi >= 2) { pl.ColorIndex = SurfaceAci[k % SurfaceAci.Length]; Add(tr, ms, pl, layer); }
                    else pl.Dispose();
                }
                // table rows under the profile: chainage + levels
                double rowH = h * 2.2;
                double ty = o.Y - rowH;
                var rowsTitle = new List<string> { Lang.En("Chainage") };
                rowsTitle.AddRange(names);
                for (int r = 0; r < rowsTitle.Count; r++)
                {
                    double yy = ty - r * rowH;
                    Add(tr, ms, new Line(new Point3d(o.X, yy, 0), new Point3d(o.X + L, yy, 0)), layer + "_GRID");
                    var t = Txt(rowsTitle[r], new Point3d(o.X - h, yy + rowH / 2, 0), h, st, TextHorizontalMode.TextRight, TextVerticalMode.TextVerticalMid);
                    if (r > 0) t.ColorIndex = SurfaceAci[(r - 1) % SurfaceAci.Length];
                    Add(tr, ms, t, layer);
                }
                double lastX = double.MinValue;
                foreach (var d in data)
                {
                    double x = o.X + d.ch;
                    if (x - lastX < h * 1.6) continue; // avoid overlapping text
                    lastX = x;
                    Add(tr, ms, new Line(new Point3d(x, o.Y, 0), new Point3d(x, Y(top), 0)), layer + "_GRID");
                    Add(tr, ms, Txt(Geo2D.FormatChainage(d.ch, startCh), new Point3d(x, ty + rowH / 2, 0), h * 0.8, st, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, Math.PI / 2), layer);
                    for (int k = 0; k < d.z.Length; k++)
                        if (!double.IsNaN(d.z[k]))
                        {
                            var t = Txt(d.z[k].ToString("0.000", Inv), new Point3d(x, ty - (k + 1) * rowH + rowH / 2, 0), h * 0.8, st, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, Math.PI / 2);
                            t.ColorIndex = SurfaceAci[k % SurfaceAci.Length];
                            Add(tr, ms, t, layer);
                        }
                }
                tr.Commit();
            }
            return L;
        }

        /// <summary>Cross sections laid out in a grid of sheets; returns number drawn.</summary>
        public static int CrossSections(Point3d o, List<SectionLine> secs, string[] names, double vex, double startCh, double h, int perRow, string layer)
        {
            if (secs.Count == 0) return 0;
            double width = secs[0].Offsets.Last() - secs[0].Offsets.First();
            double cellW = width + h * 14, cellH = 0;
            var ranges = secs.Select(s =>
            {
                var zz = s.Z.SelectMany(a => a).Where(z => !double.IsNaN(z)).ToList();
                return zz.Count == 0 ? (0.0, 1.0) : (zz.Min(), zz.Max());
            }).ToList();
            cellH = ranges.Max(r => (r.Item2 - r.Item1 + 2) * vex) + h * 10;
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, layer, 7);
                PointDrawer.EnsureLayer(tr, Db, layer + "_GRID", 9);
                var st = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = Ms(tr);
                for (int i = 0; i < secs.Count; i++)
                {
                    var s = secs[i];
                    int col = i % perRow, row = i / perRow;
                    double datum = Math.Floor(ranges[i].Item1 - 1);
                    double ox = o.X + col * cellW - s.Offsets.First(), oy = o.Y - row * cellH;
                    double Y(double z) => oy + (z - datum) * vex;
                    double top = Math.Ceiling(ranges[i].Item2 + 1);
                    for (double z = datum; z <= top + 1e-9; z += 1)
                    {
                        Add(tr, ms, new Line(new Point3d(ox + s.Offsets.First(), Y(z), 0), new Point3d(ox + s.Offsets.Last(), Y(z), 0)), layer + "_GRID");
                        Add(tr, ms, Txt(z.ToString("0.0", Inv), new Point3d(ox + s.Offsets.First() - h * 0.5, Y(z), 0), h * 0.7, st, TextHorizontalMode.TextRight, TextVerticalMode.TextVerticalMid), layer);
                    }
                    Add(tr, ms, new Line(new Point3d(ox, Y(datum), 0), new Point3d(ox, Y(top), 0)) { ColorIndex = 2 }, layer);
                    for (int k = 0; k < s.Z.Length; k++)
                    {
                        var pl = new Polyline();
                        int vi = 0;
                        for (int j = 0; j < s.Offsets.Length; j++)
                            if (!double.IsNaN(s.Z[k][j])) pl.AddVertexAt(vi++, new Point2d(ox + s.Offsets[j], Y(s.Z[k][j])), 0, 0, 0);
                        if (vi >= 2) { pl.ColorIndex = SurfaceAci[k % SurfaceAci.Length]; Add(tr, ms, pl, layer); } else pl.Dispose();
                    }
                    string title = Lang.En("Chainage") + " " + Geo2D.FormatChainage(s.Chainage, startCh);
                    Add(tr, ms, Txt(title, new Point3d(ox, Y(datum) - h * 2, 0), h, st, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid), layer);
                    if (s.Z.Length >= 2)
                        Add(tr, ms, Txt(Lang.En("Cut") + " " + s.CutArea.ToString("0.00", Inv) + " m²   " + Lang.En("Fill") + " " + s.FillArea.ToString("0.00", Inv) + " m²",
                            new Point3d(ox, Y(datum) - h * 3.6, 0), h * 0.8, st, TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid), layer);
                    n++;
                }
                tr.Commit();
            }
            return n;
        }
    }
}
