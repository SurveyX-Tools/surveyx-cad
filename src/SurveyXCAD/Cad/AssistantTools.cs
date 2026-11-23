using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using SurveyX.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>The tools the AI assistant can use. Every tool runs on AutoCAD's main thread with the document locked.</summary>
    public static class AssistantTools
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, ObjectId[]> Selections = new Dictionary<string, ObjectId[]>(StringComparer.OrdinalIgnoreCase);
        private static int _selCounter;

        private static Autodesk.AutoCAD.ApplicationServices.Document Doc => AcApp.DocumentManager.MdiActiveDocument;
        private static Editor Ed => Doc.Editor;
        private static Database Db => Doc.Database;

        public static string OutputFolder => AssistantSpecs.OutputFolder;
        public static List<ToolSpec> Specs() => AssistantSpecs.Specs();
        public static string SystemPrompt() => AssistantSpecs.SystemPrompt();

        // ================================================================== dispatcher
        public static Task<string> Run(string name, Dictionary<string, object> a)
        {
            var doc = Doc ?? throw new InvalidOperationException("No drawing is open.");
            using (doc.LockDocument())
            {
                switch (name)
                {
                    case "get_drawing_info": return Done(DrawingInfo());
                    case "list_files": return Done(ListFiles(a));
                    case "read_points": return Done(ReadPoints(a));
                    case "select_objects": return Done(SelectObjects(a));
                    case "select_by_layer": return Done(SelectByLayer(a));
                    case "pick_point": return Done(PickPoint(a));
                    case "import_points": return Done(ImportPoints(a));
                    case "convert_coordinates": return Done(Convert(a));
                    case "cut_fill": return Done(CutFillTool(a));
                    case "cumulative_report": return Done(Cumulative(a));
                    case "level_check": return Done(LevelCheckTool(a));
                    case "build_surface": return Done(Surface(a));
                    case "sections": return Done(SectionsTool(a));
                    case "setting_out_table": return Done(SetOut(a));
                    case "quantity_takeoff": return Done(Qto(a));
                    case "coordinate_table": return Done(CoordTable(a));
                    case "draw": return Done(Draw(a));
                    case "layer": return Done(Layer(a));
                    case "zoom": return Done(Zoom(a));
                    case "run_command": return Done(RunCommand(a));
                    case "set_report_info": return Done(ReportInfo(a));
                    case "p6_import": return Done(P6Import(a));
                    case "p6_list": return Done(P6List());
                    case "p6_update": return Done(P6Update(a));
                    case "p6_link": return Done(P6Link(a));
                    case "p6_set_object_percent": return Done(P6Pct(a));
                    case "p6_colour": return Done(P6Colour());
                    case "p6_export": return Done(P6Export());
                    default: throw new InvalidOperationException("Unknown tool " + name);
                }
            }
        }

        private static Task<string> Done(object o) => Task.FromResult(o is string s ? s : JsonWriter.Write(o));

        // ================================================================== helpers
        private static string ResolveFile(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return null;
            f = f.Trim().Trim('"');
            if (File.Exists(f)) return f;
            foreach (var dir in new[] { OutputFolder, Settings.Current.LastFolder, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, f);
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException("File not found: " + f + ". Use list_files to find it.");
        }

        private static string OutPath(string folderName, string fileName)
        {
            string dir = Path.Combine(OutputFolder, folderName);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, CutFillForm.Safe(fileName));
        }

        private static ObjectId[] Sel(string id)
        {
            if (string.IsNullOrEmpty(id) || !Selections.TryGetValue(id, out var ids)) throw new InvalidOperationException("Unknown selection_id '" + id + "'. Call select_objects first.");
            return ids;
        }

        public static string RegisterSelection(ObjectId[] ids) => Store(ids);

        private static string Store(ObjectId[] ids)
        {
            string id = "sel" + (++_selCounter);
            Selections[id] = ids;
            return id;
        }

        private static (List<SurveyPoint> pts, string label) Points(Dictionary<string, object> src, double densify = 0)
        {
            if (src == null) throw new InvalidOperationException("A points source is required.");
            if (src.Bool("saved_ogl"))
            {
                var p = Settings.Current.LastOglPath;
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) throw new InvalidOperationException("No saved OGL file yet — give the OGL file once.");
                var r = PointFileReader.Read(p);
                return (r.Points.Where(x => x.HasZ).ToList(), Path.GetFileName(p));
            }
            var file = src.Str("file");
            if (!string.IsNullOrEmpty(file))
            {
                var path = ResolveFile(file);
                var r = PointFileReader.Read(path);
                if (r.IsGeographic) throw new InvalidOperationException("File is in Lat/Long — convert it first with convert_coordinates or import_points.");
                Settings.Current.LastFolder = Path.GetDirectoryName(path);
                return (r.Points, Path.GetFileName(path));
            }
            var sel = src.Str("selection_id");
            if (!string.IsNullOrEmpty(sel)) return (CadPoints.FromIds(Sel(sel), densify), "drawing selection " + sel);
            throw new InvalidOperationException("Give file, selection_id or saved_ogl.");
        }

        private static Point3d Where(Dictionary<string, object> pt, string prompt)
        {
            if (pt != null && !double.IsNaN(pt.Num("x")) && !double.IsNaN(pt.Num("y"))) return new Point3d(pt.Num("x"), pt.Num("y"), 0);
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var r = Ed.GetPoint("\n" + prompt + ": ");
            if (r.Status != PromptStatus.OK) throw new OperationCanceledException("The user cancelled picking the point.");
            return r.Value.TransformBy(Ed.CurrentUserCoordinateSystem);
        }

        private static object R2(double v) => Math.Round(v, 3);

        // ================================================================== tools
        private static object DrawingInfo()
        {
            var layers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var types = new Dictionary<string, int>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    layers[e.Layer] = layers.TryGetValue(e.Layer, out int n) ? n + 1 : 1;
                    string t = e.GetType().Name;
                    types[t] = types.TryGetValue(t, out int m) ? m + 1 : 1;
                }
                var lt = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId lid in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    if (!layers.ContainsKey(l.Name)) layers[l.Name] = 0;
                }
                tr.Commit();
            }
            var st = Settings.Current;
            var sched = UiKit.TryLoadSchedule();
            return J.Obj(
                "file", Doc.Name, "units_insunits", (int)Db.Insunits, "civil3d", CivilBridge.Available,
                "extents", J.Arr(R2(Db.Extmin.X), R2(Db.Extmin.Y), R2(Db.Extmax.X), R2(Db.Extmax.Y)),
                "layers", layers.OrderByDescending(k => k.Value).Take(150).Select(k => (object)J.Obj("name", k.Key, "objects", k.Value)).ToList(),
                "object_types", types.Select(k => (object)J.Obj("type", k.Key, "count", k.Value)).ToList(),
                "company", st.CompanyName, "project", st.ProjectName, "saved_ogl", st.LastOglPath, "saved_design", st.LastDesignPath,
                "output_folder", OutputFolder, "primavera_activities", sched?.Activities.Count ?? 0);
        }

        private static object ListFiles(Dictionary<string, object> a)
        {
            var pattern = a.Str("pattern", "*.*");
            var folders = new List<string>();
            if (!string.IsNullOrEmpty(a.Str("folder"))) folders.Add(a.Str("folder"));
            else
            {
                folders.Add(OutputFolder);
                if (!string.IsNullOrEmpty(Settings.Current.LastFolder)) folders.Add(Settings.Current.LastFolder);
                folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            }
            var list = new List<object>();
            foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
                foreach (var file in Directory.GetFiles(f, pattern).Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTime).Take(40))
                    list.Add(J.Obj("path", file.FullName, "size_kb", file.Length / 1024, "modified", file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", Inv)));
            return J.Obj("files", list);
        }

        private static object ReadPoints(Dictionary<string, object> a)
        {
            var (pts, label) = Points(a.Sub("source"));
            var s = PointAnalysis.Analyze(pts);
            return J.Obj("source", label, "count", pts.Count, "with_z", pts.Count(p => p.HasZ),
                "bounds", J.Arr(R2(s.MinE), R2(s.MinN), R2(s.MaxE), R2(s.MaxN)), "z_range", J.Arr(R2(s.MinZ), R2(s.MaxZ)),
                "duplicate_names", s.DuplicateNames, "duplicate_coords", s.DuplicateCoords, "outliers_z", s.OutliersZ, "outliers_xy", s.OutliersXY,
                "first", pts.Take(5).Select(p => (object)J.Obj("name", p.Name, "e", R2(p.E), "n", R2(p.N), "z", R2(p.Z), "desc", p.Desc)).ToList());
        }

        private static object SelectObjects(Dictionary<string, object> a)
        {
            SelectionFilter filter = null;
            switch (a.Str("kind", "any"))
            {
                case "points": filter = CadPoints.AnyPointLike(); break;
                case "curves": filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,LINE,ARC,SPLINE,AECC_ALIGNMENT,AECC_FEATURE_LINE") }); break;
                case "closed": filter = CadPoints.ClosedBoundary(); break;
            }
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var opt = new PromptSelectionOptions { MessageForAdding = "\n" + a.Str("prompt", "Select objects") + ": ", SingleOnly = a.Bool("single") };
            var r = filter == null ? Ed.GetSelection(opt) : Ed.GetSelection(opt, filter);
            if (r.Status != PromptStatus.OK) return J.Obj("cancelled", true, "message", "The user did not select anything.");
            var ids = r.Value.GetObjectIds();
            return J.Obj("selection_id", Store(ids), "count", ids.Length);
        }

        private static object SelectByLayer(Dictionary<string, object> a)
        {
            var layers = (a.List("layers") ?? new List<object>()).Select(o => System.Convert.ToString(o, Inv)).ToList();
            var ids = OfficeCad.ModelSpaceIds(layers).ToArray();
            return J.Obj("selection_id", Store(ids), "count", ids.Length);
        }

        private static object PickPoint(Dictionary<string, object> a)
        {
            var p = Where(null, a.Str("prompt", "Pick a point"));
            return J.Obj("x", R2(p.X), "y", R2(p.Y), "z", R2(p.Z));
        }

        private static object ImportPoints(Dictionary<string, object> a)
        {
            var path = ResolveFile(a.Str("file"));
            var r = PointFileReader.Read(path);
            var pts = r.Points;
            PointAnalysis.Analyze(pts, 0.001, r.IsGeographic);
            int skipped = 0;
            if (a.Bool("skip_duplicates", true))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var keep = new List<SurveyPoint>();
                foreach (var p in pts)
                {
                    string k = p.Name + "|" + Math.Round(p.E, 3) + "|" + Math.Round(p.N, 3);
                    if (seen.Add(k)) keep.Add(p); else skipped++;
                }
                pts = keep;
            }
            var src = r.IsGeographic ? CrsCatalog.Wgs84Geo : CrsCatalog.Find(a.Str("file_crs", Settings.Current.LastSourceCrs)) ?? CrsCatalog.Find("EPSG:32640");
            var to = CrsCatalog.Find(a.Str("convert_to", ""));
            if (src.Kind == CrsKind.Geographic && to == null) to = CrsCatalog.Find(Settings.Current.LastTargetCrs);
            if (to != null && !ReferenceEquals(src, to))
            {
                var t = new CoordinateTransformer(src, to);
                foreach (var p in pts) { t.Transform(p.E, p.N, out double e, out double n); p.E = e; p.N = n; }
            }
            int drawn = PointDrawer.InsertPoints(Db, pts);
            Settings.Current.LastFolder = Path.GetDirectoryName(path);
            Settings.Current.Save();
            return J.Obj("drawn", drawn, "duplicates_skipped", skipped, "unreadable_lines", r.SkippedLines, "columns", r.Map?.Label,
                "converted", to != null && !ReferenceEquals(src, to) ? src.Id + " -> " + to.Id : "no");
        }

        private static object Convert(Dictionary<string, object> a)
        {
            var from = CrsCatalog.Find(a.Str("from")) ?? throw new InvalidOperationException("Unknown 'from' system.");
            var to = CrsCatalog.Find(a.Str("to")) ?? throw new InvalidOperationException("Unknown 'to' system.");
            var t = new CoordinateTransformer(from, to);
            var res = new List<object>();
            foreach (var o in a.List("points") ?? new List<object>())
            {
                var p = o as Dictionary<string, object>;
                t.Transform(p.Num("x"), p.Num("y"), out double x, out double y);
                int dec = to.Kind == CrsKind.Geographic ? 9 : 4;
                res.Add(J.Obj("name", p.Str("name", ""), "x", Math.Round(x, dec), "y", Math.Round(y, dec)));
            }
            return J.Obj("from", from.Id, "to", to.Id, "points", res);
        }

        private static object CutFillTool(Dictionary<string, object> a)
        {
            var (ogl, oglLabel) = Points(a.Sub("ogl"));
            var (srv, srvLabel) = Points(a.Sub("survey"));
            List<(double x, double y)> boundary = null;
            if (!string.IsNullOrEmpty(a.Str("boundary_selection_id"))) boundary = CadPoints.Boundary(Sel(a.Str("boundary_selection_id"))[0]);
            double maxEdge = Settings.Current.MaxEdge;
            var r = CutFill.Compute(new Tin(ogl, maxEdge), new Tin(srv, maxEdge), srv, boundary, a.Num("cell", Settings.Current.GridCell));
            var file = a.Sub("ogl").Str("file");
            if (!string.IsNullOrEmpty(file)) { Settings.Current.LastOglPath = ResolveFile(file); Settings.Current.Save(); }
            string area = a.Str("area_name", "");
            DateTime date = DateTime.TryParseExact(a.Str("date", ""), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : DateTime.Today;
            CutFillLog.Add(new CutFillLog.Entry { Date = date, Area = area, Cut = r.CutVolume, Fill = r.FillVolume, AreaM2 = r.TotalArea, Cell = r.Cell, Ogl = oglLabel, Survey = srvLabel });
            string baseName = "CutFill_" + (area.Length > 0 ? area + "_" : "") + date.ToString("yyyy-MM-dd", Inv);
            string pdf = null, xlsx = null;
            if (a.Bool("pdf", true))
            {
                pdf = OutPath("Reports", baseName + ".pdf");
                ReportBuilder.SavePdf(ReportFactory.CutFill(r, boundary, area, date, oglLabel, srvLabel, srv.Count), pdf);
            }
            if (a.Bool("excel", true)) { xlsx = OutPath("Reports", baseName + ".xlsx"); ExcelExport.CutFill(xlsx, r, area, date, oglLabel, srvLabel); }
            if (a.Bool("draw_map"))
            {
                string layer = PointDrawer.Layer("CUTFILL_MAP");
                CadDraw.ClearLayer(layer);
                CadDraw.GridMap(r.DiffGrid, v => ColorScale.CutFillClass(v), ColorScale.CutFillColors, layer);
                double h = Math.Max(Settings.Current.TextHeight, (r.MaxX - r.MinX) / 120);
                CadDraw.Legend(new Point3d(r.MaxX + h * 4, r.MaxY, 0), "CUT / FILL (m)  " + date.ToString("yyyy-MM-dd", Inv),
                    Enumerable.Range(0, ColorScale.CutFillColors.Length).Select(i => (ColorScale.CutFillColors[i], ColorScale.CutFillLabel(i, true))).ToList(), h, layer);
            }
            if (a.Bool("draw_labels"))
            {
                string layer = PointDrawer.Layer("CUTFILL_DIFF");
                CadDraw.ClearLayer(layer);
                CadDraw.DiffLabels(r.Points.Where(p => !double.IsNaN(p.RefZ)).Select(p => (p.Point.E, p.Point.N, p.Point.Z, LevelCheckForm.Signed(p.Diff),
                    (short)(Math.Abs(p.Diff) <= ColorScale.CutFillBreaks[4] ? 3 : p.Diff < 0 ? 1 : 5))).ToList(), Settings.Current.TextHeight, layer);
            }
            object activity = null;
            var add = a.Sub("add_to_activity");
            if (add != null)
            {
                var s = P6Store.Load() ?? throw new InvalidOperationException("No Primavera schedule in this drawing — use p6_import first.");
                var act = s.Find(add.Str("activity_id")) ?? throw new InvalidOperationException("Activity not found: " + add.Str("activity_id"));
                double vol = add.Str("which", "fill") == "cut" ? r.CutVolume : r.FillVolume;
                act.ExecutedQty += vol;
                P6Store.Measure(s);
                P6Store.Save(s);
                activity = J.Obj("id", act.Id, "added", R2(vol), "executed", R2(act.ExecutedQty), "planned", R2(act.PlannedQty), "percent", act.DrawingPercent >= 0 ? R2(act.DrawingPercent) : null);
            }
            return J.Obj("cut_m3", R2(r.CutVolume), "fill_m3", R2(r.FillVolume), "net_m3", R2(r.NetVolume), "area_m2", R2(r.TotalArea),
                "cut_area_m2", R2(r.CutArea), "fill_area_m2", R2(r.FillArea), "max_cut_m", R2(r.MaxCut), "max_fill_m", R2(r.MaxFill), "cell_m", r.Cell,
                "points_compared", r.Points.Count(p => !double.IsNaN(p.RefZ)), "ogl", oglLabel, "survey", srvLabel, "pdf", pdf, "excel", xlsx, "activity", activity);
        }

        private static DateTime? Date(string s) => DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        private static object Cumulative(Dictionary<string, object> a)
        {
            string area = a.Str("area_name", "");
            var e = CutFillLog.Filter(area, Date(a.Str("from")), Date(a.Str("to")));
            if (e.Count == 0) return J.Obj("message", "No cut & fill calculations logged for this filter yet.");
            string baseName = "Cumulative_" + (area.Length > 0 ? area + "_" : "") + DateTime.Today.ToString("yyyy-MM-dd", Inv);
            string pdf = OutPath("Reports", baseName + ".pdf"), xlsx = OutPath("Reports", baseName + ".xlsx");
            ReportBuilder.SavePdf(ReportFactory.Cumulative(e, area), pdf);
            ExcelExport.Cumulative(xlsx, e, area);
            return J.Obj("days", e.Count, "from", e.First().Date.ToString("yyyy-MM-dd", Inv), "to", e.Last().Date.ToString("yyyy-MM-dd", Inv),
                "total_cut_m3", R2(e.Sum(x => x.Cut)), "total_fill_m3", R2(e.Sum(x => x.Fill)), "pdf", pdf, "excel", xlsx);
        }

        private static object LevelCheckTool(Dictionary<string, object> a)
        {
            var (asb, _) = Points(a.Sub("as_built"));
            var (des, desLabel) = Points(a.Sub("design"), 1.0);
            double tp = a.Num("tol_plus", Settings.Current.TolPlus), tm = a.Num("tol_minus", Settings.Current.TolMinus);
            LevelCheckResult r;
            string mode = a.Str("mode", "surface");
            if (mode == "same_name") r = LevelCheck.AgainstNames(asb, des, tp, tm);
            else if (mode == "nearest") r = LevelCheck.AgainstNearest(asb, des, a.Num("radius", 0.5), tp, tm);
            else r = LevelCheck.AgainstSurface(asb, new Tin(des, Settings.Current.MaxEdge), tp, tm);
            string element = a.Str("element", "");
            string baseName = "LevelCheck_" + (element.Length > 0 ? element + "_" : "") + DateTime.Today.ToString("yyyy-MM-dd", Inv);
            string pdf = OutPath("Reports", baseName + ".pdf"), xlsx = OutPath("Reports", baseName + ".xlsx");
            ReportBuilder.SavePdf(ReportFactory.LevelCheck(r, element, mode), pdf);
            ExcelExport.LevelCheck(xlsx, r, element);
            if (a.Bool("draw"))
            {
                string layer = PointDrawer.Layer("LEVEL_CHECK");
                CadDraw.ClearLayer(layer);
                CadDraw.DiffLabels(r.Rows.Where(x => x.Status != CheckStatus.NoDesign).Select(x => (x.Point.E, x.Point.N, x.Point.Z, LevelCheckForm.Signed(x.Diff),
                    (short)(x.Status == CheckStatus.Pass ? 3 : 1))).ToList(), Settings.Current.TextHeight, layer);
            }
            return J.Obj("checked", r.Pass + r.Fail, "pass", r.Pass, "fail", r.Fail, "no_design", r.NoDesign, "pass_percent", Math.Round(r.PassPercent, 1),
                "max_above", R2(r.MaxAbove), "max_below", R2(r.MaxBelow), "mean", R2(r.Mean), "std_dev", R2(r.StdDev),
                "failed_points", r.Rows.Where(x => x.Status == CheckStatus.Fail).Take(30).Select(x => (object)J.Obj("name", x.Point.Name, "diff", R2(x.Diff))).ToList(),
                "pdf", pdf, "excel", xlsx);
        }

        private static object Surface(Dictionary<string, object> a)
        {
            var (pts, label) = Points(a.Sub("points"), 1.0);
            var tin = new Tin(pts, a.Num("max_edge", Settings.Current.MaxEdge));
            var done = new List<object>();
            if (a.Bool("faces")) { string l = PointDrawer.Layer("TIN"); CadDraw.ClearLayer(l); done.Add("3D faces: " + CadDraw.Faces(tin, l)); }
            if (!string.IsNullOrEmpty(a.Str("civil_surface_name")))
            {
                var pc = new Point3dCollection();
                foreach (var p in pts.Where(p => p.HasZ)) pc.Add(new Point3d(p.E, p.N, p.Z));
                done.Add("Civil 3D surface: " + CivilBridge.CreateTinSurface(a.Str("civil_surface_name"), pc));
            }
            if (a.Bool("cogo_points")) done.Add("COGO points: " + CivilBridge.CreateCogoPoints(pts));
            double ci = a.Num("contour_interval", 0);
            if (ci > 0)
            {
                string mi = PointDrawer.Layer("CONTOUR_MINOR"), ma = PointDrawer.Layer("CONTOUR_MAJOR");
                CadDraw.ClearLayer(mi); CadDraw.ClearLayer(ma);
                done.Add("contours: " + CadDraw.Contours(tin, ci, (int)a.Num("major_every", 5), mi, ma, true, Settings.Current.TextHeight));
            }
            if (a.Bool("elevation_map"))
            {
                double cell = CutFill.AutoCell(tin.MaxX - tin.MinX, tin.MaxY - tin.MinY);
                ElevationGrid.Frame(tin.MinX, tin.MinY, tin.MaxX, tin.MaxY, cell, out double x0, out double y0, out int nx, out int ny);
                var g = tin.ToGrid(x0, y0, nx, ny, cell);
                string l = PointDrawer.Layer("ELEV_MAP");
                CadDraw.ClearLayer(l);
                var colors = Enumerable.Range(0, 10).Select(i => ColorScale.Elevation((i + 0.5) / 10)).ToArray();
                double zmin = tin.MinZ, zmax = tin.MaxZ;
                CadDraw.GridMap(g, z => Math.Max(0, Math.Min(9, (int)((z - zmin) / Math.Max(1e-9, zmax - zmin) * 10))), colors, l);
                double h = Math.Max(Settings.Current.TextHeight, (tin.MaxX - tin.MinX) / 120);
                CadDraw.Legend(new Point3d(tin.MaxX + h * 4, tin.MaxY, 0), "ELEVATION (m)",
                    Enumerable.Range(0, 10).Select(i => (colors[i], (zmin + (zmax - zmin) * i / 10).ToString("0.00", Inv) + " - " + (zmin + (zmax - zmin) * (i + 1) / 10).ToString("0.00", Inv))).ToList(), h, l);
                done.Add("elevation map");
            }
            return J.Obj("source", label, "points", tin.Count, "triangles", tin.TriangleCount, "area_m2", R2(tin.Area()), "min_z", R2(tin.MinZ), "max_z", R2(tin.MaxZ), "done", done);
        }

        private static List<(double x, double y)> Alignment(string selId)
        {
            foreach (var id in Sel(selId))
            {
                var a = CadPoints.Alignment(id);
                if (a.Count >= 2) return a;
            }
            throw new InvalidOperationException("The selection has no centreline (polyline / line / alignment).");
        }

        private static object SectionsTool(Dictionary<string, object> a)
        {
            var align = Alignment(a.Str("alignment_selection_id"));
            var st = Settings.Current;
            var (ex, exLabel) = Points(a.Sub("existing"), 1.0);
            var surfaces = new List<Tin> { new Tin(ex, st.MaxEdge) };
            var names = new List<string> { "Existing" };
            if (a.Sub("design") != null) { var (de, _) = Points(a.Sub("design"), 1.0); surfaces.Add(new Tin(de, st.MaxEdge)); names.Add("Design / new"); }
            double interval = a.Num("interval", st.SectionInterval), left = a.Num("left", st.SectionLeft), right = a.Num("right", st.SectionRight),
                step = a.Num("step", st.SectionStep), start = a.Num("start_chainage", st.StartChainage);
            var res = J.Obj("length_m", R2(Geo2D.Length(align)), "existing", exLabel);
            if (a.Bool("long_section"))
            {
                var at = Where(a.Sub("long_section_at"), "Long section insertion point (lower-left)");
                var data = Sections.LongSection(align, surfaces, Math.Max(1, interval / 4));
                CadDraw.LongSection(at, data, names.ToArray(), st.VerticalExaggeration, start, st.TextHeight, PointDrawer.Layer("LONG_SECTION"));
                res["long_section_stations"] = data.Count;
            }
            List<SectionLine> secs = null;
            if (a.Bool("cross_sections"))
            {
                var at = Where(a.Sub("cross_sections_at"), "Cross sections insertion point (upper-left)");
                secs = Sections.CrossSections(align, surfaces, interval, left, right, step);
                res["cross_sections"] = CadDraw.CrossSections(at, secs, names.ToArray(), Math.Max(1, st.VerticalExaggeration / 5), start, st.TextHeight, 4, PointDrawer.Layer("CROSS_SECTIONS"));
            }
            if (a.Bool("volumes_report"))
            {
                if (surfaces.Count < 2) throw new InvalidOperationException("Volumes need a design / new surface.");
                secs = secs ?? Sections.CrossSections(align, surfaces, interval, left, right, step);
                var v = Sections.EndArea(secs);
                string baseName = "Sections_" + DateTime.Today.ToString("yyyy-MM-dd", Inv);
                string pdf = OutPath("Reports", baseName + ".pdf"), xlsx = OutPath("Reports", baseName + ".xlsx");
                ReportBuilder.SavePdf(ReportFactory.SectionVolumes(v, align, start, interval, left, right, secs.Count), pdf);
                ExcelExport.SectionVolumes(xlsx, v, start);
                res["total_cut_m3"] = R2(v.TotalCut); res["total_fill_m3"] = R2(v.TotalFill); res["pdf"] = pdf; res["excel"] = xlsx;
            }
            return res;
        }

        private static object SetOut(Dictionary<string, object> a)
        {
            var align = Alignment(a.Str("alignment_selection_id"));
            var (pts, label) = Points(a.Sub("points"));
            double start = a.Num("start_chainage", Settings.Current.StartChainage);
            var rows = pts.Select(p => { var so = StationOffset.Compute(align, p.E, p.N); return (p, so.station, so.offset); }).OrderBy(x => x.station).ToList();
            string xlsx = OutPath("Reports", "SettingOut_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".xlsx");
            ExcelExport.SetOut(xlsx, rows, start);
            int tables = 0;
            if (a.Bool("draw_table", true))
            {
                var at = Where(a.Sub("table_at"), "Setting-out table insertion point");
                tables = OfficeCad.DrawTable(at, "SETTING OUT TABLE", new[] { "Point", "Chainage", "Offset", "Easting", "Northing", "Elevation" },
                    rows.Select(x => new[] { x.p.Name, Geo2D.FormatChainage(x.station, start), x.offset.ToString("+0.000;-0.000", Inv), x.p.E.ToString("F3", Inv), x.p.N.ToString("F3", Inv), x.p.HasZ ? x.p.Z.ToString("F3", Inv) : "-" }).ToList(),
                    new[] { 6.0, 9, 7, 11, 11, 8 }, Settings.Current.TextHeight, PointDrawer.Layer("TABLE"), Settings.Current.TableRowsPerColumn);
            }
            return J.Obj("points", rows.Count, "source", label, "tables_drawn", tables, "excel", xlsx,
                "first", rows.Take(10).Select(x => (object)J.Obj("name", x.p.Name, "chainage", Geo2D.FormatChainage(x.station, start), "offset", R2(x.offset))).ToList());
        }

        private static object Qto(Dictionary<string, object> a)
        {
            IEnumerable<ObjectId> ids = !string.IsNullOrEmpty(a.Str("selection_id"))
                ? Sel(a.Str("selection_id"))
                : OfficeCad.ModelSpaceIds((a.List("layers") ?? new List<object>()).Select(o => System.Convert.ToString(o, Inv)));
            var q = OfficeCad.TakeOff(ids);
            string xlsx = OutPath("Reports", "QTO_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".xlsx");
            ExcelExport.Qto(xlsx, q);
            if (a.Bool("draw_table"))
            {
                var at = Where(a.Sub("table_at"), "Quantity table insertion point");
                OfficeCad.DrawTable(at, "QUANTITY TAKE-OFF", OfficeCad.QtoHeaders, OfficeCad.QtoRows(q), new[] { 12.0, 9, 9, 8, 8, 7, 6, 6 }, Settings.Current.TextHeight, PointDrawer.Layer("TABLE"), 60);
            }
            return J.Obj("layers", q.Select(r => (object)J.Obj("layer", r.Layer, "length_m", Math.Round(r.Length, 2), "area_m2", Math.Round(r.Area, 2),
                "lines", r.Lines + r.Polylines, "closed", r.ClosedAreas, "blocks", r.Blocks, "block_names", r.BlockNames.Select(b => (object)(b.Key + " x" + b.Value)).ToList(),
                "texts", r.Texts, "points", r.Points)).ToList(), "excel", xlsx);
        }

        private static object CoordTable(Dictionary<string, object> a)
        {
            var (pts, _) = Points(a.Sub("points"));
            var at = Where(a.Sub("table_at"), "Coordinate table insertion point");
            string f = "F" + Settings.Current.Decimals;
            int n = OfficeCad.DrawTable(at, "COORDINATES TABLE", new[] { "No.", "Point", "Easting", "Northing", "Elevation", "Description" },
                pts.Select((p, i) => new[] { (i + 1).ToString(Inv), p.Name, p.E.ToString(f, Inv), p.N.ToString(f, Inv), p.HasZ ? p.Z.ToString(f, Inv) : "-", p.Desc ?? "" }).ToList(),
                new[] { 4.0, 6, 11, 11, 8, 12 }, Settings.Current.TextHeight, PointDrawer.Layer("TABLE"), Settings.Current.TableRowsPerColumn);
            return J.Obj("points", pts.Count, "tables", n);
        }

        private static Point3d P3(object o)
        {
            var l = o as List<object> ?? throw new InvalidOperationException("Point must be [x, y] or [x, y, z].");
            double x = System.Convert.ToDouble(l[0], Inv), y = System.Convert.ToDouble(l[1], Inv), z = l.Count > 2 ? System.Convert.ToDouble(l[2], Inv) : 0;
            return new Point3d(x, y, z);
        }

        private static object Draw(Dictionary<string, object> a)
        {
            int n = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);
                var style = PointDrawer.EnsureTextStyle(tr, Db);
                foreach (var o in a.List("entities") ?? new List<object>())
                {
                    var e = o as Dictionary<string, object>;
                    if (e == null) continue;
                    Entity ent;
                    var pts = (e.List("points") ?? new List<object>()).Select(P3).ToList();
                    switch (e.Str("type", "line"))
                    {
                        case "line": ent = new Line(pts[0], pts[1]); break;
                        case "polyline":
                            if (pts.Any(p => Math.Abs(p.Z - pts[0].Z) > 1e-9))
                            {
                                var p3 = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts.ToArray()), e.Bool("closed"));
                                ent = p3;
                            }
                            else
                            {
                                var pl = new Polyline();
                                for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                                pl.Elevation = pts.Count > 0 ? pts[0].Z : 0;
                                pl.Closed = e.Bool("closed");
                                ent = pl;
                            }
                            break;
                        case "circle": ent = new Circle(P3(e.List("center")), Vector3d.ZAxis, e.Num("radius", 1)); break;
                        case "point": ent = new DBPoint(pts[0]); break;
                        case "text":
                            var at = e.List("center") != null ? P3(e.List("center")) : pts[0];
                            ent = new DBText { TextString = e.Str("text", ""), Position = at, Height = e.Num("height", Settings.Current.TextHeight),
                                Rotation = e.Num("rotation_deg", 0) * Math.PI / 180, TextStyleId = style };
                            break;
                        default: continue;
                    }
                    string layer = e.Str("layer");
                    if (!string.IsNullOrEmpty(layer)) { PointDrawer.EnsureLayer(tr, Db, layer, 7); ent.Layer = layer; }
                    double col = e.Num("color", double.NaN);
                    if (!double.IsNaN(col)) ent.ColorIndex = (int)col;
                    ms.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    n++;
                }
                tr.Commit();
            }
            Ed.Regen();
            return J.Obj("drawn", n);
        }

        private static object Layer(Dictionary<string, object> a)
        {
            string name = a.Str("name");
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var id = PointDrawer.EnsureLayer(tr, Db, name, (short)Math.Max(1, Math.Min(255, a.Num("color", 7))));
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                if (!double.IsNaN(a.Num("color"))) l.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)a.Num("color"));
                if (a.ContainsKey("on")) l.IsOff = !a.Bool("on");
                if (a.ContainsKey("frozen") && !(a.Bool("frozen") && Db.Clayer == id)) l.IsFrozen = a.Bool("frozen");
                if (a.Bool("current")) Db.Clayer = id;
                tr.Commit();
            }
            Ed.Regen();
            return J.Obj("layer", name, "ok", true);
        }

        private static object Zoom(Dictionary<string, object> a)
        {
            Extents3d? ext = null;
            if (!string.IsNullOrEmpty(a.Str("selection_id")))
            {
                using (var tr = Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in Sel(a.Str("selection_id")))
                        try { var e = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents; if (ext == null) ext = e; else { var x = ext.Value; x.AddExtents(e); ext = x; } } catch { }
                    tr.Commit();
                }
            }
            else if (a.List("window") is List<object> w && w.Count == 4)
                ext = new Extents3d(new Point3d(System.Convert.ToDouble(w[0], Inv), System.Convert.ToDouble(w[1], Inv), 0), new Point3d(System.Convert.ToDouble(w[2], Inv), System.Convert.ToDouble(w[3], Inv), 0));
            else ext = new Extents3d(Db.Extmin, Db.Extmax);
            if (ext == null) return J.Obj("ok", false);
            CadView.ZoomTo(ext.Value);
            return J.Obj("ok", true);
        }

        private static object RunCommand(Dictionary<string, object> a)
        {
            string cmd = a.Str("command", "").Trim();
            if (cmd.Length == 0) throw new InvalidOperationException("Empty command.");
            Doc.SendStringToExecute(cmd + (cmd.EndsWith(" ") || cmd.EndsWith("\n") ? "" : " "), true, false, true);
            return J.Obj("queued", cmd, "note", "The command runs in AutoCAD right after this reply.");
        }

        private static object ReportInfo(Dictionary<string, object> a)
        {
            var st = Settings.Current;
            if (a.ContainsKey("company")) st.CompanyName = a.Str("company", "");
            if (a.ContainsKey("project")) st.ProjectName = a.Str("project", "");
            if (a.ContainsKey("prepared_by")) st.PreparedBy = a.Str("prepared_by", "");
            if (a.ContainsKey("submitted_to")) st.SubmittedTo = a.Str("submitted_to", "");
            st.Save();
            return J.Obj("company", st.CompanyName, "project", st.ProjectName, "prepared_by", st.PreparedBy, "submitted_to", st.SubmittedTo);
        }

        // ------------------------------------------------------------------ Primavera
        private static P6Schedule Sched() => P6Store.Load() ?? throw new InvalidOperationException("No Primavera schedule in this drawing — use p6_import first.");

        private static object P6Import(Dictionary<string, object> a)
        {
            var s = PrimaveraReader.Read(ResolveFile(a.Str("file")));
            s.CarryQuantitiesFrom(P6Store.Load());
            P6Store.Save(s);
            P6Store.Measure(s);
            return J.Obj("project", s.ProjectName, "data_date", s.DataDate?.ToString("yyyy-MM-dd", Inv), "activities", s.Activities.Count);
        }

        private static object P6List()
        {
            var s = Sched();
            P6Store.Measure(s);
            return J.Obj("project", s.ProjectName, "data_date", s.DataDate?.ToString("yyyy-MM-dd", Inv), "activities", s.Activities.Select(x => (object)J.Obj(
                "id", x.Id, "name", x.Name, "wbs", x.Wbs, "start", x.Start?.ToString("yyyy-MM-dd", Inv), "finish", x.Finish?.ToString("yyyy-MM-dd", Inv),
                "p6_percent", x.P6Percent, "planned_qty", x.PlannedQty, "executed_qty", Math.Round(x.ExecutedQty, 2), "unit", x.Unit,
                "measured_percent", x.DrawingPercent >= 0 ? Math.Round(x.DrawingPercent, 1) : (object)null, "linked_objects", x.LinkedObjects,
                "status", x.State(s.DataDate).ToString())).ToList());
        }

        private static object P6Update(Dictionary<string, object> a)
        {
            var s = Sched();
            var x = s.Find(a.Str("activity_id")) ?? throw new InvalidOperationException("Activity not found: " + a.Str("activity_id"));
            if (!double.IsNaN(a.Num("planned_qty"))) x.PlannedQty = Math.Max(0, a.Num("planned_qty"));
            if (!double.IsNaN(a.Num("executed_qty"))) x.ExecutedQty = Math.Max(0, a.Num("executed_qty"));
            if (!double.IsNaN(a.Num("add_executed_qty"))) x.ExecutedQty = Math.Max(0, x.ExecutedQty + a.Num("add_executed_qty"));
            if (!string.IsNullOrEmpty(a.Str("unit"))) x.Unit = a.Str("unit");
            P6Store.Measure(s);
            P6Store.Save(s);
            return J.Obj("id", x.Id, "planned", x.PlannedQty, "executed", Math.Round(x.ExecutedQty, 2), "unit", x.Unit,
                "measured_percent", x.DrawingPercent >= 0 ? Math.Round(x.DrawingPercent, 1) : (object)null, "status", x.State(s.DataDate).ToString());
        }

        private static object P6Link(Dictionary<string, object> a)
        {
            var s = Sched();
            var x = s.Find(a.Str("activity_id")) ?? throw new InvalidOperationException("Activity not found.");
            return J.Obj("linked", P6Store.Link(Sel(a.Str("selection_id")), x.Id, 0), "activity", x.Id);
        }

        private static object P6Pct(Dictionary<string, object> a) =>
            J.Obj("updated", P6Store.SetPercent(Sel(a.Str("selection_id")), a.Num("percent", 0)));

        private static object P6Colour()
        {
            var s = Sched();
            P6Store.Measure(s);
            return J.Obj("coloured_objects", P6Store.DrawOverlay(s, Settings.Current.TextHeight), "layer", P6Store.StatusLayer);
        }

        private static object P6Export()
        {
            var s = Sched();
            P6Store.Measure(s);
            string baseName = "P6_Progress_" + DateTime.Today.ToString("yyyy-MM-dd", Inv);
            string xlsx = OutPath("Reports", baseName + ".xlsx"), pdf = OutPath("Reports", baseName + ".pdf");
            var measured = s.Activities.Where(x => x.DrawingPercent >= 0).ToList();
            if (measured.Count > 0) PrimaveraWriter.WriteP6Excel(xlsx, measured); else xlsx = null;
            ReportBuilder.SavePdf(ReportFactory.Progress(s), pdf);
            return J.Obj("excel_for_p6", xlsx, "pdf", pdf, "measured_activities", measured.Count,
                "how_to_import", "P6: File > Import > Spreadsheet (XLSX) > Update Existing Project");
        }
    }
}
