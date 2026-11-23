using System;
using System.Drawing;
using System.IO;

namespace SurveyX.UI
{
    /// <summary>SurveyX brand: colours taken from the SurveyX logo (navy background, teal and amber X).</summary>
    public static class Brand
    {
        public static readonly Color Navy = Color.FromArgb(0x08, 0x24, 0x40);
        public static readonly Color NavyLight = Color.FromArgb(0x0E, 0x3A, 0x5C);
        public static readonly Color Teal = Color.FromArgb(0x2E, 0xC4, 0xB0);
        public static readonly Color TealLight = Color.FromArgb(0x56, 0xE3, 0xCF);
        public static readonly Color Amber = Color.FromArgb(0xFA, 0xBA, 0x3E);

        private static Image _logo;
        private static Icon _icon;

        public static Image Logo => _logo ?? (_logo = Load("SurveyX.logo.png", s => Image.FromStream(s)));
        public static Icon Icon => _icon ?? (_icon = Load("SurveyX.surveyx.ico", s => new Icon(s)));

        private static T Load<T>(string name, Func<Stream, T> make) where T : class
        {
            try
            {
                var s = typeof(Brand).Assembly.GetManifestResourceStream(name);
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                return make(ms);
            }
            catch { return null; }
        }

        public static Bitmap LogoSized(int size)
        {
            var b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                if (Logo != null) g.DrawImage(Logo, 0, 0, size, size);
                else g.Clear(Navy);
            }
            return b;
        }
    }
}
