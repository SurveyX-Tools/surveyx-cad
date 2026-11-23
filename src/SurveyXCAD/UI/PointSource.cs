using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using SurveyX.Cad;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.UI
{
    /// <summary>"Points from a file or from the drawing" picker with a status line.</summary>
    public sealed class PointSource : GroupBox
    {
        public List<SurveyPoint> Points { get; private set; } = new List<SurveyPoint>();
        public string FilePath { get; private set; }
        public event EventHandler Changed;
        public double Densify { get; set; } = 0;

        private readonly Label _status = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Form _owner;

        public PointSource(Form owner, string titleKey, string rememberedPath = null)
        {
            _owner = owner;
            Text = Lang.T(titleKey);
            Dock = DockStyle.Top;
            Height = 92;
            Padding = new Padding(8, 4, 8, 4);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var bFile = UiKit.Btn("FromFile");
            var bDwg = UiKit.Btn("FromDrawing");
            bFile.Click += (s, e) => PickFile();
            bDwg.Click += (s, e) => PickDrawing();
            t.Controls.Add(bFile, 0, 0);
            t.Controls.Add(bDwg, 1, 0);
            t.Controls.Add(_status, 2, 0);
            Controls.Add(t);
            _status.Text = Lang.T("NotSelected");
            if (!string.IsNullOrEmpty(rememberedPath) && System.IO.File.Exists(rememberedPath))
                _status.Text = Lang.T("LastFile") + " " + System.IO.Path.GetFileName(rememberedPath) + "  — " + Lang.T("PressToLoad");
            _remembered = rememberedPath;
        }

        private readonly string _remembered;

        /// <summary>Loads the remembered file if nothing was chosen yet.</summary>
        public bool EnsureLoaded()
        {
            if (Points.Count > 0) return true;
            if (!string.IsNullOrEmpty(_remembered) && System.IO.File.Exists(_remembered)) return LoadFile(_remembered);
            return false;
        }

        private void PickFile()
        {
            var f = UiKit.BrowseOpen(_owner, PointFileReader.OpenFilter);
            if (f != null) LoadFile(f);
        }

        private bool LoadFile(string f)
        {
            try
            {
                _owner.Cursor = Cursors.WaitCursor;
                var r = PointFileReader.Read(f);
                if (r.IsGeographic) { UiKit.Error(Lang.T("GeoFileNotAllowed")); return false; }
                Set(r.Points, System.IO.Path.GetFileName(f));
                FilePath = f;
                return true;
            }
            catch (Exception ex) { UiKit.Error(Lang.T("ReadError") + "\n" + ex.Message); return false; }
            finally { _owner.Cursor = Cursors.Default; }
        }

        private void PickDrawing()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (ed.StartUserInteraction(_owner))
            {
                var res = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect points / 3D polylines / lines: " }, CadPoints.AnyPointLike());
                if (res.Status != PromptStatus.OK) return;
                var pts = CadPoints.FromIds(res.Value.GetObjectIds(), Densify);
                FilePath = null;
                Set(pts, Lang.T("Drawing"));
            }
        }

        private void Set(List<SurveyPoint> pts, string from)
        {
            Points = pts.Where(p => p.HasZ).ToList();
            _status.ForeColor = Points.Count >= 3 ? Color.DarkGreen : Color.DarkRed;
            _status.Text = string.Format(Lang.T("PointsLoaded"), Points.Count, from);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Optional boundary (closed polyline) picker.</summary>
    public sealed class BoundaryPicker : FlowLayoutPanel
    {
        public List<(double x, double y)> Polygon { get; private set; }
        private readonly Label _lbl = new Label { AutoSize = true, Margin = new Padding(6, 9, 3, 3), ForeColor = Color.DimGray };
        private readonly Form _owner;

        public BoundaryPicker(Form owner)
        {
            _owner = owner;
            AutoSize = true; Dock = DockStyle.Top; WrapContents = false;
            var b = UiKit.Btn("SelectBoundary");
            var c = UiKit.Btn("ClearBoundary");
            b.Click += (s, e) => Pick();
            c.Click += (s, e) => { Polygon = null; _lbl.Text = Lang.T("NoBoundary"); };
            Controls.Add(b); Controls.Add(c); Controls.Add(_lbl);
            _lbl.Text = Lang.T("NoBoundary");
        }

        private void Pick()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (ed.StartUserInteraction(_owner))
            {
                var r = ed.GetEntity(new PromptEntityOptions("\nSelect closed boundary polyline: "));
                if (r.Status != PromptStatus.OK) return;
                var poly = CadPoints.Boundary(r.ObjectId);
                if (poly.Count < 3) { UiKit.Error(Lang.T("BadBoundary")); return; }
                Polygon = poly;
                _lbl.Text = string.Format(Lang.T("BoundaryArea"), Geo2D.Area(poly).ToString("N1"));
            }
        }
    }
}
