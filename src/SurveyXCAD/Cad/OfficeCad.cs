using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Technical-office helpers: quantity take-off and drawing tables.</summary>
    public static class OfficeCad
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static Database Db => AcApp.DocumentManager.MdiActiveDocument.Database;

        /// <summary>Model-space objects, optionally only on the given layers (case-insensitive, * wildcard allowed).</summary>
        public static List<ObjectId> ModelSpaceIds(IEnumerable<string> layers = null)
        {
            var pats = layers?.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            var ids = new List<ObjectId>();
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (pats == null || pats.Count == 0) { ids.Add(id); continue; }
                    var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (pats.Any(p => LayerMatch(e.Layer, p))) ids.Add(id);
                }
                tr.Commit();
            }
            return ids;
        }

        public static bool LayerMatch(string layer, string pattern)
        {
            if (!pattern.Contains("*")) return string.Equals(layer, pattern, StringComparison.OrdinalIgnoreCase);
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(layer, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static List<QtoRow> TakeOff(IEnumerable<ObjectId> ids)
        {
            var rows = new Dictionary<string, QtoRow>(StringComparer.OrdinalIgnoreCase);
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (!rows.TryGetValue(e.Layer, out var r)) rows[e.Layer] = r = new QtoRow { Layer = e.Layer };
                    switch (e)
                    {
                        case Line ln: r.Lines++; r.Length += ln.Length; break;
                        case BlockReference br:
                            r.Blocks++;
                            string bn = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
                            r.BlockNames[bn] = r.BlockNames.TryGetValue(bn, out int k) ? k + 1 : 1;
                            break;
                        case DBText _: case MText _: r.Texts++; break;
                        case DBPoint _: r.Points++; break;
                        case Hatch h:
                            r.ClosedAreas++;
                            try { r.Area += h.Area; } catch { }
                            break;
                        case Curve c:
                            double len = 0;
                            try { len = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam); } catch { }
                            if (c.Closed)
                            {
                                r.ClosedAreas++;
                                try { r.Area += Math.Abs(c.Area); } catch { }
                            }
                            else r.Polylines++;
                            r.Length += len;
                            break;
                        default: r.Other++; break;
                    }
                }
                tr.Commit();
            }
            return rows.Values.OrderBy(r => r.Layer, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Draws a table (split into several side-by-side tables every rowsPer rows). Returns number of tables.</summary>
        public static int DrawTable(Point3d pos, string title, string[] headers, IList<string[]> rows, double[] widthFactors, double th, string layer, int rowsPer = 40)
        {
            int count = 0;
            using (var tr = Db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForWrite);
                var layerId = PointDrawer.EnsureLayer(tr, Db, layer, 7);
                var style = PointDrawer.EnsureTextStyle(tr, Db);
                double totalW = widthFactors.Sum() * th;
                int parts = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)Math.Max(1, rowsPer)));
                for (int part = 0; part < parts; part++)
                {
                    var chunk = rows.Skip(part * rowsPer).Take(rowsPer).ToList();
                    var tb = new Table { TableStyle = Db.Tablestyle, Position = pos, LayerId = layerId };
                    tb.SetSize(chunk.Count + 2, headers.Length);
                    for (int c = 0; c < headers.Length; c++) tb.Columns[c].Width = widthFactors[c] * th;
                    for (int r = 0; r < chunk.Count + 2; r++) tb.Rows[r].Height = th * 2.0;
                    Cell(tb, 0, 0, title + (parts > 1 ? " (" + (part + 1) + "/" + parts + ")" : ""), th * 1.2, style);
                    for (int c = 0; c < headers.Length; c++) Cell(tb, 1, c, headers[c], th, style);
                    for (int i = 0; i < chunk.Count; i++)
                        for (int c = 0; c < headers.Length && c < chunk[i].Length; c++) Cell(tb, i + 2, c, chunk[i][c], th, style);
                    tb.GenerateLayout();
                    ms.AppendEntity(tb);
                    tr.AddNewlyCreatedDBObject(tb, true);
                    pos = new Point3d(pos.X + totalW + th * 3, pos.Y, pos.Z);
                    count++;
                }
                tr.Commit();
            }
            return count;
        }

        private static void Cell(Table tb, int r, int c, string text, double h, ObjectId style)
        {
            var cell = tb.Cells[r, c];
            cell.TextString = text ?? "";
            cell.TextHeight = h;
            cell.TextStyleId = style;
            cell.Alignment = CellAlignment.MiddleCenter;
        }

        public static string[] QtoHeaders => new[] { "Layer", "Length (m)", "Area (m2)", "Lines/Polylines", "Closed areas", "Blocks", "Texts", "Points" };

        public static List<string[]> QtoRows(IList<QtoRow> rows) => rows.Select(r => new[]
        {
            r.Layer, r.Length.ToString("F2", Inv), r.Area.ToString("F2", Inv), (r.Lines + r.Polylines).ToString(Inv), r.ClosedAreas.ToString(Inv),
            r.Blocks.ToString(Inv), r.Texts.ToString(Inv), r.Points.ToString(Inv),
        }).ToList();
    }
}
