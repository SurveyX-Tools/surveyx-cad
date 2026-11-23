using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Keeps the imported Primavera schedule inside the DWG and links activities to drawing objects (XData).</summary>
    public static class P6Store
    {
        public const string AppName = "SURVEYX_P6";
        private const string DictKey = "SURVEYX_P6_SCHEDULE";
        public static string StatusLayer => PointDrawer.Layer("P6_STATUS");

        private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

        // ------------------------------------------------------------ schedule
        public static void Save(P6Schedule s)
        {
            string text = s.Serialize();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(Db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                Xrecord xr;
                if (nod.Contains(DictKey)) xr = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForWrite);
                else { xr = new Xrecord(); nod.SetAt(DictKey, xr); tr.AddNewlyCreatedDBObject(xr, true); }
                var rb = new ResultBuffer();
                const int chunk = 500; // keeps each string well under the DXF string limit, even for Arabic text
                for (int i = 0; i < text.Length; i += chunk)
                    rb.Add(new TypedValue((int)DxfCode.Text, text.Substring(i, Math.Min(chunk, text.Length - i))));
                xr.Data = rb;
                tr.Commit();
            }
        }

        public static P6Schedule Load()
        {
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(Db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DictKey)) return null;
                var xr = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForRead);
                var sb = new StringBuilder();
                if (xr.Data != null) foreach (TypedValue tv in xr.Data) sb.Append(tv.Value as string);
                tr.Commit();
                return P6Schedule.Deserialize(sb.ToString());
            }
        }

        // ------------------------------------------------------------ object links
        private static void EnsureRegApp(Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(Db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(AppName)) return;
            rat.UpgradeOpen();
            var r = new RegAppTableRecord { Name = AppName };
            rat.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
        }

        public static int Link(IEnumerable<ObjectId> ids, string activityId, double percent = 0)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                EnsureRegApp(tr);
                foreach (var id in ids)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    if (string.Equals(e.Layer, StatusLayer, StringComparison.OrdinalIgnoreCase)) continue;
                    e.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, activityId),
                        new TypedValue((int)DxfCode.ExtendedDataReal, percent));
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        public static int Unlink(IEnumerable<ObjectId> ids)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (e.GetXDataForApplication(AppName) == null) continue;
                    e.UpgradeOpen();
                    e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName)); // removes app data
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>Sets progress (0-100) on linked objects. Returns number updated.</summary>
        public static int SetPercent(IEnumerable<ObjectId> ids, double percent)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    var rb = e.GetXDataForApplication(AppName);
                    if (rb == null) continue;
                    var vals = rb.AsArray();
                    if (vals.Length < 2) continue;
                    string act = vals[1].Value as string;
                    e.UpgradeOpen();
                    e.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, act),
                        new TypedValue((int)DxfCode.ExtendedDataReal, Math.Max(0, Math.Min(100, percent))));
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        public sealed class LinkedObject
        {
            public ObjectId Id;
            public string Activity;
            public double Percent;
            public double Weight;
            public bool Closed;
            public Extents3d? Ext;
        }

        public static List<LinkedObject> ReadLinks()
        {
            var list = new List<LinkedObject>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    var rb = e.GetXDataForApplication(AppName);
                    if (rb == null) continue;
                    var v = rb.AsArray();
                    if (v.Length < 2 || !(v[1].Value is string act) || act.Length == 0) continue;
                    double pct = v.Length >= 3 && v[2].Value is double d ? d : 0;
                    var lo = new LinkedObject { Id = id, Activity = act, Percent = pct, Weight = 1 };
                    if (e is Curve c)
                    {
                        try
                        {
                            lo.Closed = c.Closed;
                            lo.Weight = c.Closed ? Math.Abs(c.Area) : c.GetDistanceAtParameter(c.EndParam);
                        }
                        catch { }
                    }
                    try { lo.Ext = e.GeometricExtents; } catch { }
                    list.Add(lo);
                }
                tr.Commit();
            }
            return list;
        }

        /// <summary>Fills LinkedObjects / DrawingPercent on every activity from the drawing links and quantities.</summary>
        public static void Measure(P6Schedule s)
        {
            var links = ReadLinks().GroupBy(l => l.Activity, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var a in s.Activities)
            {
                links.TryGetValue(a.Id, out var objs);
                a.LinkedObjects = objs?.Count ?? 0;
                double pct = objs == null ? 0 : ProgressCalc.WeightedPercent(objs.Select(o => (o.Weight, o.Percent)));
                a.UpdateDrawingPercent(pct, objs != null && objs.Count > 0);
            }
        }

        public static (byte r, byte g, byte b) StateColor(ActivityState st) => P6Colors.StateColor(st);

        /// <summary>Draws the status overlay: solid colour for closed areas, thick coloured copy for lines, plus "ID xx%" labels.</summary>
        public static int DrawOverlay(P6Schedule s, double textH)
        {
            CadDraw.ClearLayer(StatusLayer);
            var links = ReadLinks();
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr, Db, StatusLayer, 7);
                var style = PointDrawer.EnsureTextStyle(tr, Db);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);
                foreach (var lo in links)
                {
                    var act = s.Find(lo.Activity);
                    if (act == null) continue;
                    var rgb = StateColor(act.State(s.DataDate));
                    var color = Color.FromRgb(rgb.r, rgb.g, rgb.b);
                    var src = (Entity)tr.GetObject(lo.Id, OpenMode.ForRead);
                    bool drawn = false;
                    if (lo.Closed)
                    {
                        try
                        {
                            var h = new Hatch();
                            h.Layer = StatusLayer;
                            ms.AppendEntity(h);
                            tr.AddNewlyCreatedDBObject(h, true);
                            h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                            h.Associative = false;
                            h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { lo.Id });
                            h.EvaluateHatch(true);
                            h.Color = color;
                            h.Transparency = new Transparency((byte)110);
                            drawn = true;
                        }
                        catch { }
                    }
                    if (!drawn && src is Curve)
                    {
                        var copy = (Entity)src.Clone();
                        copy.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName));
                        copy.Layer = StatusLayer;
                        copy.Color = color;
                        copy.LineWeight = LineWeight.LineWeight070;
                        ms.AppendEntity(copy);
                        tr.AddNewlyCreatedDBObject(copy, true);
                    }
                    if (lo.Ext.HasValue)
                    {
                        var c = lo.Ext.Value.MinPoint + (lo.Ext.Value.MaxPoint - lo.Ext.Value.MinPoint) * 0.5;
                        var t = CadDraw.Txt(act.Id + "  " + act.EffectivePercent.ToString("0") + "%", new Point3d(c.X, c.Y, 0), textH, style,
                            TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid);
                        t.Layer = StatusLayer;
                        t.ColorIndex = 7;
                        ms.AppendEntity(t);
                        tr.AddNewlyCreatedDBObject(t, true);
                    }
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        public static Extents3d? ExtentsOf(string activityId)
        {
            Extents3d? ext = null;
            foreach (var lo in ReadLinks().Where(l => string.Equals(l.Activity, activityId, StringComparison.OrdinalIgnoreCase) && l.Ext.HasValue))
            {
                if (ext == null) ext = lo.Ext;
                else { var e = ext.Value; e.AddExtents(lo.Ext.Value); ext = e; }
            }
            return ext;
        }
    }
}
