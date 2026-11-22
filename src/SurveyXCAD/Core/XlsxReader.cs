using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace SurveyX.Core
{
    /// <summary>Minimal dependency-free .xlsx reader (first worksheet, values only).</summary>
    public static class XlsxReader
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static List<string[]> ReadFirstSheet(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                var ss = zip.GetEntry("xl/sharedStrings.xml");
                if (ss != null)
                {
                    using (var st = ss.Open())
                    {
                        var doc = XDocument.Load(st);
                        foreach (var si in doc.Root.Elements(S + "si"))
                            shared.Add(string.Concat(si.Descendants(S + "t").Select(t => t.Value)));
                    }
                }

                string sheetPath = "xl/worksheets/sheet1.xml";
                var wb = zip.GetEntry("xl/workbook.xml");
                var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
                if (wb != null && rels != null)
                {
                    try
                    {
                        XDocument wbDoc, relDoc;
                        using (var st = wb.Open()) wbDoc = XDocument.Load(st);
                        using (var st = rels.Open()) relDoc = XDocument.Load(st);
                        var first = wbDoc.Root.Element(S + "sheets")?.Elements(S + "sheet").FirstOrDefault();
                        var rid = first?.Attribute(R + "id")?.Value;
                        var target = relDoc.Root.Elements(PR + "Relationship")
                            .FirstOrDefault(e => (string)e.Attribute("Id") == rid)?.Attribute("Target")?.Value;
                        if (!string.IsNullOrEmpty(target))
                            sheetPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                    }
                    catch { /* fall back to sheet1 */ }
                }

                var sheet = zip.GetEntry(sheetPath) ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet"));
                if (sheet == null) throw new InvalidDataException("No worksheet found in workbook.");

                var rows = new List<string[]>();
                using (var st = sheet.Open())
                {
                    var doc = XDocument.Load(st);
                    var data = doc.Root.Element(S + "sheetData");
                    if (data == null) return rows;
                    foreach (var row in data.Elements(S + "row"))
                    {
                        var cells = new SortedDictionary<int, string>();
                        int autoCol = 0;
                        foreach (var c in row.Elements(S + "c"))
                        {
                            string r = (string)c.Attribute("r");
                            int col = r != null ? ColumnIndex(r) : autoCol;
                            autoCol = col + 1;
                            string type = (string)c.Attribute("t");
                            string val;
                            if (type == "s")
                            {
                                int idx;
                                val = int.TryParse(c.Element(S + "v")?.Value, out idx) && idx < shared.Count ? shared[idx] : "";
                            }
                            else if (type == "inlineStr")
                                val = string.Concat(c.Descendants(S + "t").Select(t => t.Value));
                            else
                                val = c.Element(S + "v")?.Value ?? "";
                            cells[col] = val;
                        }
                        if (cells.Count == 0) continue;
                        int max = cells.Keys.Max();
                        var arr = new string[max + 1];
                        for (int i = 0; i <= max; i++) arr[i] = cells.ContainsKey(i) ? cells[i] : "";
                        rows.Add(arr);
                    }
                }
                return rows;
            }
        }

        private static int ColumnIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else break;
            }
            return col - 1;
        }
    }
}
