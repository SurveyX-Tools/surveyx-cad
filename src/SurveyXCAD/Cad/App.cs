using System;
using Autodesk.AutoCAD.Runtime;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(SurveyX.Cad.App))]

namespace SurveyX.Cad
{
    public class App : IExtensionApplication
    {
        public void Initialize()
        {
            Lang.Set(Settings.Current.Language);
            try { RibbonBuilder.InitWhenReady(); } catch { /* ribbon is optional */ }
            AcApp.Idle += OnFirstIdle;
        }

        private static void OnFirstIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= OnFirstIdle;
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\n" + AppInfo.Name + " " + AppInfo.Version + " loaded — by " + AppInfo.Author +
                    ". Commands: SXIMPORT, SXEXPORT, SXCONVERT, SXTABLE, SXLABEL, SXPOINT, SXSETTINGS, SXHELP\n");
            }
            catch { }
            if (Settings.Current.CheckUpdates) Updater.CheckInBackground();
        }

        public void Terminate() { }
    }
}
