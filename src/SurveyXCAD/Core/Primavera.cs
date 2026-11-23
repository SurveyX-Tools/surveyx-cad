using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace SurveyX.Core
{
    public enum ActivityState { NotStarted, InProgress, Completed, Late }

    public sealed class P6Activity
    {
        public string Id = "";          // Activity ID (task_code)
        public string Name = "";
        public string Wbs = "";
        public DateTime? Start;         // planned / early start
        public DateTime? Finish;        // planned / early finish
        public double P6Percent;        // physical % complete from the schedule
        // Quantities (entered by the user or added from cut / fill results):
        public double PlannedQty;
        public double ExecutedQty;
        public string Unit = "m3";
        // Filled from the drawing:
        public int LinkedObjects;
        public double DrawingPercent = -1; // -1 = no progress measured in the drawing

        /// <summary>Progress measured in the drawing: executed / planned quantity when a planned quantity exists,
        /// otherwise the weighted progress of linked objects.</summary>
        public void UpdateDrawingPercent(double objectPercent, bool hasObjects)
        {
            if (PlannedQty > 0) DrawingPercent = Math.Min(100, Math.Max(0, ExecutedQty / PlannedQty * 100));
            else if (hasObjects) DrawingPercent = objectPercent;
            else DrawingPercent = -1;
        }

        public double EffectivePercent => DrawingPercent >= 0 ? DrawingPercent : P6Percent;

        public ActivityState State(DateTime? dataDate)
        {
            double pct = EffectivePercent;
            DateTime dd = dataDate ?? DateTime.Today;
            if (pct >= 99.999) return ActivityState.Completed;
            if (pct > 0) return Finish.HasValue && Finish.Value.Date < dd.Date ? ActivityState.Late : ActivityState.InProgress;
            return Start.HasValue && Start.Value.Date < dd.Date ? ActivityState.Late : ActivityState.NotStarted;
        }
    }

    public sealed class P6Schedule
    {
        public string ProjectName = "";
        public string SourceFile = "";
        public DateTime? DataDate;
        public List<P6Activity> Activities = new List<P6Activity>();

        /// <summary>Keeps quantities already entered when a newer schedule is imported.</summary>
        public void CarryQuantitiesFrom(P6Schedule old)
        {
            if (old == null) return;
            foreach (var a in Activities)
            {
                var o = old.Find(a.Id);
                if (o == null) continue;
                a.PlannedQty = o.PlannedQty; a.ExecutedQty = o.ExecutedQty; a.Unit = o.Unit;
            }
        }

        public P6Activity Find(string id) => Activities.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        // ---------------- persistence as plain text (stored inside the DWG by the CAD layer)
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string D(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm", Inv) : "";
        private static DateTime? PD(string s) => DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
        private static string E(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("SXP6\t1\t").Append(E(ProjectName)).Append('\t').Append(D(DataDate)).Append('\t').Append(E(SourceFile)).Append('\n');
            foreach (var a in Activities)
                sb.Append(E(a.Id)).Append('\t').Append(E(a.Name)).Append('\t').Append(E(a.Wbs)).Append('\t')
                  .Append(D(a.Start)).Append('\t').Append(D(a.Finish)).Append('\t').Append(a.P6Percent.ToString("R", Inv)).Append('\t')
                  .Append(a.PlannedQty.ToString("R", Inv)).Append('\t').Append(a.ExecutedQty.ToString("R", Inv)).Append('\t').Append(E(a.Unit)).Append('\n');
            return sb.ToString();
        }

        public static P6Schedule Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var lines = text.Split('\n');
            var h = lines[0].Split('\t');
            if (h.Length < 2 || h[0] != "SXP6") return null;
            var s = new P6Schedule
            {
                ProjectName = h.Length > 2 ? h[2] : "",
                DataDate = h.Length > 3 ? PD(h[3]) : null,
                SourceFile = h.Length > 4 ? h[4] : "",
            };
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Split('\t');
                if (c.Length < 6) continue;
                double.TryParse(c[5], NumberStyles.Float, Inv, out double pct);
                var a = new P6Activity { Id = c[0], Name = c[1], Wbs = c[2], Start = PD(c[3]), Finish = PD(c[4]), P6Percent = pct };
                if (c.Length >= 9)
                {
                    double.TryParse(c[6], NumberStyles.Float, Inv, out a.PlannedQty);
                    double.TryParse(c[7], NumberStyles.Float, Inv, out a.ExecutedQty);
                    a.Unit = c[8];
                }
                s.Activities.Add(a);
            }
            return s;
        }
    }

    /// <summary>Reads Oracle Primavera P6 exports: XER (native) and Primavera XML (P6 EPPM / Professional).</summary>
    public static class PrimaveraReader
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss.fff", "dd-MMM-yy", "dd-MMM-yyyy", "dd-MMM-yy HH:mm",
        };

        public const string OpenFilter = "Primavera P6 (*.xer;*.xml)|*.xer;*.xml|XER|*.xer|Primavera XML|*.xml|All files|*.*";

        public static P6Schedule Read(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            var s = ext == ".xml" ? ReadXml(path) : ReadXer(path);
            s.SourceFile = Path.GetFileName(path);
            if (s.Activities.Count == 0) throw new InvalidDataException(Lang.T("P6NoActivities"));
            return s;
        }

        public static DateTime? ParseDate(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            if (DateTime.TryParseExact(v, DateFormats, Inv, DateTimeStyles.None, out var d)) return d;
            if (DateTime.TryParse(v, Inv, DateTimeStyles.None, out d)) return d;
            return null;
        }

        // ------------------------------------------------------------ XER
        public static P6Schedule ReadXer(string path)
        {
            string text;
            byte[] bytes = File.ReadAllBytes(path);
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { text = Latin(bytes); }
            return ParseXer(text);
        }

        private static string Latin(byte[] b)
        {
#if NET
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            try { return Encoding.GetEncoding(1252).GetString(b); } catch { return Encoding.Default.GetString(b); }
        }

        public static P6Schedule ParseXer(string text)
        {
            var tables = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            string current = null;
            string[] fields = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("%T\t")) { current = line.Substring(3).Trim(); fields = null; if (!tables.ContainsKey(current)) tables[current] = new List<Dictionary<string, string>>(); }
                else if (line.StartsWith("%F\t")) fields = line.Substring(3).Split('\t');
                else if (line.StartsWith("%R\t") && current != null && fields != null)
                {
                    var vals = line.Substring(3).Split('\t');
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fields.Length; i++) row[fields[i]] = i < vals.Length ? vals[i] : "";
                    tables[current].Add(row);
                }
            }

            var s = new P6Schedule();
            if (tables.TryGetValue("PROJECT", out var proj) && proj.Count > 0)
            {
                s.ProjectName = Get(proj[0], "proj_short_name");
                s.DataDate = ParseDate(Get(proj[0], "last_recalc_date")) ?? ParseDate(Get(proj[0], "next_data_date"));
            }
            var wbs = new Dictionary<string, string>();
            if (tables.TryGetValue("PROJWBS", out var w))
                foreach (var r in w) wbs[Get(r, "wbs_id")] = Get(r, "wbs_name");

            if (tables.TryGetValue("TASK", out var tasks))
            {
                foreach (var r in tasks)
                {
                    var a = new P6Activity
                    {
                        Id = Get(r, "task_code"),
                        Name = Get(r, "task_name"),
                        Wbs = wbs.TryGetValue(Get(r, "wbs_id"), out var wn) ? wn : "",
                        Start = ParseDate(Get(r, "act_start_date")) ?? ParseDate(Get(r, "target_start_date")) ?? ParseDate(Get(r, "early_start_date")),
                        Finish = ParseDate(Get(r, "act_end_date")) ?? ParseDate(Get(r, "target_end_date")) ?? ParseDate(Get(r, "early_end_date")),
                    };
                    double.TryParse(Get(r, "phys_complete_pct"), NumberStyles.Float, Inv, out a.P6Percent);
                    string st = Get(r, "status_code");
                    if (st == "TK_Complete" && a.P6Percent <= 0) a.P6Percent = 100;
                    if (string.IsNullOrEmpty(a.Id)) continue;
                    s.Activities.Add(a);
                }
            }
            return s;
        }

        private static string Get(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v ?? "" : "";

        // ------------------------------------------------------------ Primavera XML
        public static P6Schedule ReadXml(string path)
        {
            var doc = XDocument.Load(path);
            var s = new P6Schedule();
            string V(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";

            var project = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Project");
            if (project != null)
            {
                s.ProjectName = V(project, "Id");
                if (string.IsNullOrEmpty(s.ProjectName)) s.ProjectName = V(project, "Name");
                s.DataDate = ParseDate(V(project, "DataDate")) ?? ParseDate(V(project, "CurrentDataDate"));
            }
            var wbs = new Dictionary<string, string>();
            foreach (var w in doc.Descendants().Where(x => x.Name.LocalName == "WBS"))
                wbs[V(w, "ObjectId")] = V(w, "Name");

            foreach (var e in doc.Descendants().Where(x => x.Name.LocalName == "Activity"))
            {
                string id = V(e, "Id");
                if (string.IsNullOrEmpty(id)) continue;
                var a = new P6Activity
                {
                    Id = id,
                    Name = V(e, "Name"),
                    Wbs = wbs.TryGetValue(V(e, "WBSObjectId"), out var wn) ? wn : "",
                    Start = ParseDate(V(e, "ActualStartDate")) ?? ParseDate(V(e, "PlannedStartDate")) ?? ParseDate(V(e, "StartDate")),
                    Finish = ParseDate(V(e, "ActualFinishDate")) ?? ParseDate(V(e, "PlannedFinishDate")) ?? ParseDate(V(e, "FinishDate")),
                };
                double.TryParse(V(e, "PhysicalPercentComplete"), NumberStyles.Float, Inv, out a.P6Percent);
                if (a.P6Percent > 0 && a.P6Percent <= 1.0 && V(e, "PhysicalPercentComplete").Contains(".")) a.P6Percent *= 100; // some exports use 0-1
                if (V(e, "Status") == "Completed" && a.P6Percent <= 0) a.P6Percent = 100;
                s.Activities.Add(a);
            }
            return s;
        }
    }

    public static class P6Colors
    {
        public static (byte r, byte g, byte b) StateColor(ActivityState st)
        {
            switch (st)
            {
                case ActivityState.Completed: return (67, 160, 71);
                case ActivityState.InProgress: return (251, 140, 0);
                case ActivityState.Late: return (229, 57, 53);
                default: return (158, 158, 158);
            }
        }
    }

    /// <summary>Weighted progress of one activity from its linked drawing objects.</summary>
    public static class ProgressCalc
    {
        /// <param name="items">(weight, percentDone) for each linked object; weight = area, length or 1.</param>
        public static double WeightedPercent(IEnumerable<(double weight, double pct)> items)
        {
            double tw = 0, done = 0;
            foreach (var (w, p) in items)
            {
                double ww = w > 0 ? w : 1;
                tw += ww;
                done += ww * Math.Max(0, Math.Min(100, p));
            }
            return tw > 0 ? done / tw : 0;
        }
    }

    /// <summary>Writes progress files that P6 can take back.</summary>
    public static class PrimaveraWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Excel file in P6 "Import from Spreadsheet" layout: sheet TASK, row 1 = P6 field names,
        /// row 2 = column titles, then one row per activity (task_code + phys_complete_pct).
        /// </summary>
        public static void WriteP6Excel(string path, IList<P6Activity> acts)
        {
            var rows = new List<string[]>
            {
                new[] { "task_code", "phys_complete_pct" },
                new[] { "Activity ID", "Physical % Complete" },
            };
            foreach (var a in acts)
                rows.Add(new[] { a.Id, Math.Round(a.EffectivePercent, 2).ToString(Inv) });
            XlsxWriter.Write(path, "TASK", rows, numericFromRow: 2, numericColumns: new[] { 1 });
        }

        public static void WriteReportCsv(string path, P6Schedule s, IList<P6Activity> acts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Activity ID,Activity Name,WBS,Start,Finish,P6 %,Drawing %,Used %,Linked objects,Status");
            foreach (var a in acts)
            {
                sb.Append(Q(a.Id)).Append(',').Append(Q(a.Name)).Append(',').Append(Q(a.Wbs)).Append(',')
                  .Append(a.Start?.ToString("yyyy-MM-dd", Inv)).Append(',').Append(a.Finish?.ToString("yyyy-MM-dd", Inv)).Append(',')
                  .Append(a.P6Percent.ToString("F1", Inv)).Append(',')
                  .Append(a.DrawingPercent >= 0 ? a.DrawingPercent.ToString("F1", Inv) : "").Append(',')
                  .Append(a.EffectivePercent.ToString("F1", Inv)).Append(',')
                  .Append(a.LinkedObjects).Append(',')
                  .Append(a.State(s.DataDate)).AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Q(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }

    /// <summary>Minimal dependency-free single-sheet .xlsx writer (inline strings + numbers).</summary>
    public static class XlsxWriter
    {
        public static void Write(string path, string sheetName, IList<string[]> rows, int numericFromRow = int.MaxValue, int[] numericColumns = null)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                void Entry(string name, string content)
                {
                    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
                }
                Entry("[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "</Types>");
                Entry("_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Entry("xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"" + SecurityElement.Escape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Entry("xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "</Relationships>");

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                for (int r = 0; r < rows.Count; r++)
                {
                    sb.Append("<row r=\"").Append(r + 1).Append("\">");
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        string refc = Col(c) + (r + 1);
                        string v = rows[r][c] ?? "";
                        bool num = r >= numericFromRow && numericColumns != null && Array.IndexOf(numericColumns, c) >= 0 &&
                                   double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                        if (num) sb.Append("<c r=\"").Append(refc).Append("\"><v>").Append(v).Append("</v></c>");
                        else sb.Append("<c r=\"").Append(refc).Append("\" t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(v)).Append("</t></is></c>");
                    }
                    sb.Append("</row>");
                }
                sb.Append("</sheetData></worksheet>");
                Entry("xl/worksheets/sheet1.xml", sb.ToString());
            }
        }

        private static string Col(int i)
        {
            string s = "";
            i++;
            while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; }
            return s;
        }
    }
}
