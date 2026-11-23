using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SurveyX.Core;

namespace SurveyX.Cad
{
    /// <summary>Tool definitions and system prompt for the assistant (no AutoCAD types, so they can be validated outside AutoCAD).</summary>
    public static class AssistantSpecs
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string OutputFolder
        {
            get
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SurveyX");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        // ================================================================== system prompt
        public static string SystemPrompt()
        {
            var st = Settings.Current;
            return
"You are SurveyX Assistant, a senior land-surveying and technical-office engineer working INSIDE AutoCAD / Civil 3D for " +
(string.IsNullOrEmpty(st.CompanyName) ? "the user" : st.CompanyName) + ". Project: " + (string.IsNullOrEmpty(st.ProjectName) ? "(not set)" : st.ProjectName) + ".\n" +
"You act through the provided tools: survey points, coordinate conversion (UTM, DLTM, Nahrwan, Egyptian belts), daily cut & fill against the OGL with PDF/Excel reports, " +
"level checks against design, TIN surfaces/contours/elevation maps, long & cross sections and volumes, setting-out tables, quantity take-off, cumulative quantities, " +
"general CAD drawing and Primavera P6 progress (schedule imported from XER/XML, quantities, colouring, Excel back to P6).\n" +
"Rules:\n" +
"- Reply in the user's language and dialect (Egyptian / Gulf Arabic or English). Be short, clear and professional; give the key numbers with units (m, m², m³).\n" +
"- Use tools to do the work; never invent results, file names or quantities. If a needed input is missing (which file, which area, which activity), ask one short question or use list_files / get_drawing_info to find it.\n" +
"- The saved OGL file can be used with {\"saved_ogl\": true}. Files may be given by full path or by name in the SurveyX folder: " + OutputFolder + ".\n" +
"- When objects must be chosen in the drawing, call select_objects (the user will select) and reuse the returned selection_id.\n" +
"- Before deleting or moving existing drawing objects, or running a raw AutoCAD command that changes many objects, say what you will do and ask for confirmation. Creating new objects/reports needs no confirmation. Everything you draw can be undone with UNDO.\n" +
"- After a task, summarise: what was done, the main results, and where reports were saved.\n" +
"- Units are metres. Positive difference = fill / above design, negative = cut / below design.\n" +
"Today is " + DateTime.Today.ToString("yyyy-MM-dd", Inv) + ".";
        }

        // ================================================================== tool specs
        private static Dictionary<string, object> SourceSchema(string what) => new Dictionary<string, object>
        {
            { "type", "object" },
            { "description", what + ": one of {\"file\": path}, {\"selection_id\": id from select_objects}, {\"saved_ogl\": true} (the remembered OGL file)." },
            { "properties", new Dictionary<string, object>
                {
                    { "file", new Dictionary<string, object> { { "type", "string" } } },
                    { "selection_id", new Dictionary<string, object> { { "type", "string" } } },
                    { "saved_ogl", new Dictionary<string, object> { { "type", "boolean" } } },
                } },
        };

        private static Dictionary<string, object> PointSchema(string what) => new Dictionary<string, object>
        {
            { "type", "object" }, { "description", what + " {x, y}. Omit to let the user pick it in the drawing." },
            { "properties", new Dictionary<string, object> { { "x", new Dictionary<string, object> { { "type", "number" } } }, { "y", new Dictionary<string, object> { { "type", "number" } } } } },
        };

        private static ToolSpec Src(this ToolSpec t, string name, string what, bool required = false)
        {
            t.Properties[name] = SourceSchema(what);
            if (required) t.Required.Add(name);
            return t;
        }

        private static ToolSpec At(this ToolSpec t, string name, string what)
        {
            t.Properties[name] = PointSchema(what);
            return t;
        }

        public static List<ToolSpec> Specs()
        {
            var str = new Dictionary<string, object> { { "type", "string" } };
            var crsIds = CrsCatalog.All.Select(c => (object)c.Id).ToList();
            return new List<ToolSpec>
            {
                new ToolSpec("get_drawing_info", "Summary of the open drawing: file, units, extents, layers with object counts, SurveyX settings (company, project, saved OGL file), and whether a Primavera schedule is stored."),
                new ToolSpec("list_files", "List files in a folder (default: the SurveyX folder and the last used folder) to find survey / OGL / design / schedule files.")
                    .Prop("folder", "string", "Folder path (optional).").Prop("pattern", "string", "Wildcard like *.csv (optional)."),
                new ToolSpec("read_points", "Read a points file or a drawing selection and return count, bounds, elevation range, detected columns and the first rows. Use before importing an unknown file.")
                    .Src("source", "Points", true),
                new ToolSpec("select_objects", "Ask the user to select objects in the drawing. Returns a selection_id to reuse in other tools.")
                    .Prop("prompt", "string", "Short instruction shown on the command line (English).", true)
                    .Prop("kind", "string", "What to allow.", false, new List<object> { "points", "curves", "closed", "any" })
                    .Prop("single", "boolean", "Pick one object only."),
                new ToolSpec("select_by_layer", "Select all model-space objects on the given layers (wildcards allowed) without asking the user. Returns selection_id.")
                    .Prop("layers", "array", "Layer names / patterns.", true, null, str),
                new ToolSpec("pick_point", "Ask the user to pick a point in the drawing. Returns x, y, z.").Prop("prompt", "string", "Instruction (English).", true),
                new ToolSpec("import_points", "Import and draw survey points from a file (CSV, TXT, Excel, LandXML, KML, GeoJSON), optionally converting coordinates.")
                    .Prop("file", "string", "Points file.", true)
                    .Prop("file_crs", "string", "Coordinate system of the file (default: last used).", false, crsIds)
                    .Prop("convert_to", "string", "Convert to this system before drawing (optional).", false, crsIds)
                    .Prop("skip_duplicates", "boolean", "Skip duplicate points (default true)."),
                new ToolSpec("convert_coordinates", "Convert coordinates between systems. Points use x = Easting/Longitude, y = Northing/Latitude.")
                    .Prop("from", "string", "Source system.", true, crsIds).Prop("to", "string", "Target system.", true, crsIds)
                    .Prop("points", "array", "Points [{name, x, y}].", true, null, new Dictionary<string, object> { { "type", "object" } }),
                new ToolSpec("cut_fill", "Daily cut & fill: compare a new survey with the OGL (original ground). Computes volumes, saves PDF + Excel reports with the company logo, logs the result for cumulative reports, can draw a colour map / point differences and add the volume to a Primavera activity.")
                    .Src("ogl", "OGL points (usually {\"saved_ogl\": true})", true).Src("survey", "New survey points", true)
                    .Prop("boundary_selection_id", "string", "Closed polyline selection limiting the area (optional).")
                    .Prop("area_name", "string", "Area / layer name used in the report and the cumulative log.")
                    .Prop("date", "string", "Report date yyyy-MM-dd (default today).")
                    .Prop("cell", "number", "Grid cell in m (0 = automatic).")
                    .Prop("draw_map", "boolean", "Draw the cut/fill colour map in the drawing.")
                    .Prop("draw_labels", "boolean", "Draw +/- difference labels at the survey points.")
                    .Prop("pdf", "boolean", "Save PDF report (default true).").Prop("excel", "boolean", "Save Excel (default true).")
                    .Prop("add_to_activity", "object", "{activity_id, which: \"cut\"|\"fill\"} adds the volume to the activity's executed quantity."),
                new ToolSpec("cumulative_report", "Cumulative cut & fill totals from all previous daily calculations (optionally one area / date range). Saves PDF + Excel.")
                    .Prop("area_name", "string", "Area (optional).").Prop("from", "string", "yyyy-MM-dd (optional).").Prop("to", "string", "yyyy-MM-dd (optional)."),
                new ToolSpec("level_check", "Check as-built levels against design levels with tolerances (kerb, asphalt, base, subgrade...). Saves PDF + Excel and can draw green/red labels.")
                    .Src("as_built", "As-built points", true).Src("design", "Design points / 3D polylines / feature lines", true)
                    .Prop("mode", "string", "How to get the design level.", false, new List<object> { "surface", "same_name", "nearest" })
                    .Prop("tol_plus", "number", "Tolerance above, m (e.g. 0.010).").Prop("tol_minus", "number", "Tolerance below, m.")
                    .Prop("radius", "number", "Search radius for nearest mode, m.").Prop("element", "string", "Element name for the report (e.g. Kerb top).")
                    .Prop("draw", "boolean", "Draw results in the drawing."),
                new ToolSpec("build_surface", "Build a TIN surface from points / 3D lines: 3D faces, Civil 3D TIN surface, COGO points, contours, elevation colour map, PDF surface report.")
                    .Src("points", "Surface points", true)
                    .Prop("max_edge", "number", "Max triangle edge m (default 50).").Prop("faces", "boolean", "Draw 3D faces.")
                    .Prop("civil_surface_name", "string", "Create a Civil 3D TIN surface with this name (Civil 3D only).")
                    .Prop("cogo_points", "boolean", "Create Civil 3D COGO points.")
                    .Prop("contour_interval", "number", "Draw contours at this interval (m).").Prop("major_every", "integer", "Major contour every N (default 5).")
                    .Prop("elevation_map", "boolean", "Draw elevation colour map.").Prop("pdf", "boolean", "Save surface PDF report."),
                new ToolSpec("sections", "Long section / cross sections / end-area volumes along a centreline (polyline, line, Civil alignment or feature line).")
                    .Prop("alignment_selection_id", "string", "Selection containing the centreline.", true)
                    .Src("existing", "Existing ground points (often {\"saved_ogl\": true})", true).Src("design", "Design / new surface points (optional)")
                    .Prop("interval", "number", "Section interval m (default 20).").Prop("left", "number", "Left width m.").Prop("right", "number", "Right width m.")
                    .Prop("step", "number", "Offset step m.").Prop("start_chainage", "number", "Start chainage m.")
                    .Prop("long_section", "boolean", "Draw long section.").At("long_section_at", "Lower-left insertion point")
                    .Prop("cross_sections", "boolean", "Draw cross sections.").At("cross_sections_at", "Upper-left insertion point")
                    .Prop("volumes_report", "boolean", "Save end-area volumes PDF + Excel (needs design)."),
                new ToolSpec("setting_out_table", "Chainage / offset (+right, −left) of points relative to a centreline. Draws a table and saves Excel.")
                    .Prop("alignment_selection_id", "string", "Selection containing the centreline.", true)
                    .Src("points", "Points to set out", true).Prop("start_chainage", "number", "Start chainage m.")
                    .At("table_at", "Table insertion point").Prop("draw_table", "boolean", "Draw the table (default true)."),
                new ToolSpec("quantity_takeoff", "Quantity take-off per layer (lengths, areas of closed shapes, counts of lines, blocks by name, texts, points). Saves Excel; can draw a table.")
                    .Prop("layers", "array", "Layer names / patterns (optional = all).", false, null, str)
                    .Prop("selection_id", "string", "Use this selection instead of layers (optional).")
                    .Prop("draw_table", "boolean", "Draw the table in the drawing.").At("table_at", "Table insertion point"),
                new ToolSpec("coordinate_table", "Draw a coordinates table (point, E, N, Z, description) for points.")
                    .Src("points", "Points", true).At("table_at", "Table insertion point"),
                new ToolSpec("draw", "Draw entities. Each item: {type: line|polyline|circle|text|point, points:[[x,y,z?],...], center:[x,y], radius, text, height, rotation_deg, layer, color (ACI 1-255), closed}.")
                    .Prop("entities", "array", "Entities to draw.", true, null, new Dictionary<string, object> { { "type", "object" } }),
                new ToolSpec("layer", "Create or change a layer (colour, on/off, freeze, make current).")
                    .Prop("name", "string", "Layer name.", true).Prop("color", "integer", "ACI colour 1-255.")
                    .Prop("on", "boolean", "Turn on/off.").Prop("frozen", "boolean", "Freeze/thaw.").Prop("current", "boolean", "Make current."),
                new ToolSpec("zoom", "Zoom the view: to drawing extents, a selection, or a window.")
                    .Prop("selection_id", "string", "Zoom to this selection (optional).")
                    .Prop("window", "array", "[x1, y1, x2, y2] (optional).", false, null, new Dictionary<string, object> { { "type", "number" } }),
                new ToolSpec("run_command", "Run any AutoCAD / Civil 3D command line text (e.g. \"_.PURGE _A * _N\"). It runs after the current reply finishes. Use only when no dedicated tool exists; ask before destructive commands.")
                    .Prop("command", "string", "Command text exactly as typed on the command line.", true),
                new ToolSpec("set_report_info", "Change the company name, project name, prepared-by or submitted-to shown on PDF reports.")
                    .Prop("company", "string", "").Prop("project", "string", "").Prop("prepared_by", "string", "").Prop("submitted_to", "string", ""),
                new ToolSpec("p6_import", "Import a Primavera P6 schedule (XER or XML) into the drawing (keeps quantities already entered).")
                    .Prop("file", "string", "Schedule file.", true),
                new ToolSpec("p6_list", "List the activities of the stored schedule with dates, P6 %, planned / executed quantities, measured %, linked objects and status."),
                new ToolSpec("p6_update", "Update an activity's quantities: planned quantity, executed quantity (set or add) and unit. Progress % = executed / planned.")
                    .Prop("activity_id", "string", "Activity ID.", true).Prop("planned_qty", "number", "").Prop("executed_qty", "number", "Set executed quantity.")
                    .Prop("add_executed_qty", "number", "Add to executed quantity.").Prop("unit", "string", ""),
                new ToolSpec("p6_link", "Link drawing objects (selection) to an activity.")
                    .Prop("activity_id", "string", "Activity ID.", true).Prop("selection_id", "string", "Objects.", true),
                new ToolSpec("p6_set_object_percent", "Set progress % on linked objects (for activities measured by objects, not quantities).")
                    .Prop("selection_id", "string", "Linked objects.", true).Prop("percent", "number", "0-100.", true),
                new ToolSpec("p6_colour", "Colour the drawing by activity status (grey not started, orange in progress, green completed, red late)."),
                new ToolSpec("p6_export", "Export progress: Excel file that P6 can import (Physical % Complete) and a PDF progress report."),
            };
        }

    }
}
