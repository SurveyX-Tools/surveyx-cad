using System;
using System.Drawing;
using Autodesk.AutoCAD.Windows;
using SurveyX.Core;
using SurveyX.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    /// <summary>Dockable AutoCAD palette hosting the AI assistant chat.</summary>
    public static class AssistantPalette
    {
        private static PaletteSet _ps;
        private static string _lang;

        public static void Show()
        {
            if (_ps != null && _lang != Settings.Current.Language)
            {
                _ps.Visible = false;
                _ps.Dispose();
                _ps = null;
            }
            if (_ps == null)
            {
                _lang = Settings.Current.Language;
                _ps = new PaletteSet("SurveyX Assistant", new Guid("6f0c2a4e-8d1b-4b7e-9a51-3c2e7d9a1f44"))
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(340, 420),
                };
                var chat = new ChatControl(AssistantSpecs.Specs, AssistantTools.Run, AssistantSpecs.SystemPrompt, OpenSettings);
                _ps.Add(Lang.T("AiTitle"), chat);
                _ps.Size = new Size(430, 720);
                _ps.DockEnabled = DockSides.Left | DockSides.Right;
            }
            _ps.Visible = true;
        }

        private static void OpenSettings()
        {
            using (var f = new SettingsForm()) AcApp.ShowModalDialog(f);
        }
    }
}
