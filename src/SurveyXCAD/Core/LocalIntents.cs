using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SurveyX.Core
{
    /// <summary>
    /// Free, offline understanding of short requests ("قطع وردم", "حصر كميات", "بريمافيرا"...): they open the right
    /// SurveyX command directly, with no AI call and no cost. Longer or detailed requests go to the AI assistant.
    /// </summary>
    public static class LocalIntents
    {
        public sealed class Intent
        {
            public string Command;   // AutoCAD command to run
            public string LabelKey;  // Lang key shown in the chat
            public string[] Keys;    // normalised keywords
        }

        // Order matters: more specific intents first.
        public static readonly List<Intent> All = new List<Intent>
        {
            I("SXCUMULATIVE", "RibCumulative", "تراكمي", "التراكمي", "cumulative"),
            I("SXCUTFILL", "RibCutFill", "قطع ورد", "قطع و رد", "حفر ورد", "حفر و رد", "القطع والرد", "cut fill", "cut & fill", "cut and fill", "cutfill"),
            I("SXLEVELCHECK", "RibLevelCheck", "فحص منسوب", "فحص المنسوب", "فحص مناسيب", "فحص المناسيب", "مناسيب التصميم", "level check", "check level"),
            I("SXQTO", "RibQto", "حصر", "take off", "takeoff", "take-off", "qto", "quantity"),
            I("SXSETOUT", "RibSetOut", "جدول توقيع", "جدول التوقيع", "توقيع نقط", "setting out", "setout", "set out", "chainage offset"),
            I("SXTABLE", "RibTable", "جدول احداثيات", "جدول الاحداثيات", "coordinate table", "coordinates table"),
            I("SXCONVERT", "RibConvert", "تحويل احداثيات", "تحويل الاحداثيات", "حول الاحداثيات", "حول احداثيات", "convert coordinates", "coordinate conversion"),
            I("SXSECTIONS", "RibSections", "قطاع", "قطاعات", "القطاعات", "section", "sections", "profile"),
            I("SXSURFACE", "RibSurface", "سطح", "كنتور", "خريطه مناسيب", "surface", "contour", "contours", "tin"),
            I("SXP6", "RibP6", "بريمافيرا", "بريمافيرا", "بريمفيرا", "primavera", "p6"),
            I("SXIMPORT", "RibImport", "استيراد", "استورد", "import"),
            I("SXEXPORT", "RibExport", "تصدير", "صدر النقط", "export"),
            I("SXLABEL", "RibLabel", "ليدر", "leader"),
            I("SXPOINT", "RibAddPoint", "اضافه نقط", "ضيف نقط", "add point", "add points"),
            I("SXSETTINGS", "RibSettings", "اعدادات", "الاعدادات", "settings"),
            I("SXHELP", "RibAbout", "الاوامر", "مساعده", "help", "commands"),
        };

        private static Intent I(string cmd, string label, params string[] keys) =>
            new Intent { Command = cmd, LabelKey = label, Keys = keys.Select(Normalize).ToArray() };

        /// <summary>Arabic/English normalisation: lower case, no diacritics/tatweel, unified alef/yaa/taa marbuta.</summary>
        public static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c0 in (s ?? "").ToLowerInvariant())
            {
                char c = c0;
                if (c >= 'ً' && c <= 'ْ' || c == 'ـ') continue;          // harakat, tatweel
                if (c == 'أ' || c == 'إ' || c == 'آ' || c == 'ٱ') c = 'ا';
                else if (c == 'ى') c = 'ي';
                else if (c == 'ة') c = 'ه';
                else if (c == 'ؤ') c = 'و';
                else if (c == 'ئ') c = 'ي';
                sb.Append(c);
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ', '\t', '\r', '\n', '،', ',', '؟', '?', '!', '.' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Returns the intent for a short, simple request; null when the request needs real understanding
        /// (long sentences, numbers, file names, several tasks at once) so the AI handles it.
        /// </summary>
        public static Intent Match(string text)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text ?? "", @"\.[A-Za-z]{2,5}\b|[\\/:]")) return null; // file names / paths → AI
            string n = Normalize(text);
            if (n.Length == 0) return null;
            int words = n.Split(' ').Length;
            if (words > 6) return null;                                  // a real sentence → AI
            if ((" " + n + " ").Replace(" p6 ", " ").Any(char.IsDigit)) return null; // numbers (except "P6") → AI
            var hits = All.Where(i => i.Keys.Any(k => Contains(n, k))).ToList();
            return hits.Count >= 1 && hits.Select(h => h.Command).Distinct().Count() == 1 ? hits[0]
                 : hits.Count > 1 && hits[0].Command == "SXCUMULATIVE" ? hits[0]   // "قطع وردم تراكمي"
                 : null;                                                          // several different tasks → AI
        }

        private static bool Contains(string text, string key)
        {
            if (key.Length <= 3 && key.All(c => c < 128)) // short English keys (p6, tin, qto) must be whole words
                return (" " + text + " ").Contains(" " + key + " ");
            return text.Contains(key);
        }
    }
}
