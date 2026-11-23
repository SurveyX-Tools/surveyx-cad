using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Cad;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.UI
{
    /// <summary>Long section, cross sections and end-area volumes along an alignment (any polyline / curve / Civil alignment).</summary>
    public sealed class SectionsForm : SxForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PointSource _exist, _design;
        private readonly Label _alignLbl = new Label { AutoSize = true, Margin = new Padding(6, 9, 3, 3), ForeColor = Color.DimGray };
        private readonly NumericUpDown _interval = N(1, 1000, 20, 0), _left = N(0, 500, 20, 1), _right = N(0, 500, 20, 1), _step = N(0.1m, 50, 1, 2),
            _vex = N(1, 100, 10, 1), _start = N(0, 1000000, 0, 3);
        private readonly Label _info = new Label { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Soft, Font = new Font("Segoe UI", 10.5f) };
        private List<(double x, double y)> _align;

        private static NumericUpDown N(decimal min, decimal max, decimal val, int dec) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dec, Width = 90 };

        public SectionsForm() : base("SectionsTitle", "HelpSections")
        {
            var st = Settings.Current;
            Size = new Size(820, 700);
            MinimumSize = new Size(740, 620);
            _exist = new PointSource(this, "ExistingSurface", st.LastOglPath) { Densify = 1.0 };
            _design = new PointSource(this, "DesignSurfaceOpt", st.LastDesignPath) { Densify = 1.0 };
            Set(_interval, st.SectionInterval); Set(_left, st.SectionLeft); Set(_right, st.SectionRight);
            Set(_step, st.SectionStep); Set(_vex, st.VerticalExaggeration); Set(_start, st.StartChainage);

            var alignBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            var bAlign = UiKit.Btn("SelectAlignment", true); bAlign.Click += (s, e) => PickAlignment();
            alignBar.Controls.Add(bAlign); alignBar.Controls.Add(_alignLbl);
            _alignLbl.Text = Lang.T("NoAlignment");

            var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            p.Controls.Add(UiKit.Pair("Interval", _interval));
            p.Controls.Add(UiKit.Pair("LeftWidth", _left));
            p.Controls.Add(UiKit.Pair("RightWidth", _right));
            p.Controls.Add(UiKit.Pair("OffsetStep", _step));
            p.Controls.Add(UiKit.Pair("VertExag", _vex));
            p.Controls.Add(UiKit.Pair("StartChainage", _start));

            var bLong = UiKit.Btn("DrawLongSection"); bLong.Click += (s, e) => LongSection();
            var bCross = UiKit.Btn("DrawCrossSections"); bCross.Click += (s, e) => CrossSections();
            var bVol = UiKit.Btn("VolumesReport", true); bVol.Click += (s, e) => Volumes();
            var close = UiKit.Btn("Close"); close.Click += (s, e) => Close(); CancelButton = close;

            Controls.Add(_info);
            Controls.Add(p);
            Controls.Add(_design);
            Controls.Add(_exist);
            Controls.Add(alignBar);
            Controls.Add(Header("SectionsTitle"));
            Controls.Add(UiKit.ButtonBar(close, bVol, bCross, bLong));
            _info.Text = Lang.T("SectionsIntro");
        }

        private static void Set(NumericUpDown n, double v) => n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)v));

        private void Save()
        {
            var st = Settings.Current;
            st.SectionInterval = (double)_interval.Value; st.SectionLeft = (double)_left.Value; st.SectionRight = (double)_right.Value;
            st.SectionStep = (double)_step.Value; st.VerticalExaggeration = (double)_vex.Value; st.StartChainage = (double)_start.Value;
            st.Save();
        }

        private void PickAlignment()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (ed.StartUserInteraction(this))
            {
                var r = ed.GetEntity(new PromptEntityOptions("\nSelect centreline (polyline / line / alignment / feature line): "));
                if (r.Status != PromptStatus.OK) return;
                var a = CadPoints.Alignment(r.ObjectId);
                if (a.Count < 2) { UiKit.Error(Lang.T("BadAlignment")); return; }
                _align = a;
                _alignLbl.Text = string.Format(Lang.T("AlignmentLength"), Geo2D.Length(a).ToString("N2"));
            }
        }

        private bool Ready(out List<Tin> surfaces, out string[] names)
        {
            surfaces = null; names = null;
            if (_align == null) { UiKit.Error(Lang.T("NoAlignment")); return false; }
            if (!_exist.EnsureLoaded() || _exist.Points.Count < 3) { UiKit.Error(Lang.T("ChooseExisting")); return false; }
            _design.EnsureLoaded();
            double me = Settings.Current.MaxEdge;
            surfaces = new List<Tin> { new Tin(_exist.Points, me) };
            var n = new List<string> { Lang.En("Existing") };
            if (_design.Points.Count >= 3) { surfaces.Add(new Tin(_design.Points, me)); n.Add(Lang.En("DesignNew")); }
            names = n.ToArray();
            Save();
            return true;
        }

        private Point3d? PickPoint(string msg)
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (ed.StartUserInteraction(this))
            {
                var r = ed.GetPoint(msg);
                if (r.Status != PromptStatus.OK) return null;
                return r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            }
        }

        private void LongSection()
        {
            try
            {
                if (!Ready(out var surfs, out var names)) return;
                var at = PickPoint("\nLong section insertion point (lower-left): ");
                if (at == null) return;
                Cursor = Cursors.WaitCursor;
                var data = Sections.LongSection(_align, surfs, Math.Max(1, (double)_interval.Value / 4));
                CadDraw.LongSection(at.Value, data, names, (double)_vex.Value, (double)_start.Value, Settings.Current.TextHeight, PointDrawer.Layer("LONG_SECTION"));
                _info.Text = string.Format(Lang.T("LongDone"), data.Count);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private List<SectionLine> Secs(List<Tin> surfs) =>
            Sections.CrossSections(_align, surfs, (double)_interval.Value, (double)_left.Value, (double)_right.Value, (double)_step.Value);

        private void CrossSections()
        {
            try
            {
                if (!Ready(out var surfs, out var names)) return;
                var at = PickPoint("\nCross sections insertion point (upper-left): ");
                if (at == null) return;
                Cursor = Cursors.WaitCursor;
                var secs = Secs(surfs);
                int n = CadDraw.CrossSections(at.Value, secs, names, Math.Max(1, (double)_vex.Value / 5), (double)_start.Value, Settings.Current.TextHeight, 4, PointDrawer.Layer("CROSS_SECTIONS"));
                _info.Text = string.Format(Lang.T("CrossDone"), n);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void Volumes()
        {
            try
            {
                if (!Ready(out var surfs, out var names)) return;
                if (surfs.Count < 2) { UiKit.Error(Lang.T("NeedTwoSurfaces")); return; }
                var path = UiKit.BrowseSave(this, "PDF|*.pdf", "Sections_Volumes_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".pdf", out _);
                if (path == null) return;
                Cursor = Cursors.WaitCursor;
                var secs = Secs(surfs);
                var v = Sections.EndArea(secs);
                double st0 = (double)_start.Value;
                var d = ReportFactory.SectionVolumes(v, _align, st0, (double)_interval.Value, (double)_left.Value, (double)_right.Value, secs.Count);
                double cc = 0, cf = 0;
                var csv = new StringBuilder("Chainage,Cut area (m2),Fill area (m2),Cut volume (m3),Fill volume (m3),Cumulative cut (m3),Cumulative fill (m3)\n");
                foreach (var r in v.Rows)
                {
                    cc += r.cutVol; cf += r.fillVol;
                    csv.AppendLine(string.Join(",", Geo2D.FormatChainage(r.ch, st0), r.cutArea.ToString("F2", Inv), r.fillArea.ToString("F2", Inv), r.cutVol.ToString("F2", Inv), r.fillVol.ToString("F2", Inv), cc.ToString("F2", Inv), cf.ToString("F2", Inv)));
                }
                ReportBuilder.SavePdf(d, path);
                File.WriteAllText(Path.ChangeExtension(path, ".csv"), csv.ToString(), new UTF8Encoding(true));
                ExcelExport.SectionVolumes(Path.ChangeExtension(path, ".xlsx"), v, st0);
                _info.Text = Lang.T("Cut") + " " + v.TotalCut.ToString("N2") + " m³   ·   " + Lang.T("Fill") + " " + v.TotalFill.ToString("N2") + " m³";
                if (UiKit.Ask(Lang.T("ReportSavedOpen") + "\n" + path)) UiKit.OpenUrl(path);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }
    }
}
