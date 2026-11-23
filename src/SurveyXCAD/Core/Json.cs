using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurveyX.Core
{
    /// <summary>Minimal JSON writer for Dictionary / List / primitives (pairs with MiniJson.Parse).</summary>
    public static class JsonWriter
    {
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            W(sb, o);
            return sb.ToString();
        }

        private static void W(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s: Str(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: W(sb, (double)f); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(sb, kv.Key);
                        sb.Append(':');
                        W(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                case IEnumerable en:
                    sb.Append('[');
                    bool f1 = true;
                    foreach (var x in en)
                    {
                        if (!f1) sb.Append(',');
                        f1 = false;
                        W(sb, x);
                    }
                    sb.Append(']');
                    return;
                default:
                    Str(sb, Convert.ToString(o, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>Convenience accessors for parsed JSON objects (tool inputs).</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        public static List<object> Arr(params object[] items) => new List<object>(items);

        public static string Str(this IDictionary<string, object> d, string k, string def = null) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;

        public static double Num(this IDictionary<string, object> d, string k, double def = double.NaN)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
            if (v is double x) return x;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : def;
        }

        public static bool Bool(this IDictionary<string, object> d, string k, bool def = false) =>
            d != null && d.TryGetValue(k, out var v) && v is bool b ? b : def;

        public static Dictionary<string, object> Sub(this IDictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> List(this IDictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v as List<object> : null;
    }
}
