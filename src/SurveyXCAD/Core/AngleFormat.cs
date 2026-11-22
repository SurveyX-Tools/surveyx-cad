using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SurveyX.Core
{
    public static class AngleFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Parses decimal degrees or DMS: 25.2048 | 25°12'17.3" | 25:12:17.3 | 25d12m17.3s | 25 12 17.3 N</summary>
        public static bool TryParse(string text, out double degrees)
        {
            degrees = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().ToUpperInvariant().Replace(',', '.');
            int sign = 1;
            if (s.EndsWith("S") || s.EndsWith("W")) { sign = -1; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("N") || s.EndsWith("E")) s = s.Substring(0, s.Length - 1);
            if (s.StartsWith("S") || s.StartsWith("W")) { sign = -1; s = s.Substring(1); }
            else if (s.StartsWith("N") || s.StartsWith("E")) s = s.Substring(1);
            s = s.Trim();
            if (s.StartsWith("-")) { sign = -sign; s = s.Substring(1); }

            if (double.TryParse(s, NumberStyles.Float, Inv, out double dec))
            {
                degrees = sign * dec; return true;
            }
            var parts = Regex.Split(s, @"[^0-9.]+");
            double[] v = new double[3]; int k = 0;
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (k >= 3) return false;
                if (!double.TryParse(p, NumberStyles.Float, Inv, out v[k])) return false;
                k++;
            }
            if (k == 0) return false;
            if (v[1] >= 60 || v[2] >= 60) return false;
            degrees = sign * (v[0] + v[1] / 60.0 + v[2] / 3600.0);
            return true;
        }

        public static string ToDms(double deg, bool isLat, int secDecimals = 4)
        {
            char hemi = isLat ? (deg < 0 ? 'S' : 'N') : (deg < 0 ? 'W' : 'E');
            double a = Math.Abs(deg);
            int d = (int)Math.Floor(a);
            double mFull = (a - d) * 60;
            int m = (int)Math.Floor(mFull);
            double sec = Math.Round((mFull - m) * 60, secDecimals);
            if (sec >= 60) { sec -= 60; m++; }
            if (m >= 60) { m -= 60; d++; }
            return string.Format(Inv, "{0}°{1:00}'{2}\" {3}", d, m, sec.ToString("00." + new string('0', secDecimals), Inv), hemi);
        }
    }
}
