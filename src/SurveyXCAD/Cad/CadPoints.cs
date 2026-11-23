using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Collects survey points from drawing objects: SurveyX points, blocks, POINTs, Civil 3D COGO points,
    /// and vertices of 3D polylines / lines / polylines with elevation (densified for design surfaces).</summary>
    public static class CadPoints
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static SelectionFilter AnyPointLike() => new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, "INSERT,POINT,POLYLINE,LWPOLYLINE,LINE,AECC_COGO_POINT,AECC_FEATURE_LINE")
        });

        public static SelectionFilter ClosedBoundary() => new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,CIRCLE")
        });

        public static List<SurveyPoint> FromIds(IEnumerable<ObjectId> ids, double densify = 0)
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var res = new List<SurveyPoint>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var simple = new List<ObjectId>();
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (ent is BlockReference || ent is DBPoint) { simple.Add(id); continue; }
                    if (TryCogo(ent, out var cp)) { res.Add(cp); continue; }
                    var verts = Vertices3d(tr, ent);
                    if (verts == null || verts.Count == 0) continue;
                    if (densify > 0) verts = Densify(verts, densify);
                    int k = 1;
                    foreach (var v in verts)
                        res.Add(new SurveyPoint { E = v.X, N = v.Y, Z = v.Z, Name = "V" + (k++).ToString(Inv), Desc = ent.Layer });
                }
                res.AddRange(PointDrawer.ReadPoints(tr, simple));
                tr.Commit();
            }
            return res;
        }

        private static List<Point3d> Vertices3d(Transaction tr, Entity ent)
        {
            var l = new List<Point3d>();
            switch (ent)
            {
                case Polyline3d p3:
                    foreach (ObjectId vid in p3)
                        if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v) l.Add(v.Position);
                    if (p3.Closed && l.Count > 1) l.Add(l[0]);
                    break;
                case Polyline2d p2:
                    foreach (ObjectId vid in p2)
                        if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v) l.Add(new Point3d(v.Position.X, v.Position.Y, p2.Elevation));
                    break;
                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++) l.Add(pl.GetPoint3dAt(i));
                    if (pl.Closed && l.Count > 1) l.Add(l[0]);
                    break;
                case Line ln:
                    l.Add(ln.StartPoint); l.Add(ln.EndPoint);
                    break;
                default:
                    // Civil 3D feature lines and other curves: sample through the generic Curve API
                    if (ent is Curve cv)
                    {
                        try
                        {
                            double len = cv.GetDistanceAtParameter(cv.EndParam);
                            int n = Math.Max(2, Math.Min(5000, (int)(len / 1.0) + 1));
                            for (int i = 0; i < n; i++) l.Add(cv.GetPointAtDist(len * i / (n - 1)));
                        }
                        catch { }
                    }
                    break;
            }
            return l;
        }

        private static List<Point3d> Densify(List<Point3d> v, double step)
        {
            var r = new List<Point3d> { v[0] };
            for (int i = 1; i < v.Count; i++)
            {
                double d = v[i - 1].DistanceTo(v[i]);
                int n = (int)Math.Floor(d / step);
                for (int k = 1; k < n; k++)
                {
                    double f = (double)k / n;
                    r.Add(v[i - 1] + (v[i] - v[i - 1]) * f);
                }
                r.Add(v[i]);
            }
            return r;
        }

        /// <summary>Reads a Civil 3D COGO point by reflection (no compile-time Civil 3D dependency).</summary>
        private static bool TryCogo(Entity ent, out SurveyPoint p)
        {
            p = null;
            var t = ent.GetType();
            if (t.Name != "CogoPoint") return false;
            try
            {
                double e = Convert.ToDouble(t.GetProperty("Easting").GetValue(ent, null), Inv);
                double n = Convert.ToDouble(t.GetProperty("Northing").GetValue(ent, null), Inv);
                double z = Convert.ToDouble(t.GetProperty("Elevation").GetValue(ent, null), Inv);
                string num = Convert.ToString(t.GetProperty("PointNumber")?.GetValue(ent, null), Inv);
                string name = Convert.ToString(t.GetProperty("PointName")?.GetValue(ent, null), Inv);
                string desc = Convert.ToString(t.GetProperty("RawDescription")?.GetValue(ent, null), Inv);
                p = new SurveyPoint { E = e, N = n, Z = z, Name = string.IsNullOrWhiteSpace(name) ? num : name, Desc = desc ?? "", HasZ = !double.IsNaN(z) && Math.Abs(z) < 1e10 };
                return true;
            }
            catch { return false; }
        }

        /// <summary>Boundary polygon (XY) from a closed polyline / circle.</summary>
        public static List<(double x, double y)> Boundary(ObjectId id)
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var res = new List<(double, double)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (ent is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var seg = pl.GetSegmentType(i);
                        if (seg == SegmentType.Arc)
                        {
                            var arc = pl.GetArcSegment2dAt(i);
                            var s = arc.GetSamplePoints(16);
                            foreach (var q in s.Take(s.Length - 1)) res.Add((q.X, q.Y));
                        }
                        else { var q = pl.GetPoint2dAt(i); res.Add((q.X, q.Y)); }
                    }
                }
                else if (ent != null)
                {
                    double len = ent.GetDistanceAtParameter(ent.EndParam);
                    for (int i = 0; i < 200; i++) { var q = ent.GetPointAtDist(len * i / 200); res.Add((q.X, q.Y)); }
                }
                tr.Commit();
            }
            return res;
        }

        /// <summary>Alignment polyline (XY) from a polyline / line / Civil alignment (any Curve).</summary>
        public static List<(double x, double y)> Alignment(ObjectId id)
        {
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var res = new List<(double, double)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Curve c)
                {
                    if (c is Polyline pl && Enumerable.Range(0, pl.NumberOfVertices).All(i => pl.GetSegmentType(i) != SegmentType.Arc))
                        for (int i = 0; i < pl.NumberOfVertices; i++) { var q = pl.GetPoint2dAt(i); res.Add((q.X, q.Y)); }
                    else
                    {
                        double len = c.GetDistanceAtParameter(c.EndParam);
                        int n = Math.Max(2, Math.Min(20000, (int)(len / 0.5) + 1));
                        for (int i = 0; i < n; i++) { var q = c.GetPointAtDist(len * i / (n - 1)); res.Add((q.X, q.Y)); }
                    }
                }
                tr.Commit();
            }
            return res;
        }
    }
}
