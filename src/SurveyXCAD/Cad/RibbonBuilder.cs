using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Builds the "SurveyX" ribbon tab with generated icons (no image files needed).</summary>
    public static class RibbonBuilder
    {
        private const string TabId = "SURVEYX_TAB";

        public static void InitWhenReady()
        {
            if (ComponentManager.Ribbon != null) Build();
            else ComponentManager.ItemInitialized += OnItemInitialized;
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Build();
        }

        public static void Rebuild()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            var old = ribbon.FindTab(TabId);
            if (old != null) ribbon.Tabs.Remove(old);
            Build();
        }

        private static void Build()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null || ribbon.FindTab(TabId) != null) return;
            var tab = new RibbonTab { Title = "SurveyX", Id = TabId };
            ribbon.Tabs.Add(tab);

            tab.Panels.Add(Panel(Lang.T("RibPoints"),
                Btn(Lang.T("RibImport"), "SXIMPORT", "IN", Color.FromRgb(0x1E, 0x88, 0xE5), Lang.T("TipImport")),
                Btn(Lang.T("RibExport"), "SXEXPORT", "OUT", Color.FromRgb(0x43, 0xA0, 0x47), Lang.T("TipExport")),
                Btn(Lang.T("RibAddPoint"), "SXPOINT", "+", Color.FromRgb(0xE5, 0x39, 0x35), Lang.T("TipAddPoint"))));
            tab.Panels.Add(Panel(Lang.T("RibCoords"),
                Btn(Lang.T("RibConvert"), "SXCONVERT", "XY", Color.FromRgb(0x8E, 0x24, 0xAA), Lang.T("TipConvert")),
                Btn(Lang.T("RibTable"), "SXTABLE", "TB", Color.FromRgb(0xFB, 0x8C, 0x00), Lang.T("TipTable")),
                Btn(Lang.T("RibLabel"), "SXLABEL", "EN", Color.FromRgb(0x00, 0x89, 0x7B), Lang.T("TipLabel"))));
            tab.Panels.Add(Panel(Lang.T("RibEarthworks"),
                Btn(Lang.T("RibCutFill"), "SXCUTFILL", "C/F", Color.FromRgb(0xC6, 0x28, 0x28), Lang.T("TipCutFill")),
                Btn(Lang.T("RibLevelCheck"), "SXLEVELCHECK", "±", Color.FromRgb(0x2E, 0x7D, 0x32), Lang.T("TipLevelCheck")),
                Btn(Lang.T("RibSurface"), "SXSURFACE", "TIN", Color.FromRgb(0x6D, 0x4C, 0x41), Lang.T("TipSurface")),
                Btn(Lang.T("RibSections"), "SXSECTIONS", "SEC", Color.FromRgb(0x45, 0x5A, 0x64), Lang.T("TipSections"))));
            tab.Panels.Add(Panel(Lang.T("RibPrimavera"),
                Btn(Lang.T("RibP6"), "SXP6", "P6", Color.FromRgb(0xD8, 0x43, 0x15), Lang.T("TipP6"))));
            tab.Panels.Add(Panel(Lang.T("RibTools"),
                Btn(Lang.T("RibSettings"), "SXSETTINGS", "ST", Color.FromRgb(0x54, 0x6E, 0x7A), Lang.T("TipSettings")),
                Btn(Lang.T("RibAbout"), "SXABOUT", "?", Color.FromRgb(0x37, 0x47, 0x4F), Lang.T("TipAbout"))));
        }

        private static RibbonPanel Panel(string title, params RibbonButton[] buttons)
        {
            var src = new RibbonPanelSource { Title = title };
            foreach (var b in buttons) src.Items.Add(b);
            return new RibbonPanel { Source = src };
        }

        private static RibbonButton Btn(string text, string cmd, string glyph, Color color, string tip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Icon(glyph, color, 32),
                Image = Icon(glyph, color, 16),
                ToolTip = tip,
                CommandParameter = cmd,
                CommandHandler = new CmdHandler(),
            };
        }

        private static ImageSource Icon(string glyph, Color color, int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var rect = new Rect(1, 1, size - 2, size - 2);
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, size / 6.0, size / 6.0);
                var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    glyph.Length > 2 ? size * 0.32 : size * 0.45, Brushes.White, 1.0);
                dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private sealed class CmdHandler : System.Windows.Input.ICommand
        {
#pragma warning disable 67
            public event EventHandler CanExecuteChanged;
#pragma warning restore 67
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                if (parameter is RibbonButton b && b.CommandParameter is string cmd)
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\x03\x03_" + cmd + " ", true, false, true);
            }
        }
    }
}
