using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SurveyX.Core
{
    /// <summary>
    /// Minimal PDF writer: one full-page JPEG image per page. Report pages are rendered by GDI+
    /// (which shapes Arabic correctly), so the PDF needs no font embedding.
    /// </summary>
    public static class PdfWriter
    {
        public sealed class Page
        {
            public byte[] Jpeg;
            public int PixelWidth, PixelHeight;
            public double WidthPt = 595.28, HeightPt = 841.89; // A4 portrait
        }

        public static void Write(string path, IList<Page> pages, string title, string author)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                Write(fs, pages, title, author);
        }

        public static void Write(Stream s, IList<Page> pages, string title, string author)
        {
            var inv = CultureInfo.InvariantCulture;
            var offsets = new List<long>();
            long pos = 0;
            void Raw(byte[] b) { s.Write(b, 0, b.Length); pos += b.Length; }
            void Txt(string t) => Raw(Encoding.ASCII.GetBytes(t));
            void Obj(int n) { while (offsets.Count < n) offsets.Add(0); offsets[n - 1] = pos; Txt(n + " 0 obj\n"); }

            Raw(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4', 10, (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, 10 });

            int n = pages.Count;
            // object numbers: 1 catalog, 2 pages, 3 info, then per page: page, contents, image
            int PageObj(int i) => 4 + i * 3;

            Obj(1); Txt("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            Obj(2);
            var kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append(PageObj(i)).Append(" 0 R ");
            Txt("<< /Type /Pages /Count " + n + " /Kids [ " + kids + "] >>\nendobj\n");
            Obj(3);
            Txt("<< /Title " + PdfString(title) + " /Author " + PdfString(author) + " /Creator (SurveyX CAD) /Producer (SurveyX CAD) /CreationDate (D:" +
                DateTime.Now.ToString("yyyyMMddHHmmss", inv) + ") >>\nendobj\n");

            for (int i = 0; i < n; i++)
            {
                var p = pages[i];
                int po = PageObj(i), co = po + 1, io = po + 2;
                string w = p.WidthPt.ToString("0.##", inv), h = p.HeightPt.ToString("0.##", inv);
                Obj(po);
                Txt("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + w + " " + h + "] /Resources << /XObject << /Im0 " + io +
                    " 0 R >> >> /Contents " + co + " 0 R >>\nendobj\n");
                string content = "q " + w + " 0 0 " + h + " 0 0 cm /Im0 Do Q\n";
                Obj(co);
                Txt("<< /Length " + content.Length + " >>\nstream\n" + content + "endstream\nendobj\n");
                Obj(io);
                Txt("<< /Type /XObject /Subtype /Image /Width " + p.PixelWidth + " /Height " + p.PixelHeight +
                    " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + p.Jpeg.Length + " >>\nstream\n");
                Raw(p.Jpeg);
                Txt("\nendstream\nendobj\n");
            }

            long xref = pos;
            int count = offsets.Count + 1;
            Txt("xref\n0 " + count + "\n0000000000 65535 f \n");
            foreach (var o in offsets) Txt(o.ToString("0000000000", inv) + " 00000 n \n");
            Txt("trailer\n<< /Size " + count + " /Root 1 0 R /Info 3 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }

        /// <summary>PDF text string; non-ASCII written as UTF-16BE hex.</summary>
        private static string PdfString(string s)
        {
            s = s ?? "";
            bool ascii = true;
            foreach (char c in s) if (c > 126 || c < 32) { ascii = false; break; }
            if (ascii) return "(" + s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
            var sb = new StringBuilder("<FEFF");
            foreach (char c in s) sb.Append(((int)c).ToString("X4"));
            return sb.Append('>').ToString();
        }
    }
}
