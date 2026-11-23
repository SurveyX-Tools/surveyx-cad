using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using SurveyX.Cad;
using SurveyX.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SurveyX.UI
{
    /// <summary>Primavera P6: import schedule, link activities to drawing objects, measure progress, colour the drawing, export back to P6.</summary>
    public sealed class PrimaveraForm : SxForm
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private P6Schedule _s;
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _head = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 8, 8, 0), BackColor = Soft, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        private readonly FlowLayoutPanel _acts = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        private readonly NumericUpDown _pct = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100, Width = 70 };

        private static readonly string[] Cols = { "P6Id", "P6Name", "P6Wbs", "P6Start", "P6Finish", "P6Pct", "PlannedQty", "ExecutedQty", "Unit", "DrawingPct", "Linked", "Status" };

        public PrimaveraForm() : base("P6Title", "HelpP6")
        {
            Size = new Size(1180, 760);
            MinimumSize = new Size(960, 600);
            SetupGrid();

            var bImport = UiKit.Btn("P6Import", true); bImport.Click += (s, e) => Import();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            top.Controls.Add(bImport);
            top.Controls.Add(new Label { Text = Lang.T("P6ImportHint"), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(6, 10, 3, 3) });

            var bLink = UiKit.Btn("P6Link"); bLink.Click += (s, e) => Link();
            var bUnlink = UiKit.Btn("P6Unlink"); bUnlink.Click += (s, e) => Unlink();
            var bPct = UiKit.Btn("P6SetPct"); bPct.Click += (s, e) => SetPct();
            var bZoom = UiKit.Btn("P6Zoom"); bZoom.Click += (s, e) => Zoom();
            var bColor = UiKit.Btn("P6Colour", true); bColor.Click += (s, e) => Colour();
            var bClear = UiKit.Btn("P6ClearColour"); bClear.Click += (s, e) => { CadDraw.ClearLayer(P6Store.StatusLayer); };
            var bXls = UiKit.Btn("P6ExportExcel", true); bXls.Click += (s, e) => ExportExcel();
            var bPdf = UiKit.Btn("PdfReport"); bPdf.Click += (s, e) => Pdf();
            _acts.Controls.AddRange(new Control[] { bLink, bUnlink, UiKit.Lbl("Percent"), _pct, bPct, bZoom, bColor, bClear, bXls, bPdf });

            var legend = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 2, 8, 2), BackColor = Soft };
            foreach (ActivityState st in Enum.GetValues(typeof(ActivityState)))
            {
                var c = P6Store.StateColor(st);
                legend.Controls.Add(new Label { BackColor = Color.FromArgb(c.r, c.g, c.b), Width = 18, Height = 14, Margin = new Padding(10, 8, 2, 2) });
                legend.Controls.Add(new Label { Text = StateText(st), AutoSize = true, Margin = new Padding(2, 6, 10, 2) });
            }

            var close = UiKit.Btn("Close"); close.Click += (s, e) => { SaveEdits(); Close(); }; CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(_head);
            Controls.Add(top);
            Controls.Add(Header("P6Title"));
            Controls.Add(_acts);
            Controls.Add(legend);
            Controls.Add(UiKit.ButtonBar(close));

            _s = UiKit.TryLoadSchedule();
            Refresh2();
        }

        public static string StateText(ActivityState s)
        {
            switch (s)
            {
                case ActivityState.Completed: return Lang.T("StCompleted");
                case ActivityState.InProgress: return Lang.T("StInProgress");
                case ActivityState.Late: return Lang.T("StLate");
                default: return Lang.T("StNotStarted");
            }
        }

        private void SetupGrid()
        {
            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false; _grid.BackgroundColor = Color.White;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; _grid.VirtualMode = true;
            float[] w = { 1.1f, 3f, 1.6f, 1.1f, 1.1f, 0.8f, 1f, 1f, 0.7f, 0.9f, 0.8f, 1.1f };
            for (int i = 0; i < Cols.Length; i++)
            {
                bool editable = Cols[i] == "PlannedQty" || Cols[i] == "ExecutedQty" || Cols[i] == "Unit";
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Lang.T(Cols[i]), FillWeight = w[i] * 100, ReadOnly = !editable, SortMode = DataGridViewColumnSortMode.NotSortable });
                if (editable) _grid.Columns[i].DefaultCellStyle.BackColor = Color.FromArgb(0xFF, 0xFD, 0xE7);
            }
            _grid.CellValueNeeded += (s, e) =>
            {
                if (_s == null || e.RowIndex >= _s.Activities.Count) return;
                var a = _s.Activities[e.RowIndex];
                switch (Cols[e.ColumnIndex])
                {
                    case "P6Id": e.Value = a.Id; break;
                    case "P6Name": e.Value = a.Name; break;
                    case "P6Wbs": e.Value = a.Wbs; break;
                    case "P6Start": e.Value = a.Start?.ToString("yyyy-MM-dd", Inv); break;
                    case "P6Finish": e.Value = a.Finish?.ToString("yyyy-MM-dd", Inv); break;
                    case "P6Pct": e.Value = a.P6Percent.ToString("0.#", Inv); break;
                    case "PlannedQty": e.Value = a.PlannedQty > 0 ? a.PlannedQty.ToString("0.###", Inv) : ""; break;
                    case "ExecutedQty": e.Value = a.ExecutedQty > 0 ? a.ExecutedQty.ToString("0.###", Inv) : ""; break;
                    case "Unit": e.Value = a.Unit; break;
                    case "DrawingPct": e.Value = a.DrawingPercent >= 0 ? a.DrawingPercent.ToString("0.#", Inv) : "—"; break;
                    case "Linked": e.Value = a.LinkedObjects; break;
                    case "Status": e.Value = StateText(a.State(_s.DataDate)); break;
                }
            };
            _grid.CellValuePushed += (s, e) =>
            {
                if (_s == null || e.RowIndex >= _s.Activities.Count) return;
                var a = _s.Activities[e.RowIndex];
                string v = Convert.ToString(e.Value, Inv) ?? "";
                switch (Cols[e.ColumnIndex])
                {
                    case "PlannedQty": a.PlannedQty = PointFileReader.TryNum(v, out double p) ? Math.Max(0, p) : 0; break;
                    case "ExecutedQty": a.ExecutedQty = PointFileReader.TryNum(v, out double q) ? Math.Max(0, q) : 0; break;
                    case "Unit": a.Unit = v.Trim(); break;
                }
                P6Store.Measure(_s);
                P6Store.Save(_s);
                _grid.InvalidateRow(e.RowIndex);
            };
            _grid.CellFormatting += (s, e) =>
            {
                if (_s == null || e.RowIndex >= _s.Activities.Count || Cols[e.ColumnIndex] != "Status") return;
                var c = P6Store.StateColor(_s.Activities[e.RowIndex].State(_s.DataDate));
                e.CellStyle.BackColor = Color.FromArgb(c.r, c.g, c.b);
                e.CellStyle.ForeColor = Color.White;
            };
        }

        private void Refresh2()
        {
            bool has = _s != null;
            foreach (Control c in _acts.Controls) c.Enabled = has;
            if (!has) { _head.Text = Lang.T("P6NoScheduleYet"); _grid.RowCount = 0; return; }
            try { P6Store.Measure(_s); } catch (System.IO.FileNotFoundException) { /* outside AutoCAD (preview tests) */ }
            _head.Text = string.Format(Lang.T("P6Header"), _s.ProjectName, _s.SourceFile, _s.DataDate?.ToString("yyyy-MM-dd", Inv) ?? "—",
                _s.Activities.Count, _s.Activities.Count(a => a.LinkedObjects > 0));
            _grid.RowCount = 0;
            _grid.RowCount = _s.Activities.Count;
        }

        private P6Activity Current => _s != null && _grid.CurrentRow != null && _grid.CurrentRow.Index < _s.Activities.Count ? _s.Activities[_grid.CurrentRow.Index] : null;

        private void SaveEdits()
        {
            if (_s == null) return;
            _grid.EndEdit();
            P6Store.Save(_s);
        }

        private void Import()
        {
            var f = UiKit.BrowseOpen(this, PrimaveraReader.OpenFilter);
            if (f == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var s = PrimaveraReader.Read(f);
                s.CarryQuantitiesFrom(_s);
                _s = s;
                P6Store.Save(_s);
                Refresh2();
            }
            catch (Exception ex) { UiKit.Error(Lang.T("ReadError") + "\n" + ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private ObjectIdArray PickObjects(string msg)
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            using (ed.StartUserInteraction(this))
            {
                var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = msg });
                return r.Status == PromptStatus.OK ? new ObjectIdArray(r.Value.GetObjectIds()) : null;
            }
        }

        private sealed class ObjectIdArray
        {
            public readonly Autodesk.AutoCAD.DatabaseServices.ObjectId[] Ids;
            public ObjectIdArray(Autodesk.AutoCAD.DatabaseServices.ObjectId[] ids) { Ids = ids; }
        }

        private void Link()
        {
            var a = Current;
            if (a == null) { UiKit.Error(Lang.T("P6ChooseActivity")); return; }
            var sel = PickObjects("\nSelect objects for activity " + a.Id + " (areas, lines, blocks...): ");
            if (sel == null) return;
            int n = P6Store.Link(sel.Ids, a.Id, 0);
            Refresh2();
            UiKit.Info(string.Format(Lang.T("P6Linked"), n, a.Id));
        }

        private void Unlink()
        {
            var sel = PickObjects("\nSelect objects to unlink: ");
            if (sel == null) return;
            int n = P6Store.Unlink(sel.Ids);
            Refresh2();
            UiKit.Info(string.Format(Lang.T("P6Unlinked"), n));
        }

        private void SetPct()
        {
            var sel = PickObjects("\nSelect linked objects to set progress " + _pct.Value + "%: ");
            if (sel == null) return;
            int n = P6Store.SetPercent(sel.Ids, (double)_pct.Value);
            Refresh2();
            UiKit.Info(string.Format(Lang.T("P6PctSet"), n, _pct.Value));
        }

        private void Zoom()
        {
            var a = Current;
            if (a == null) return;
            var ext = P6Store.ExtentsOf(a.Id);
            if (ext == null) { UiKit.Info(Lang.T("P6NoLinks")); return; }
            CadView.ZoomTo(ext.Value);
        }

        private void Colour()
        {
            SaveEdits();
            P6Store.Measure(_s);
            int n = P6Store.DrawOverlay(_s, Settings.Current.TextHeight);
            UiKit.Info(string.Format(Lang.T("P6Coloured"), n, P6Store.StatusLayer));
        }

        private void ExportExcel()
        {
            SaveEdits();
            P6Store.Measure(_s);
            var path = UiKit.BrowseSave(this, "Excel|*.xlsx", "P6_Progress_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".xlsx", out _);
            if (path == null) return;
            try
            {
                var acts = _s.Activities.Where(a => a.DrawingPercent >= 0).ToList();
                if (acts.Count == 0) { UiKit.Info(Lang.T("P6NothingMeasured")); return; }
                PrimaveraWriter.WriteP6Excel(path, acts);
                PrimaveraWriter.WriteReportCsv(Path.ChangeExtension(path, ".csv"), _s, _s.Activities);
                UiKit.Info(string.Format(Lang.T("P6Exported"), acts.Count) + "\n" + path + "\n\n" + Lang.T("P6ImportSteps"));
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
        }

        private void Pdf()
        {
            SaveEdits();
            P6Store.Measure(_s);
            var path = UiKit.BrowseSave(this, "PDF|*.pdf", "Progress_" + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".pdf", out _);
            if (path == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var st = Settings.Current;
                var d = ReportFactory.Progress(_s);
                ReportBuilder.SavePdf(d, path);
                if (UiKit.Ask(Lang.T("ReportSavedOpen") + "\n" + path)) UiKit.OpenUrl(path);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }
    }
}
