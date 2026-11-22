using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SurveyX.Core;

namespace SurveyX.Cad
{
    /// <summary>Creates and reads SurveyX point blocks (cross + circle with PN / ELEV / DESC attributes).</summary>
    public static class PointDrawer
    {
        public const string BlockName = "SX_POINT";
        public const string TextStyleName = "SX_TEXT";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Layer(string suffix) => (Settings.Current.LayerPrefix ?? "SX_") + suffix;

        public static ObjectId EnsureLayer(Transaction tr, Database db, string name, short aci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        public static ObjectId EnsureTextStyle(Transaction tr, Database db)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(TextStyleName)) return tst[TextStyleName];
            tst.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = TextStyleName, FileName = "arial.ttf", TextSize = 0 };
            var id = tst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        public static void EnsureStandardLayers(Transaction tr, Database db)
        {
            EnsureLayer(tr, db, Layer("POINTS"), 1);
            EnsureLayer(tr, db, Layer("PNUM"), 7);
            EnsureLayer(tr, db, Layer("ELEV"), 3);
            EnsureLayer(tr, db, Layer("DESC"), 4);
        }

        public static ObjectId EnsureBlock(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockName)) return bt[BlockName];
            EnsureStandardLayers(tr, db);
            var style = EnsureTextStyle(tr, db);
            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = BlockName, Origin = Point3d.Origin, Comments = "SurveyX CAD survey point" };
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            Entity[] geom =
            {
                new Line(new Point3d(-0.5, 0, 0), new Point3d(0.5, 0, 0)),
                new Line(new Point3d(0, -0.5, 0), new Point3d(0, 0.5, 0)),
                new Circle(Point3d.Origin, Vector3d.ZAxis, 0.25),
            };
            foreach (var e in geom)
            {
                e.Layer = "0";
                e.ColorIndex = 0; // ByBlock
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }

            AddAttDef(tr, btr, "PN", "Point name", new Point3d(0.6, 0.2, 0), Layer("PNUM"), style);
            AddAttDef(tr, btr, "ELEV", "Elevation", new Point3d(0.6, -0.9, 0), Layer("ELEV"), style);
            AddAttDef(tr, btr, "DESC", "Description", new Point3d(0.6, -2.0, 0), Layer("DESC"), style);
            return id;
        }

        private static void AddAttDef(Transaction tr, BlockTableRecord btr, string tag, string prompt, Point3d pos, string layer, ObjectId style)
        {
            var ad = new AttributeDefinition(pos, "", tag, prompt, style)
            {
                Height = 0.7,
                Layer = layer,
                ColorIndex = 256, // ByLayer
                LockPositionInBlock = false,
            };
            btr.AppendEntity(ad);
            tr.AddNewlyCreatedDBObject(ad, true);
        }

        /// <summary>Inserts points into model space. Returns number inserted.</summary>
        public static int InsertPoints(Database db, IList<SurveyPoint> pts, Action<int> progress = null)
        {
            var st = Settings.Current;
            int count = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockId = EnsureBlock(tr, db);
                var pointLayer = EnsureLayer(tr, db, Layer("POINTS"), 1);
                var btrDef = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                var attDefs = new List<AttributeDefinition>();
                foreach (ObjectId oid in btrDef)
                    if (tr.GetObject(oid, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant) attDefs.Add(ad);

                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                string fmt = "F" + st.Decimals;
                foreach (var p in pts)
                {
                    double z = st.Place3D && p.HasZ ? p.Z : 0;
                    var br = new BlockReference(new Point3d(p.E, p.N, z), blockId)
                    {
                        ScaleFactors = new Scale3d(st.TextHeight / 0.7),
                        LayerId = pointLayer,
                    };
                    ms.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    foreach (var ad in attDefs)
                    {
                        var ar = new AttributeReference();
                        ar.SetAttributeFromBlock(ad, br.BlockTransform);
                        switch (ad.Tag)
                        {
                            case "PN": ar.TextString = p.Name ?? ""; ar.Invisible = !st.ShowName; break;
                            case "ELEV": ar.TextString = p.HasZ ? p.Z.ToString(fmt, Inv) : ""; ar.Invisible = !st.ShowElevation; break;
                            case "DESC": ar.TextString = p.Desc ?? ""; ar.Invisible = !st.ShowDesc; break;
                        }
                        br.AttributeCollection.AppendAttribute(ar);
                        tr.AddNewlyCreatedDBObject(ar, true);
                    }
                    count++;
                    if (progress != null && count % 200 == 0) progress(count);
                }
                tr.Commit();
            }
            return count;
        }

        private static readonly string[] NameTags = { "PN", "POINT", "NAME", "NO", "ID", "PT", "NUMBER" };
        private static readonly string[] ElevTags = { "ELEV", "Z", "LEVEL", "RL", "ELEVATION", "HEIGHT", "H" };
        private static readonly string[] DescTags = { "DESC", "CODE", "DESCRIPTION", "REMARK", "D" };

        /// <summary>Reads survey points from block references (any block — attributes PN/ELEV/DESC recognised) and AutoCAD POINT entities.</summary>
        public static List<SurveyPoint> ReadPoints(Transaction tr, IEnumerable<ObjectId> ids, List<ObjectId> outIds = null)
        {
            var list = new List<SurveyPoint>();
            int auto = 1;
            foreach (var id in ids)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent is BlockReference br)
                {
                    var p = new SurveyPoint { E = br.Position.X, N = br.Position.Y, Z = br.Position.Z, HasZ = true };
                    string name = null, elev = null, desc = null;
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        if (!(tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)) continue;
                        string tag = ar.Tag.ToUpperInvariant();
                        if (name == null && Array.IndexOf(NameTags, tag) >= 0) name = ar.TextString;
                        else if (elev == null && Array.IndexOf(ElevTags, tag) >= 0) elev = ar.TextString;
                        else if (desc == null && Array.IndexOf(DescTags, tag) >= 0) desc = ar.TextString;
                    }
                    if (elev != null && PointFileReader.TryNum(elev, out double z) && Math.Abs(br.Position.Z) < 1e-9) p.Z = z;
                    p.Name = string.IsNullOrWhiteSpace(name) ? (auto).ToString(Inv) : name;
                    p.Desc = desc ?? "";
                    auto++;
                    list.Add(p);
                    outIds?.Add(id);
                }
                else if (ent is DBPoint dp)
                {
                    list.Add(new SurveyPoint { E = dp.Position.X, N = dp.Position.Y, Z = dp.Position.Z, Name = (auto++).ToString(Inv) });
                    outIds?.Add(id);
                }
            }
            return list;
        }

        public static SelectionFilter PointFilter() =>
            new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT,POINT") });

        /// <summary>Moves a block reference / DBPoint to new XY, keeping Z and attribute offsets.</summary>
        public static void MoveTo(Transaction tr, ObjectId id, double e, double n)
        {
            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            Point3d from = ent is BlockReference br ? br.Position : ((DBPoint)ent).Position;
            var to = new Point3d(e, n, from.Z);
            ent.TransformBy(Matrix3d.Displacement(to - from));
        }
    }
}
