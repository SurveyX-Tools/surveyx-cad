using System;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.Cad
{
    public static class CadView
    {
        public static void ZoomTo(Extents3d ext, double margin = 1.2)
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (var view = ed.GetCurrentView())
            {
                // transform WCS extents into the view's display coordinate system
                Matrix3d wcs2dcs = Matrix3d.PlaneToWorld(view.ViewDirection);
                wcs2dcs = Matrix3d.Displacement(view.Target - Point3d.Origin) * wcs2dcs;
                wcs2dcs = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) * wcs2dcs;
                wcs2dcs = wcs2dcs.Inverse();
                var e = ext;
                e.TransformBy(wcs2dcs);
                double w = Math.Max(e.MaxPoint.X - e.MinPoint.X, 1e-3) * margin;
                double h = Math.Max(e.MaxPoint.Y - e.MinPoint.Y, 1e-3) * margin;
                view.CenterPoint = new Point2d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2);
                view.Width = w;
                view.Height = h;
                ed.SetCurrentView(view);
            }
        }
    }

    /// <summary>Optional Civil 3D features, reached by reflection so the plugin still loads in plain AutoCAD.</summary>
    public static class CivilBridge
    {
        private static Assembly Civil =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "AeccDbMgd", StringComparison.OrdinalIgnoreCase));

        public static bool Available => Civil != null;

        /// <summary>Creates a Civil 3D TIN surface from points. Returns the surface name.</summary>
        public static string CreateTinSurface(string name, Point3dCollection pts)
        {
            var asm = Civil ?? throw new InvalidOperationException(Lang.T("NeedsCivil"));
            var tinType = asm.GetType("Autodesk.Civil.DatabaseServices.TinSurface", true);
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var create = tinType.GetMethod("Create", new[] { typeof(Database), typeof(string) })
                         ?? throw new MissingMethodException("TinSurface.Create");
            string unique = name;
            for (int i = 2; i < 1000; i++)
            {
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var id = (ObjectId)create.Invoke(null, new object[] { db, unique });
                        var surf = tr.GetObject(id, OpenMode.ForWrite);
                        var add = tinType.GetMethod("AddVertices", new[] { typeof(Point3dCollection) })
                                  ?? throw new MissingMethodException("TinSurface.AddVertices");
                        add.Invoke(surf, new object[] { pts });
                        tr.Commit();
                        return unique;
                    }
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null && ex.InnerException.Message.IndexOf("exist", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    unique = name + " (" + i + ")";
                }
            }
            throw new InvalidOperationException("Could not create surface.");
        }

        /// <summary>Creates Civil 3D COGO points. Returns number created.</summary>
        public static int CreateCogoPoints(System.Collections.Generic.IList<SurveyPoint> pts)
        {
            var asm = Civil ?? throw new InvalidOperationException(Lang.T("NeedsCivil"));
            var docType = asm.GetType("Autodesk.Civil.ApplicationServices.CivilDocument", false)
                          ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Autodesk.Civil.ApplicationServices.CivilDocument", false)).FirstOrDefault(t => t != null)
                          ?? throw new MissingMemberException("CivilDocument");
            var db = AcApp.DocumentManager.MdiActiveDocument.Database;
            var civDoc = docType.GetMethod("GetCivilDocument", new[] { typeof(Database) }).Invoke(null, new object[] { db });
            var cogo = docType.GetProperty("CogoPoints").GetValue(civDoc, null);
            var addM = cogo.GetType().GetMethods().FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == "Add" && p.Length == 3 && p[0].ParameterType == typeof(Point3d) && p[1].ParameterType == typeof(string) && p[2].ParameterType == typeof(bool);
            }) ?? throw new MissingMethodException("CogoPointCollection.Add");
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var p in pts)
                {
                    var id = (ObjectId)addM.Invoke(cogo, new object[] { new Point3d(p.E, p.N, p.HasZ ? p.Z : 0), p.Desc ?? "", true });
                    try
                    {
                        var obj = tr.GetObject(id, OpenMode.ForWrite);
                        obj.GetType().GetProperty("PointName")?.SetValue(obj, p.Name, null);
                    }
                    catch { /* duplicate names are allowed to fail silently: number is kept */ }
                    n++;
                }
                tr.Commit();
            }
            return n;
        }
    }
}
