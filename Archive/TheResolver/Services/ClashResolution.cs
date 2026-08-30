using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace TheResolver.Services
{
    public class ClashResolution
    {
        private const double ClearanceMM = 500;
        private const double OffsetHeightMM = 1000;
        private readonly Document _Doc;

        public ClashResolution(Document doc)
        {
            _Doc = doc;

        }
        public void ResolveClashes()
        {
            try
            {
                List<CableTray> trays = new FilteredElementCollector(_Doc)
                    .OfClass(typeof(CableTray))
                    .Cast<CableTray>()
                    .ToList();

                List<Element> obstacles = CollectObstacles(_Doc);

                using (Transaction tx =
                    new Transaction(_Doc, "Resolve Tray Clashes"))
                {
                    tx.Start();

                    foreach (CableTray tray in trays)
                    {
                        LocationCurve lc =
                            tray.Location as LocationCurve;

                        if (lc == null)
                            continue;

                        Line trayLine = lc.Curve as Line;

                        if (trayLine == null)
                            continue;

                        BoundingBoxXYZ trayBox =
                            tray.get_BoundingBox(null);

                        foreach (Element obstacle in obstacles)
                        {
                            if (obstacle.Id == tray.Id)
                                continue;

                            BoundingBoxXYZ obsBox =
                                obstacle.get_BoundingBox(null);

                            if (obsBox == null)
                                continue;

                            if (!BoxesIntersect(trayBox, obsBox))
                                continue;

                            XYZ clashPoint =
                                GetClashPoint(trayBox, obsBox);

                            CreateOffsetRoute(
                                _Doc,
                                tray,
                                trayLine,
                                clashPoint);

                            break;
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }        

            
        private List<Element> CollectObstacles(Document doc)
        {
            List<BuiltInCategory> cats =
                new List<BuiltInCategory>()
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_Conduit
                };

            List<Element> result =
                new List<Element>();

            foreach (BuiltInCategory bic in cats)
            {
                result.AddRange(
                    new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }

            return result;
        }

        private bool BoxesIntersect(
            BoundingBoxXYZ a,
            BoundingBoxXYZ b)
        {
            return
                a.Max.X >= b.Min.X &&
                a.Min.X <= b.Max.X &&
                a.Max.Y >= b.Min.Y &&
                a.Min.Y <= b.Max.Y &&
                a.Max.Z >= b.Min.Z &&
                a.Min.Z <= b.Max.Z;
        }

        private XYZ GetClashPoint(
            BoundingBoxXYZ tray,
            BoundingBoxXYZ obstacle)
        {
            double x =
                (Math.Max(tray.Min.X, obstacle.Min.X)
                 + Math.Min(tray.Max.X, obstacle.Max.X)) / 2;

            double y =
                (Math.Max(tray.Min.Y, obstacle.Min.Y)
                 + Math.Min(tray.Max.Y, obstacle.Max.Y)) / 2;

            double z =
                (Math.Max(tray.Min.Z, obstacle.Min.Z)
                 + Math.Min(tray.Max.Z, obstacle.Max.Z)) / 2;

            return new XYZ(x, y, z);
        }

        private void CreateOffsetRoute(
            Document doc,
            CableTray originalTray,
            Line trayLine,
            XYZ clashPoint)
        {
            double clearance =
                UnitUtils.ConvertToInternalUnits(
                    ClearanceMM,
                    UnitTypeId.Millimeters);

            double offsetHeight =
                UnitUtils.ConvertToInternalUnits(
                    OffsetHeightMM,
                    UnitTypeId.Millimeters);

            XYZ start =
                trayLine.GetEndPoint(0);

            XYZ end =
                trayLine.GetEndPoint(1);

            XYZ direction =
                (end - start).Normalize();

            XYZ break1 =
                clashPoint - direction * clearance;

            XYZ break2 =
                clashPoint + direction * clearance;

            XYZ elevated1 =
                break1 + XYZ.BasisZ * offsetHeight;

            XYZ elevated2 =
                break2 + XYZ.BasisZ * offsetHeight;

            ElementId trayType =
                originalTray.GetTypeId();

            ElementId levelId =
                originalTray.ReferenceLevel.Id;

            doc.Delete(originalTray.Id);

            CableTray s1 =
                CableTray.Create(
                    doc,
                    trayType,
                    start,
                    break1,
                    levelId);

            CableTray s2 =
                CableTray.Create(
                    doc,
                    trayType,
                    break1,
                    elevated1,
                    levelId);

            CableTray s3 =
                CableTray.Create(
                    doc,
                    trayType,
                    elevated1,
                    elevated2,
                    levelId);

            CableTray s4 =
                CableTray.Create(
                    doc,
                    trayType,
                    elevated2,
                    break2,
                    levelId);

            CableTray s5 =
                CableTray.Create(
                    doc,
                    trayType,
                    break2,
                    end,
                    levelId);

            CreateElbow(doc, s1, s2);
            CreateElbow(doc, s2, s3);
            CreateElbow(doc, s3, s4);
            CreateElbow(doc, s4, s5);
        }

        private void CreateElbow(
            Document doc,
            MEPCurve curve1,
            MEPCurve curve2)
        {
            Connector c1 =
                FindNearestConnector(curve1, curve2);

            Connector c2 =
                FindNearestConnector(curve2, curve1);

            if (c1 == null || c2 == null)
                return;

            try
            {
                doc.Create.NewElbowFitting(c1, c2);
            }
            catch
            {
            }
        }

        private Connector FindNearestConnector(
            MEPCurve source,
            MEPCurve target)
        {
            double min = double.MaxValue;

            Connector result = null;

            foreach (Connector c1 in
                source.ConnectorManager.Connectors)
            {
                foreach (Connector c2 in
                    target.ConnectorManager.Connectors)
                {
                    double d =
                        c1.Origin.DistanceTo(c2.Origin);

                    if (d < min)
                    {
                        min = d;
                        result = c1;
                    }
                }
            }

            return result;
        }
    }
}



#region
/*
namespace CableTrayClashResolver
{
    [Transaction(TransactionMode.Manual)]
    public class ResolveCableTrayClash : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                Reference reference =
                    uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        new CableTraySelectionFilter(),
                        "Select Cable Tray");

                CableTray tray =doc.GetElement(reference) as CableTray;

                if (tray == null)
                {
                    message = "Selected element is not a cable tray.";
                    return Result.Failed;
                }

                LocationCurve locationCurve =
                    tray.Location as LocationCurve;

                Line line =
                    locationCurve.Curve as Line;

                if (line == null)
                {
                    message = "Only straight cable trays are supported.";
                    return Result.Failed;
                }

                //--------------------------------------------------------
                // SAMPLE VALUES
                //--------------------------------------------------------
                double offsetHeight =
                    UnitUtils.ConvertToInternalUnits(
                        1000,
                        UnitTypeId.Millimeters);

                double offsetLength =
                    UnitUtils.ConvertToInternalUnits(
                        500,
                        UnitTypeId.Millimeters);

                //--------------------------------------------------------
                // ASSUME CLASH AT MIDPOINT
                //--------------------------------------------------------
                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);

                XYZ clashPoint =
                    (start + end) * 0.5;

                XYZ dir =
                    (end - start).Normalize();

                XYZ breakPt1 =
                    clashPoint - dir * offsetLength;

                XYZ breakPt2 =
                    clashPoint + dir * offsetLength;

                XYZ elevatedPt1 =
                    breakPt1 + XYZ.BasisZ * offsetHeight;

                XYZ elevatedPt2 =
                    breakPt2 + XYZ.BasisZ * offsetHeight;

                ElementId trayTypeId =
                    tray.GetTypeId();

                ElementId levelId =
                    tray.ReferenceLevel.Id;

                using (Transaction tx = new Transaction(doc,"Cable Tray Clash Resolution"))
                {
                    tx.Start();

                    //----------------------------------------------------
                    // DELETE ORIGINAL
                    //----------------------------------------------------
                    doc.Delete(tray.Id);

                    //----------------------------------------------------
                    // 1. LEFT SEGMENT
                    //----------------------------------------------------
                    CableTray tray1 =
                        CableTray.Create(
                            doc,
                            trayTypeId,
                            start,
                            breakPt1,
                            levelId);

                    //----------------------------------------------------
                    // 2. VERTICAL UP
                    //----------------------------------------------------
                    CableTray rise =
                        CableTray.Create(
                            doc,
                            trayTypeId,
                            breakPt1,
                            elevatedPt1,
                            levelId);

                    //----------------------------------------------------
                    // 3. TOP OFFSET
                    //----------------------------------------------------
                    CableTray top =
                        CableTray.Create(
                            doc,
                            trayTypeId,
                            elevatedPt1,
                            elevatedPt2,
                            levelId);

                    //----------------------------------------------------
                    // 4. VERTICAL DOWN
                    //----------------------------------------------------
                    CableTray drop =
                        CableTray.Create(
                            doc,
                            trayTypeId,
                            elevatedPt2,
                            breakPt2,
                            levelId);

                    //----------------------------------------------------
                    // 5. RIGHT SEGMENT
                    //----------------------------------------------------
                    CableTray tray2 =
                        CableTray.Create(
                            doc,
                            trayTypeId,
                            breakPt2,
                            end,
                            levelId);

                    //----------------------------------------------------
                    // FITTINGS
                    //----------------------------------------------------
                    ConnectWithElbow(doc, tray1, rise);
                    ConnectWithElbow(doc, rise, top);
                    ConnectWithElbow(doc, top, drop);
                    ConnectWithElbow(doc, drop, tray2);

                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static void ConnectWithElbow(
            Document doc,
            MEPCurve curve1,
            MEPCurve curve2)
        {
            Connector c1 =
                GetNearestConnector(curve1, curve2);

            Connector c2 =
                GetNearestConnector(curve2, curve1);

            if (c1 == null || c2 == null)
                return;

            try
            {
                doc.Create.NewElbowFitting(c1, c2);
            }
            catch
            {
            }
        }

        private static Connector GetNearestConnector(MEPCurve source,MEPCurve target)
        {
            double minDistance = double.MaxValue;
            Connector nearest = null;

            foreach (Connector c1 in source.ConnectorManager.Connectors)
            {
                foreach (Connector c2 in target.ConnectorManager.Connectors)
                {
                    double d =
                        c1.Origin.DistanceTo(c2.Origin);

                    if (d < minDistance)
                    {
                        minDistance = d;
                        nearest = c1;
                    }
                }
            }
            return nearest;
        }
    }

    public class CableTraySelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is CableTray;
        }

        public bool AllowReference(
            Reference reference,
            XYZ position)
        {
            return false;
        }
    }
}
*/
#endregion