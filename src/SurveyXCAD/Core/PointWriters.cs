using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace SurveyX.Core
{
    public enum ExportFormat { Csv, Txt, LandXml, Kml, GeoJson }

    public static class PointWriters
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string SaveFilter =
            "CSV (comma)|*.csv|TXT (tab)|*.txt|LandXML|*.xml|KML (Google Earth)|*.kml|GeoJSON|*.geojson";

        public static ExportFormat FromFilterIndex(int idx1Based)
        {
            switch (idx1Based)
            {
                case 2: return ExportFormat.Txt;
                case 3: return ExportFormat.LandXml;
                case 4: return ExportFormat.Kml;
                case 5: return ExportFormat.GeoJson;
                default: return ExportFormat.Csv;
            }
        }

        /// <param name="toWgs84">Required for KML/GeoJSON when points are projected. Null when already lon/lat.</param>
        public static void Write(string path, ExportFormat fmt, IList<SurveyPoint> pts, string order, int decimals,
            CoordinateTransformer toWgs84 = null, bool header = true)
        {
            switch (fmt)
            {
                case ExportFormat.Csv: WriteDelimited(path, pts, order, decimals, ",", header); break;
                case ExportFormat.Txt: WriteDelimited(path, pts, order, decimals, "\t", header); break;
                case ExportFormat.LandXml: WriteLandXml(path, pts, decimals); break;
                case ExportFormat.Kml: WriteKml(path, pts, toWgs84); break;
                case ExportFormat.GeoJson: WriteGeoJson(path, pts, toWgs84); break;
            }
        }

        private static string Num(double v, int dec) => v.ToString("F" + dec, Inv);

        private static string Csv(string s, string delim)
        {
            s = s ?? "";
            return s.Contains(delim) || s.Contains("\"") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static void WriteDelimited(string path, IList<SurveyPoint> pts, string order, int dec, string delim, bool header, bool geographic = false)
        {
            if (string.IsNullOrEmpty(order)) order = "PNEZD";
            var sb = new StringBuilder();
            if (header)
                sb.AppendLine(string.Join(delim, order.Select(c => c == 'P' ? "Point" : c == 'N' ? (geographic ? "Latitude" : "Northing") : c == 'E' ? (geographic ? "Longitude" : "Easting") : c == 'Z' ? "Elevation" : "Description")));
            foreach (var p in pts)
            {
                var cells = order.Select(c =>
                    c == 'P' ? Csv(p.Name, delim) :
                    c == 'N' ? Num(p.N, dec) :
                    c == 'E' ? Num(p.E, dec) :
                    c == 'Z' ? (p.HasZ ? Num(p.Z, dec) : "") :
                    Csv(p.Desc, delim));
                sb.AppendLine(string.Join(delim, cells));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static void WriteLandXml(string path, IList<SurveyPoint> pts, int dec)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<LandXML xmlns=\"http://www.landxml.org/schema/LandXML-1.2\" version=\"1.2\" date=\"" +
                DateTime.Now.ToString("yyyy-MM-dd", Inv) + "\" time=\"" + DateTime.Now.ToString("HH:mm:ss", Inv) + "\">");
            sb.AppendLine("  <Units><Metric areaUnit=\"squareMeter\" linearUnit=\"meter\" volumeUnit=\"cubicMeter\" temperatureUnit=\"celsius\" pressureUnit=\"milliBars\"/></Units>");
            sb.AppendLine("  <Application name=\"" + AppInfo.Name + "\" version=\"" + AppInfo.Version + "\" manufacturer=\"" + SecurityElement.Escape(AppInfo.Author) + "\"/>");
            sb.AppendLine("  <CgPoints>");
            foreach (var p in pts)
            {
                string z = p.HasZ ? " " + Num(p.Z, dec) : "";
                sb.AppendLine("    <CgPoint name=\"" + SecurityElement.Escape(p.Name) + "\"" +
                    (string.IsNullOrEmpty(p.Desc) ? "" : " code=\"" + SecurityElement.Escape(p.Desc) + "\"") + ">" +
                    Num(p.N, dec) + " " + Num(p.E, dec) + z + "</CgPoint>");
            }
            sb.AppendLine("  </CgPoints>");
            sb.AppendLine("</LandXML>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void LonLat(SurveyPoint p, CoordinateTransformer t, out double lon, out double lat)
        {
            if (t == null) { lon = p.E; lat = p.N; return; }
            t.Transform(p.E, p.N, out lon, out lat);
        }

        public static void WriteKml(string path, IList<SurveyPoint> pts, CoordinateTransformer toWgs84)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
            sb.AppendLine("<name>" + SecurityElement.Escape(Path.GetFileNameWithoutExtension(path)) + "</name>");
            sb.AppendLine("<Style id=\"sx\"><IconStyle><scale>0.7</scale><Icon><href>http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png</href></Icon></IconStyle><LabelStyle><scale>0.7</scale></LabelStyle></Style>");
            foreach (var p in pts)
            {
                LonLat(p, toWgs84, out double lon, out double lat);
                sb.Append("<Placemark><name>").Append(SecurityElement.Escape(p.Name)).Append("</name>");
                string d = "E=" + Num(p.E, 3) + " N=" + Num(p.N, 3) + (p.HasZ ? " Z=" + Num(p.Z, 3) : "") + (string.IsNullOrEmpty(p.Desc) ? "" : " " + p.Desc);
                sb.Append("<description>").Append(SecurityElement.Escape(d)).Append("</description><styleUrl>#sx</styleUrl>");
                sb.Append("<Point><coordinates>").Append(Num(lon, 9)).Append(',').Append(Num(lat, 9)).Append(',').Append(Num(p.HasZ ? p.Z : 0, 3))
                  .AppendLine("</coordinates></Point></Placemark>");
            }
            sb.AppendLine("</Document></kml>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string J(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

        public static void WriteGeoJson(string path, IList<SurveyPoint> pts, CoordinateTransformer toWgs84)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                LonLat(p, toWgs84, out double lon, out double lat);
                if (i > 0) sb.Append(',');
                sb.Append("\n{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[")
                  .Append(Num(lon, 9)).Append(',').Append(Num(lat, 9));
                if (p.HasZ) sb.Append(',').Append(Num(p.Z, 3));
                sb.Append("]},\"properties\":{\"name\":").Append(J(p.Name))
                  .Append(",\"desc\":").Append(J(p.Desc))
                  .Append(",\"E\":").Append(Num(p.E, 4)).Append(",\"N\":").Append(Num(p.N, 4));
                if (p.HasZ) sb.Append(",\"Z\":").Append(Num(p.Z, 4));
                sb.Append("}}");
            }
            sb.Append("\n]}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
