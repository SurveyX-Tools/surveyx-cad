using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SurveyX.Core
{
    /// <summary>Which column holds which value (-1 = not present).</summary>
    public sealed class ColumnMap
    {
        public int P = -1, N = -1, E = -1, Z = -1, D = -1;
        public string Label = "";

        public static readonly string[] Presets =
        {
            "PNEZD", "PENZD", "PNEZ", "PENZ", "NEZD", "ENZD", "NEZ", "ENZ", "PNE", "PEN", "NE", "EN", "PDNEZ", "PDENZ"
        };

        public static ColumnMap FromPreset(string preset)
        {
            var m = new ColumnMap { Label = preset };
            for (int i = 0; i < preset.Length; i++)
            {
                switch (preset[i])
                {
                    case 'P': m.P = i; break;
                    case 'N': m.N = i; break;
                    case 'E': m.E = i; break;
                    case 'Z': m.Z = i; break;
                    case 'D': m.D = i; break;
                }
            }
            return m;
        }

        public bool IsValid => N >= 0 && E >= 0;
        public int MaxIndex => new[] { P, N, E, Z, D }.Max();
    }

    public sealed class ReadResult
    {
        public List<SurveyPoint> Points = new List<SurveyPoint>();
        public List<string> Warnings = new List<string>();
        public int SkippedLines;
        public bool HadHeader;
        public ColumnMap Map;
        public bool IsGeographic; // KML/GeoJSON give lon/lat
        public List<string[]> RawRows; // for preview of tabular files
    }

    public static class PointFileReader
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string OpenFilter =
            "All supported|*.csv;*.txt;*.pnt;*.xyz;*.asc;*.dat;*.prn;*.xlsx;*.xml;*.landxml;*.kml;*.geojson;*.json|" +
            "Text / CSV|*.csv;*.txt;*.pnt;*.xyz;*.asc;*.dat;*.prn|Excel|*.xlsx|LandXML|*.xml;*.landxml|KML|*.kml|GeoJSON|*.geojson;*.json|All files|*.*";

        public static bool IsTabular(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return !(ext == ".xml" || ext == ".landxml" || ext == ".kml" || ext == ".geojson" || ext == ".json");
        }

        /// <summary>Reads any supported file. For tabular files pass map = null to auto-detect.</summary>
        public static ReadResult Read(string path, ColumnMap map = null)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".xml":
                case ".landxml": return ReadLandXml(path);
                case ".kml": return ReadKml(path);
                case ".geojson":
                case ".json": return ReadGeoJson(path);
            }
            List<string[]> rows = ext == ".xlsx" ? XlsxReader.ReadFirstSheet(path) : ReadDelimited(path);
            return ParseRows(rows, map);
        }

        // ------------------------------------------------------------------ text
        public static string ReadAllTextSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return AnsiArabic().GetString(bytes);
            }
        }

        private static Encoding AnsiArabic()
        {
#if NET
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            try { return Encoding.GetEncoding(1256); } catch { return Encoding.Default; }
        }

        public static List<string[]> ReadDelimited(string path)
        {
            string text = ReadAllTextSmart(path);
            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            char delim = DetectDelimiter(lines);
            var rows = new List<string[]>(lines.Length);
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) { rows.Add(null); continue; }
                rows.Add(delim == ' ' ? line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries) : SplitQuoted(line, delim));
            }
            return rows;
        }

        public static char DetectDelimiter(IEnumerable<string> lines)
        {
            var sample = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(30).ToList();
            char[] cands = { '\t', ',', ';', '|' };
            foreach (var c in cands)
            {
                var counts = sample.Select(l => l.Count(ch => ch == c)).ToList();
                if (counts.Count > 0 && counts.All(n => n > 0) && counts.Distinct().Count() <= 2) return c;
            }
            foreach (var c in cands)
                if (sample.Count > 0 && sample.Count(l => l.IndexOf(c) >= 0) * 2 >= sample.Count) return c;
            return ' ';
        }

        private static string[] SplitQuoted(string line, char delim)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else q = !q;
                }
                else if (ch == delim && !q) { res.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(ch);
            }
            res.Add(sb.ToString().Trim());
            return res.ToArray();
        }

        public static bool TryNum(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Float, Inv, out v)) return true;
            // decimal comma ("123,45") when no other comma meaning
            if (s.Count(c => c == ',') == 1 && s.IndexOf('.') < 0 &&
                double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out v)) return true;
            return false;
        }

        // ------------------------------------------------------------------ mapping
        private static readonly Dictionary<string, char> HeaderNames = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
        {
            {"name",'P'},{"point",'P'},{"pt",'P'},{"pn",'P'},{"id",'P'},{"no",'P'},{"no.",'P'},{"number",'P'},{"pointname",'P'},{"point name",'P'},{"point id",'P'},{"stn",'P'},{"station",'P'},
            {"رقم",'P'},{"رقم النقطة",'P'},{"النقطة",'P'},{"اسم",'P'},{"الاسم",'P'},
            {"n",'N'},{"north",'N'},{"northing",'N'},{"y",'N'},{"lat",'N'},{"latitude",'N'},{"local n",'N'},{"grid n",'N'},
            {"شمالي",'N'},{"الشمالي",'N'},{"شمال",'N'},{"خط العرض",'N'},
            {"e",'E'},{"east",'E'},{"easting",'E'},{"x",'E'},{"lon",'E'},{"long",'E'},{"lng",'E'},{"longitude",'E'},{"local e",'E'},{"grid e",'E'},
            {"شرقي",'E'},{"الشرقي",'E'},{"شرق",'E'},{"خط الطول",'E'},
            {"z",'Z'},{"h",'Z'},{"elev",'Z'},{"elevation",'Z'},{"height",'Z'},{"level",'Z'},{"rl",'Z'},{"alt",'Z'},{"altitude",'Z'},{"ellipsoid height",'Z'},{"local h",'Z'},
            {"منسوب",'Z'},{"المنسوب",'Z'},{"ارتفاع",'Z'},{"الارتفاع",'Z'},
            {"d",'D'},{"desc",'D'},{"description",'D'},{"code",'D'},{"remark",'D'},{"remarks",'D'},{"feature",'D'},{"note",'D'},
            {"وصف",'D'},{"الوصف",'D'},{"كود",'D'},{"الكود",'D'},{"ملاحظات",'D'},
        };

        public static ColumnMap MapFromHeader(string[] header)
        {
            if (header == null) return null;
            var m = new ColumnMap { Label = "Header" };
            for (int i = 0; i < header.Length; i++)
            {
                string h = (header[i] ?? "").Trim().Trim('"').Replace("(m)", "").Replace("[m]", "").Trim();
                if (!HeaderNames.TryGetValue(h, out char role)) continue;
                switch (role)
                {
                    case 'P': if (m.P < 0) m.P = i; break;
                    case 'N': if (m.N < 0) m.N = i; break;
                    case 'E': if (m.E < 0) m.E = i; break;
                    case 'Z': if (m.Z < 0) m.Z = i; break;
                    case 'D': if (m.D < 0) m.D = i; break;
                }
            }
            if (!m.IsValid) return null;
            m.Label = "Header: " + string.Join(",", header.Take(m.MaxIndex + 1));
            return m;
        }

        private static bool LooksLikeHeader(string[] row)
        {
            if (row == null) return false;
            int numeric = row.Count(c => TryNum(c, out _));
            return numeric < 2;
        }

        /// <summary>Guess a preset from the first data rows.</summary>
        public static ColumnMap Guess(List<string[]> rows)
        {
            var data = rows.Where(r => r != null && !LooksLikeHeader(r)).Take(20).ToList();
            if (data.Count == 0) return ColumnMap.FromPreset("PNEZD");
            int cols = (int)Math.Round(data.Average(r => r.Length));
            var first = data[0];
            bool firstIsName = !TryNum(first[0], out double f0) || (cols >= 4 && Math.Abs(f0 - Math.Round(f0)) < 1e-9 && f0 < 100000 && data.Count > 1 && LooksSequential(data));
            int c = firstIsName ? 1 : 0;
            int numericAfter = 0;
            for (int i = c; i < first.Length && TryNum(first[i], out _); i++) numericAfter++;
            bool hasDescAfter = first.Length > c + numericAfter;

            // Decide N/E order from typical magnitudes (N >= 1,000,000 in UTM UAE/Egypt; lon > 90 not possible for lat)
            bool nFirst = true;
            if (numericAfter >= 2 && TryNum(first[c], out double a) && TryNum(first[c + 1], out double b))
            {
                double avgA = data.Where(r => r.Length > c + 1).Average(r => TryNum(r[c], out double x) ? Math.Abs(x) : 0);
                double avgB = data.Where(r => r.Length > c + 1).Average(r => TryNum(r[c + 1], out double x) ? Math.Abs(x) : 0);
                if (avgA <= 180 && avgB <= 180) nFirst = !(avgA > 90 || (avgA > avgB && avgA > 40 && avgB < 40)); // lat,lon
                else if (avgA < 1000000 && avgB >= 1000000) nFirst = false;
                else nFirst = true;
            }
            string ne = nFirst ? "NE" : "EN";
            string preset = (firstIsName ? "P" : "") + ne + (numericAfter >= 3 ? "Z" : "") + (hasDescAfter ? "D" : "");
            return ColumnMap.FromPreset(preset);
        }

        private static bool LooksSequential(List<string[]> data)
        {
            int inc = 0;
            for (int i = 1; i < data.Count; i++)
                if (TryNum(data[i][0], out double x) && TryNum(data[i - 1][0], out double y) && Math.Abs(x - y) <= 10) inc++;
            return inc >= (data.Count - 1) / 2;
        }

        public static ReadResult ParseRows(List<string[]> rows, ColumnMap map)
        {
            var res = new ReadResult { RawRows = rows };
            int firstIdx = rows.FindIndex(r => r != null);
            string[] firstRow = firstIdx >= 0 ? rows[firstIdx] : null;
            bool header = LooksLikeHeader(firstRow);
            res.HadHeader = header;
            if (map == null)
                map = (header ? MapFromHeader(firstRow) : null) ?? Guess(rows);
            res.Map = map;

            int autoNum = 1;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                if (header && i == firstIdx) continue;
                if (r.Length <= Math.Max(map.N, map.E))
                {
                    res.SkippedLines++; continue;
                }
                bool okN = TryCoord(r[map.N], out double n);
                bool okE = TryCoord(r[map.E], out double e);
                if (!okN || !okE) { res.SkippedLines++; continue; }
                var p = new SurveyPoint { N = n, E = e, SourceLine = i + 1 };
                if (map.Z >= 0 && map.Z < r.Length && TryNum(r[map.Z], out double z)) p.Z = z;
                else { p.HasZ = false; if (map.Z >= 0) p.Issues |= PointIssue.MissingZ; }
                p.Name = map.P >= 0 && map.P < r.Length && r[map.P].Length > 0 ? r[map.P] : (autoNum).ToString(Inv);
                autoNum++;
                if (map.D >= 0 && map.D < r.Length) p.Desc = r[map.D];
                else if (map.D < 0 && r.Length > map.MaxIndex + 1)
                {
                    // keep extra trailing text as description (e.g. LandStar code columns)
                    var extra = r.Skip(map.MaxIndex + 1).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !TryNum(s, out _));
                    if (extra != null) p.Desc = extra;
                }
                res.Points.Add(p);
            }
            if (res.SkippedLines > 0) res.Warnings.Add(string.Format(Lang.T("SkippedLines"), res.SkippedLines));
            return res;
        }

        private static bool TryCoord(string s, out double v)
        {
            if (TryNum(s, out v)) return true;
            return AngleFormat.TryParse(s, out v) && s.IndexOfAny(new[] { '°', '\'', ':', 'd', 'D' }) >= 0;
        }

        // ------------------------------------------------------------------ LandXML
        public static ReadResult ReadLandXml(string path)
        {
            var res = new ReadResult();
            var doc = XDocument.Load(path);
            int auto = 1;
            foreach (var cg in doc.Descendants().Where(x => x.Name.LocalName == "CgPoint"))
            {
                var parts = (cg.Value ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { res.SkippedLines++; continue; }
                if (!TryNum(parts[0], out double n) || !TryNum(parts[1], out double e)) { res.SkippedLines++; continue; }
                var p = new SurveyPoint
                {
                    N = n, E = e,
                    Name = (string)cg.Attribute("name") ?? (string)cg.Attribute("pntRef") ?? (auto).ToString(Inv),
                    Desc = (string)cg.Attribute("code") ?? (string)cg.Attribute("desc") ?? "",
                };
                auto++;
                if (parts.Length >= 3 && TryNum(parts[2], out double z)) p.Z = z; else p.HasZ = false;
                res.Points.Add(p);
            }
            res.Map = new ColumnMap { Label = "LandXML" };
            return res;
        }

        // ------------------------------------------------------------------ KML (lon,lat,alt)
        public static ReadResult ReadKml(string path)
        {
            var res = new ReadResult { IsGeographic = true };
            var doc = XDocument.Load(path);
            int auto = 1;
            foreach (var pm in doc.Descendants().Where(x => x.Name.LocalName == "Placemark"))
            {
                var name = pm.Elements().FirstOrDefault(x => x.Name.LocalName == "name")?.Value;
                var desc = pm.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "";
                foreach (var pt in pm.Descendants().Where(x => x.Name.LocalName == "Point"))
                {
                    var coords = pt.Descendants().FirstOrDefault(x => x.Name.LocalName == "coordinates")?.Value;
                    if (coords == null) continue;
                    var c = coords.Trim().Split(',');
                    if (c.Length < 2 || !TryNum(c[0], out double lon) || !TryNum(c[1], out double lat)) { res.SkippedLines++; continue; }
                    var p = new SurveyPoint { E = lon, N = lat, Name = string.IsNullOrWhiteSpace(name) ? (auto).ToString(Inv) : name.Trim(), Desc = StripHtml(desc) };
                    auto++;
                    if (c.Length >= 3 && TryNum(c[2], out double z)) p.Z = z; else p.HasZ = false;
                    res.Points.Add(p);
                }
            }
            res.Map = new ColumnMap { Label = "KML" };
            return res;
        }

        private static string StripHtml(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "").Trim();

        // ------------------------------------------------------------------ GeoJSON (Point / MultiPoint)
        public static ReadResult ReadGeoJson(string path)
        {
            var res = new ReadResult { IsGeographic = true, Map = new ColumnMap { Label = "GeoJSON" } };
            string json = ReadAllTextSmart(path);
            var root = MiniJson.Parse(json);
            int auto = 1;
            void AddPoint(List<object> c, Dictionary<string, object> props)
            {
                if (c == null || c.Count < 2) return;
                var p = new SurveyPoint { E = Convert.ToDouble(c[0], Inv), N = Convert.ToDouble(c[1], Inv) };
                if (c.Count >= 3) p.Z = Convert.ToDouble(c[2], Inv); else p.HasZ = false;
                p.Name = PropString(props, "name", "Name", "id", "ID", "point", "PN") ?? (auto).ToString(Inv);
                p.Desc = PropString(props, "desc", "description", "code", "Code", "DESC") ?? "";
                auto++;
                res.Points.Add(p);
            }
            void Geometry(Dictionary<string, object> g, Dictionary<string, object> props)
            {
                if (g == null) return;
                string type = g.ContainsKey("type") ? g["type"] as string : null;
                if (type == "Point") AddPoint(g["coordinates"] as List<object>, props);
                else if (type == "MultiPoint")
                    foreach (var c in (g["coordinates"] as List<object>) ?? new List<object>()) AddPoint(c as List<object>, props);
            }
            var obj = root as Dictionary<string, object>;
            if (obj == null) return res;
            string rt = obj.ContainsKey("type") ? obj["type"] as string : null;
            if (rt == "FeatureCollection" && obj.ContainsKey("features"))
            {
                foreach (var f in (obj["features"] as List<object>) ?? new List<object>())
                {
                    var fd = f as Dictionary<string, object>;
                    if (fd == null) continue;
                    Geometry(fd.ContainsKey("geometry") ? fd["geometry"] as Dictionary<string, object> : null,
                             fd.ContainsKey("properties") ? fd["properties"] as Dictionary<string, object> : null);
                }
            }
            else if (rt == "Feature")
                Geometry(obj["geometry"] as Dictionary<string, object>, obj.ContainsKey("properties") ? obj["properties"] as Dictionary<string, object> : null);
            else Geometry(obj, null);
            return res;
        }

        private static string PropString(Dictionary<string, object> props, params string[] keys)
        {
            if (props == null) return null;
            foreach (var k in keys)
                if (props.TryGetValue(k, out object v) && v != null)
                    return Convert.ToString(v, Inv);
            return null;
        }
    }

    /// <summary>Tiny JSON parser (objects → Dictionary, arrays → List, numbers → double).</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r');
                    else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }
}
