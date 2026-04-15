using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutocadAI.Actions;

public static class AcadActions
{
    public static string DrawSquareInteractive(Document doc, double? side = null, double defaultSide = 12.0)
    {
        var ed = doc.Editor;

        var s = side;
        if (s == null || s <= 0)
        {
            var pdrSide = ed.GetDouble(new PromptDoubleOptions("\nLongueur du côté (ex: 12): ")
            {
                DefaultValue = defaultSide,
                AllowNegative = false,
                AllowZero = false,
                UseDefaultValue = true
            });
            if (pdrSide.Status != PromptStatus.OK)
                return "Annulé.";
            s = pdrSide.Value;
        }

        var ppr = ed.GetPoint("\nPoint d’insertion (coin bas-gauche): ");
        if (ppr.Status != PromptStatus.OK)
            return "Annulé.";

        var p0 = ppr.Value;
        var p1 = new Point2d(p0.X + s.Value, p0.Y);
        var p2 = new Point2d(p0.X + s.Value, p0.Y + s.Value);
        var p3 = new Point2d(p0.X, p0.Y + s.Value);

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(p0.X, p0.Y), 0, 0, 0);
            pl.AddVertexAt(1, p1, 0, 0, 0);
            pl.AddVertexAt(2, p2, 0, 0, 0);
            pl.AddVertexAt(3, p3, 0, 0, 0);
            pl.Closed = true;

            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
        }

        return $"Carré {s.Value.ToString(CultureInfo.InvariantCulture)}x{s.Value.ToString(CultureInfo.InvariantCulture)} créé.";
    }

    public static string DrawRectangleInteractive(Document doc, double? width = null, double? height = null, double defaultWidth = 12.0, double defaultHeight = 12.0)
    {
        var ed = doc.Editor;

        var w = width;
        var h = height;
        if (w == null || w <= 0)
        {
            var pdr = ed.GetDouble(new PromptDoubleOptions("\nLargeur (X) (ex: 16): ")
            {
                DefaultValue = defaultWidth,
                AllowNegative = false,
                AllowZero = false,
                UseDefaultValue = true
            });
            if (pdr.Status != PromptStatus.OK) return "Annulé.";
            w = pdr.Value;
        }
        if (h == null || h <= 0)
        {
            var pdr = ed.GetDouble(new PromptDoubleOptions("\nHauteur (Y) (ex: 14): ")
            {
                DefaultValue = defaultHeight,
                AllowNegative = false,
                AllowZero = false,
                UseDefaultValue = true
            });
            if (pdr.Status != PromptStatus.OK) return "Annulé.";
            h = pdr.Value;
        }

        var ppr = ed.GetPoint("\nPoint d’insertion (coin bas-gauche): ");
        if (ppr.Status != PromptStatus.OK)
            return "Annulé.";

        var p0 = ppr.Value;
        var p1 = new Point2d(p0.X + w.Value, p0.Y);
        var p2 = new Point2d(p0.X + w.Value, p0.Y + h.Value);
        var p3 = new Point2d(p0.X, p0.Y + h.Value);

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(p0.X, p0.Y), 0, 0, 0);
            pl.AddVertexAt(1, p1, 0, 0, 0);
            pl.AddVertexAt(2, p2, 0, 0, 0);
            pl.AddVertexAt(3, p3, 0, 0, 0);
            pl.Closed = true;

            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
        }

        return $"Rectangle {w.Value.ToString(CultureInfo.InvariantCulture)}x{h.Value.ToString(CultureInfo.InvariantCulture)} créé.";
    }

    public static string DrawCircleInteractive(Document doc, double? radius = null, double? diameter = null, double defaultDiameter = 24.0)
    {
        var ed = doc.Editor;

        var r = radius;
        if ((r == null || r <= 0) && diameter != null && diameter > 0)
            r = diameter.Value / 2.0;

        if (r == null || r <= 0)
        {
            var pdr = ed.GetDouble(new PromptDoubleOptions("\nDiamètre (ex: 24): ")
            {
                DefaultValue = defaultDiameter,
                AllowNegative = false,
                AllowZero = false,
                UseDefaultValue = true
            });
            if (pdr.Status != PromptStatus.OK) return "Annulé.";
            r = pdr.Value / 2.0;
        }

        var ppr = ed.GetPoint("\nCentre du cercle: ");
        if (ppr.Status != PromptStatus.OK)
            return "Annulé.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

            var c = new Circle(ppr.Value, Vector3d.ZAxis, r.Value);
            btr.AppendEntity(c);
            tr.AddNewlyCreatedDBObject(c, true);
            tr.Commit();
        }

        var dOut = (r.Value * 2.0).ToString(CultureInfo.InvariantCulture);
        return $"Cercle Ø{dOut} créé.";
    }

    public static string ReadTextInteractive(Document doc)
    {
        var ed = doc.Editor;

        var peo = new PromptEntityOptions("\nSélectionne un MTEXT ou TEXT: ");
        peo.SetRejectMessage("\nObjet non supporté. Sélectionne MTEXT ou TEXT.");
        peo.AddAllowedClass(typeof(MText), exactMatch: false);
        peo.AddAllowedClass(typeof(DBText), exactMatch: false);

        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK)
            return "Annulé.";

        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var obj = tr.GetObject(per.ObjectId, OpenMode.ForRead);
            string text;
            if (obj is MText mt)
                text = mt.Contents ?? "";
            else if (obj is DBText dt)
                text = dt.TextString ?? "";
            else
                text = "";

            tr.Commit();
            return "Texte sélectionné = " + text;
        }
    }

    public static string? ReadSingleTextIfUnambiguous(Document doc)
    {
        using (doc.LockDocument())
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);

            ObjectId? only = null;
            var count = 0;

            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is MText || obj is DBText)
                {
                    count++;
                    if (count > 1) break;
                    only = id;
                }
            }

            if (count == 0)
                return "Aucun MTEXT/TEXT trouvé dans l’espace courant.";
            if (count > 1 || only == null)
                return null; // plusieurs textes : appelant doit demander à l’utilisateur lequel

            var oneObj = tr.GetObject(only.Value, OpenMode.ForRead);
            var text = oneObj is MText mt ? (mt.Contents ?? "") :
                       oneObj is DBText dt ? (dt.TextString ?? "") : "";

            tr.Commit();
            return "Texte (unique) = " + text;
        }
    }
}

