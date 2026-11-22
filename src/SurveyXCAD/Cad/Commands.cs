using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SurveyX.Core;
using SurveyX.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(SurveyX.Cad.Commands))]

namespace SurveyX.Cad
{
    public class Commands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;
        private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

        private static void Run(string name, Action body)
        {
            try { body(); }
            catch (System.Exception ex)
            {
                Ed.WriteMessage("\n[SurveyX] " + name + " error: " + ex.Message);
                UiKit.Error(ex.Message);
            }
        }

        // ------------------------------------------------------------- import
        [CommandMethod("SXIMPORT", CommandFlags.Modal)]
        public void Import() => Run("SXIMPORT", () =>
        {
            using (var f = new ImportForm())
            {
                if (AcApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK || f.ResultPoints == null) return;
                var pts = f.ResultPoints;
                var pm = new ProgressMeter();
                pm.Start("SurveyX: drawing " + pts.Count + " points...");
                pm.SetLimit(Math.Max(1, pts.Count / 200));
                int n;
                try { n = PointDrawer.InsertPoints(Db, pts, _ => pm.MeterProgress()); }
                finally { pm.Stop(); }
                Ed.WriteMessage("\n[SurveyX] " + n + " points drawn.");
                if (f.ZoomAfter) AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute("._ZOOM _E ", true, false, false);
            }
        });

        // ------------------------------------------------------------- export
        [CommandMethod("SXEXPORT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Export() => Run("SXEXPORT", () =>
        {
            var pts = SelectPoints("\nSelect points to export (Enter = all points): ", true, out _);
            if (pts == null) return;
            if (pts.Count == 0) { Ed.WriteMessage("\n[SurveyX] No points found."); return; }
            using (var f = new ExportForm(pts)) AcApp.ShowModalDialog(f);
        });

        // ------------------------------------------------------------- convert
        [CommandMethod("SXCONVERT", CommandFlags.Modal)]
        public void Convert() => Run("SXCONVERT", () =>
        {
            using (var f = new ConvertForm())
            {
                if (AcApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) return;
                if (f.TransformDrawing) TransformDrawingPoints(f.SourceCrs, f.TargetCrs);
            }
        });

        private void TransformDrawingPoints(Crs src, Crs dst)
        {
            var ids = new List<ObjectId>();
            var res = Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect points to transform: " }, PointDrawer.PointFilter());
            if (res.Status != PromptStatus.OK) return;
            var t = new CoordinateTransformer(src, dst);
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var pts = PointDrawer.ReadPoints(tr, res.Value.GetObjectIds(), ids);
                for (int i = 0; i < pts.Count; i++)
                {
                    t.Transform(pts[i].E, pts[i].N, out double e, out double nn);
                    PointDrawer.MoveTo(tr, ids[i], e, nn);
                    n++;
                }
                tr.Commit();
            }
            Ed.WriteMessage("\n[SurveyX] " + n + " points transformed from " + src.Id + " to " + dst.Id + ". (UNDO to revert)");
        }

        // ------------------------------------------------------------- table
        [CommandMethod("SXTABLE", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void CoordTable() => Run("SXTABLE", () =>
        {
            var pts = SelectPoints("\nSelect points for the table: ", false, out _);
            if (pts == null || pts.Count == 0) return;
            var ppr = Ed.GetPoint("\nTable insertion point: ");
            if (ppr.Status != PromptStatus.OK) return;
            var st = Settings.Current;
            bool hasDesc = pts.Any(p => !string.IsNullOrWhiteSpace(p.Desc));
            int rowsPer = Math.Max(5, st.TableRowsPerColumn);
            double th = st.TextHeight;
            string fmt = "F" + st.Decimals;
            string[] heads = hasDesc
                ? new[] { "No.", "Point", "Easting", "Northing", "Elevation", "Description" }
                : new[] { "No.", "Point", "Easting", "Northing", "Elevation" };
            double[] widths = hasDesc ? new[] { 4.0, 6, 11, 11, 8, 12 } : new[] { 4.0, 6, 11, 11, 8 };

            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);
                var layer = PointDrawer.EnsureLayer(tr, Db, PointDrawer.Layer("TABLE"), 7);
                var style = PointDrawer.EnsureTextStyle(tr, Db);
                Point3d pos = ppr.Value.TransformBy(Ed.CurrentUserCoordinateSystem);
                double totalW = widths.Sum() * th;
                for (int start = 0, part = 1; start < pts.Count; start += rowsPer, part++)
                {
                    var chunk = pts.Skip(start).Take(rowsPer).ToList();
                    var tb = new Table();
                    tb.TableStyle = Db.Tablestyle;
                    tb.SetSize(chunk.Count + 2, heads.Length);
                    tb.Position = pos;
                    tb.LayerId = layer;
                    for (int c = 0; c < heads.Length; c++) tb.Columns[c].Width = widths[c] * th;
                    for (int r = 0; r < chunk.Count + 2; r++) tb.Rows[r].Height = th * 2.0;

                    SetCell(tb, 0, 0, "COORDINATES TABLE" + (pts.Count > rowsPer ? " (" + part + ")" : ""), th * 1.2, style);
                    for (int c = 0; c < heads.Length; c++) SetCell(tb, 1, c, heads[c], th, style);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var p = chunk[i];
                        int r = i + 2;
                        SetCell(tb, r, 0, (start + i + 1).ToString(Inv), th, style);
                        SetCell(tb, r, 1, p.Name, th, style);
                        SetCell(tb, r, 2, p.E.ToString(fmt, Inv), th, style);
                        SetCell(tb, r, 3, p.N.ToString(fmt, Inv), th, style);
                        SetCell(tb, r, 4, p.HasZ ? p.Z.ToString(fmt, Inv) : "-", th, style);
                        if (hasDesc) SetCell(tb, r, 5, p.Desc, th, style);
                    }
                    tb.GenerateLayout();
                    ms.AppendEntity(tb);
                    tr.AddNewlyCreatedDBObject(tb, true);
                    pos = new Point3d(pos.X + totalW + th * 3, pos.Y, pos.Z);
                }
                tr.Commit();
            }
            Ed.WriteMessage("\n[SurveyX] Table created for " + pts.Count + " points.");
        });

        private static void SetCell(Table tb, int r, int c, string text, double h, ObjectId style)
        {
            var cell = tb.Cells[r, c];
            cell.TextString = text ?? "";
            cell.TextHeight = h;
            cell.TextStyleId = style;
            cell.Alignment = CellAlignment.MiddleCenter;
        }

        // ------------------------------------------------------------- leader label
        [CommandMethod("SXLABEL", CommandFlags.Modal)]
        public void Label() => Run("SXLABEL", () =>
        {
            var st = Settings.Current;
            bool withZ = true;
            string fmt = "F" + st.Decimals;
            using (var tr0 = Db.TransactionManager.StartTransaction())
            {
                PointDrawer.EnsureLayer(tr0, Db, PointDrawer.Layer("LABELS"), 2);
                PointDrawer.EnsureTextStyle(tr0, Db);
                tr0.Commit();
            }
            while (true)
            {
                var opt = new PromptPointOptions("\nPick point to label [Z on/off]: ");
                opt.Keywords.Add("Z");
                opt.AppendKeywordsToMessage = false;
                var r1 = Ed.GetPoint(opt);
                if (r1.Status == PromptStatus.Keyword) { withZ = !withZ; Ed.WriteMessage("\nElevation in label: " + (withZ ? "ON" : "OFF")); continue; }
                if (r1.Status != PromptStatus.OK) break;
                var p1 = r1.Value.TransformBy(Ed.CurrentUserCoordinateSystem);
                var o2 = new PromptPointOptions("\nText location: ") { UseBasePoint = true, BasePoint = r1.Value };
                var r2 = Ed.GetPoint(o2);
                if (r2.Status != PromptStatus.OK) break;
                var p2 = r2.Value.TransformBy(Ed.CurrentUserCoordinateSystem);

                string txt = "E = " + p1.X.ToString(fmt, Inv) + "\\PN = " + p1.Y.ToString(fmt, Inv) + (withZ ? "\\PZ = " + p1.Z.ToString(fmt, Inv) : "");
                using (var tr = Db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);
                    var mt = new MText();
                    mt.SetDatabaseDefaults();
                    mt.Contents = txt;
                    mt.TextHeight = st.TextHeight;
                    mt.TextStyleId = PointDrawer.EnsureTextStyle(tr, Db);
                    mt.Location = p2;
                    var ml = new MLeader();
                    ml.SetDatabaseDefaults();
                    ml.ContentType = ContentType.MTextContent;
                    ml.MText = mt;
                    ml.TextHeight = st.TextHeight;
                    ml.ArrowSize = st.TextHeight;
                    ml.Layer = PointDrawer.Layer("LABELS");
                    int idx = ml.AddLeaderLine(p2);
                    ml.AddFirstVertex(idx, p1);
                    ms.AppendEntity(ml);
                    tr.AddNewlyCreatedDBObject(ml, true);
                    tr.Commit();
                }
            }
        });

        // ------------------------------------------------------------- add points by picking
        [CommandMethod("SXPOINT", CommandFlags.Modal)]
        public void AddPoint() => Run("SXPOINT", () =>
        {
            var so = new PromptStringOptions("\nStarting point name <1>: ") { AllowSpaces = false };
            var sr = Ed.GetString(so);
            if (sr.Status == PromptStatus.Cancel) return;
            string name = string.IsNullOrWhiteSpace(sr.StringResult) ? "1" : sr.StringResult.Trim();
            var dr = Ed.GetString(new PromptStringOptions("\nDescription <none>: ") { AllowSpaces = true });
            string desc = dr.Status == PromptStatus.OK ? dr.StringResult : "";
            while (true)
            {
                var r = Ed.GetPoint("\nPick point " + name + ": ");
                if (r.Status != PromptStatus.OK) break;
                var w = r.Value.TransformBy(Ed.CurrentUserCoordinateSystem);
                PointDrawer.InsertPoints(Db, new List<SurveyPoint> { new SurveyPoint { Name = name, E = w.X, N = w.Y, Z = w.Z, Desc = desc } });
                name = NextName(name);
            }
        });

        internal static string NextName(string name)
        {
            int i = name.Length;
            while (i > 0 && char.IsDigit(name[i - 1])) i--;
            if (i == name.Length) return name + "1";
            string prefix = name.Substring(0, i), digits = name.Substring(i);
            long n = long.Parse(digits, Inv) + 1;
            return prefix + n.ToString(Inv).PadLeft(digits.Length, '0');
        }

        // ------------------------------------------------------------- settings / about / update
        [CommandMethod("SXSETTINGS", CommandFlags.Modal)]
        public void SettingsCmd() => Run("SXSETTINGS", () =>
        {
            using (var f = new SettingsForm())
                if (AcApp.ShowModalDialog(f) == System.Windows.Forms.DialogResult.OK) RibbonBuilder.Rebuild();
        });

        [CommandMethod("SXABOUT", CommandFlags.Modal)]
        public void About() => Run("SXABOUT", () => { using (var f = new AboutForm()) AcApp.ShowModalDialog(f); });

        [CommandMethod("SXHELP", CommandFlags.Modal)]
        public void Help() => About();

        [CommandMethod("SXUPDATE", CommandFlags.Modal)]
        public void Update() => Run("SXUPDATE", () => UiKit.OpenUrl(AppInfo.ReleasesUrl));

        [CommandMethod("SXRIBBON", CommandFlags.Modal)]
        public void Ribbon() => Run("SXRIBBON", RibbonBuilder.Rebuild);

        // ------------------------------------------------------------- helpers
        /// <summary>Selects points (implied selection honoured). allowAll: Enter selects every point in model space.</summary>
        private static List<SurveyPoint> SelectPoints(string msg, bool allowAll, out List<ObjectId> ids)
        {
            ids = new List<ObjectId>();
            ObjectId[] sel = null;
            var implied = Ed.SelectImplied();
            if (implied.Status == PromptStatus.OK) sel = implied.Value.GetObjectIds();
            else
            {
                var pso = new PromptSelectionOptions { MessageForAdding = msg };
                var res = Ed.GetSelection(pso, PointDrawer.PointFilter());
                if (res.Status == PromptStatus.OK) sel = res.Value.GetObjectIds();
                else if (allowAll && (res.Status == PromptStatus.Error || res.Status == PromptStatus.None))
                {
                    var all = Ed.SelectAll(PointDrawer.PointFilter());
                    if (all.Status == PromptStatus.OK) sel = all.Value.GetObjectIds();
                    else return new List<SurveyPoint>();
                }
                else return null;
            }
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var pts = PointDrawer.ReadPoints(tr, sel, ids);
                tr.Commit();
                return pts;
            }
        }
    }
}
