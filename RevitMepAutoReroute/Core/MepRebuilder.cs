using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Core
{
    public class MepRebuilder
    {
        private readonly Document _doc;
        private readonly RerouteConfig _config;

        public MepRebuilder(Document doc, RerouteConfig config)
        {
            _doc = doc;
            _config = config;
        }

        public void ExecuteRebuild(MepNetworkNode network, List<ClashCluster> clusters)
        {
            MEPCurve target = network.MainCurve;
            LocationCurve locCurve = target.Location as LocationCurve;
            Line line = locCurve?.Curve as Line;
            if (line == null) return;

            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            double totalLen = line.Length;

            double thetaRad = _config.PreferredAngleDeg * (Math.PI / 180.0);

            // Check if clash cluster is within the proximity of End0 or End1 fitting
            bool clashAtEnd0 = clusters.Any(c => c.ParamStart <= 0.05);
            bool clashAtEnd1 = clusters.Any(c => c.ParamEnd >= totalLen - 0.05);

            if (network.End1Fitting != null && clashAtEnd1)
            {
                // SCENARIO: Clash near / on fitting (Elbow or Tee)
                ClashCluster lastCluster = clusters.Last();
                double deltaZ = lastCluster.DeltaZ;

                // 1. Move the fitting to elevated height
                XYZ moveVector = new XYZ(0, 0, deltaZ);
                ElementTransformUtils.MoveElement(_doc, network.End1Fitting.Id, moveVector);

                // 2. Disconnect and rebuild main incoming run with incline
                XYZ inclineStart = p0 + dir * Math.Max(0, lastCluster.ParamStart);
                XYZ elevatedStart = inclineStart + dir * (deltaZ / Math.Tan(thetaRad)) + new XYZ(0, 0, deltaZ);
                XYZ elevatedEnd = p1 + new XYZ(0, 0, deltaZ);

                locCurve.Curve = Line.CreateBound(p0, inclineStart);

                MEPCurve slantCurve = CreateMepCurveLike(target, inclineStart, elevatedStart);
                MEPCurve elevatedCurve = CreateMepCurveLike(target, elevatedStart, elevatedEnd);

                ConnectMepCurves(target, slantCurve);
                ConnectMepCurves(slantCurve, elevatedCurve);

                // Reconnect to elevated fitting
                Connector cElevatedEnd = GetNearestConnector(elevatedCurve, elevatedEnd);
                Connector cFittingPrimary = GetNearestConnector(network.End1Fitting, elevatedEnd);
                if (cElevatedEnd != null && cFittingPrimary != null)
                {
                    cElevatedEnd.ConnectTo(cFittingPrimary);
                }

                // 3. Step down each branch connected to the elevated fitting back to floor
                foreach (MEPCurve branch in network.BranchCurves)
                {
                    RerouteBranchToFloor(branch, deltaZ, thetaRad);
                }
            }
            else
            {
                // SCENARIO: In-line bridge rerouting (4-bend saddle offset)
                foreach (var cluster in clusters)
                {
                    double dZ = cluster.DeltaZ;
                    double lTrans = dZ / Math.Tan(thetaRad);

                    XYZ ptA = p0 + dir * cluster.ParamStart;
                    XYZ ptB = ptA + dir * lTrans + new XYZ(0, 0, dZ);
                    XYZ ptC = (p0 + dir * cluster.ParamEnd) - dir * lTrans + new XYZ(0, 0, dZ);
                    XYZ ptD = p0 + dir * cluster.ParamEnd;

                    locCurve.Curve = Line.CreateBound(p0, ptA);

                    MEPCurve cUp = CreateMepCurveLike(target, ptA, ptB);
                    MEPCurve cTop = CreateMepCurveLike(target, ptB, ptC);
                    MEPCurve cDown = CreateMepCurveLike(target, ptC, ptD);
                    MEPCurve cEnd = CreateMepCurveLike(target, ptD, p1);

                    ConnectMepCurves(target, cUp);
                    ConnectMepCurves(cUp, cTop);
                    ConnectMepCurves(cTop, cDown);
                    ConnectMepCurves(cDown, cEnd);
                }
            }
        }

        private void RerouteBranchToFloor(MEPCurve branch, double deltaZ, double thetaRad)
        {
            LocationCurve bLoc = branch.Location as LocationCurve;
            Line bLine = bLoc?.Curve as Line;
            if (bLine == null) return;

            XYZ bp0 = bLine.GetEndPoint(0); // Near fitting (elevated)
            XYZ bp1 = bLine.GetEndPoint(1); // Downstream original elevation
            XYZ bDir = (bp1 - bp0).Normalize();

            XYZ bElevatedStart = bp0 + new XYZ(0, 0, deltaZ);
            double lTrans = deltaZ / Math.Tan(thetaRad);
            XYZ bDeclineStart = bElevatedStart + bDir * _config.MinStraightSpoolFeet;
            XYZ bDeclineEnd = bDeclineStart + bDir * lTrans - new XYZ(0, 0, deltaZ);

            bLoc.Curve = Line.CreateBound(bElevatedStart, bDeclineStart);
            MEPCurve branchSlant = CreateMepCurveLike(branch, bDeclineStart, bDeclineEnd);
            MEPCurve branchDownstream = CreateMepCurveLike(branch, bDeclineEnd, bp1);

            ConnectMepCurves(branch, branchSlant);
            ConnectMepCurves(branchSlant, branchDownstream);
        }

        public MEPCurve CreateMepCurveLike(MEPCurve source, XYZ start, XYZ end)
        {
            ElementId typeId = source.GetTypeId();
            ElementId levelId = source.ReferenceLevel.Id;

            if (source is CableTray tray)
            {
                CableTray newTray = CableTray.Create(_doc, typeId, start, end, levelId);
                newTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).Set(
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble());
                newTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).Set(
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble());
                return newTray;
            }
            else if (source is Conduit conduit)
            {
                Conduit newConduit = Conduit.Create(_doc, typeId, start, end, levelId);
                newConduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).Set(
                    conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble());
                return newConduit;
            }

            throw new NotSupportedException($"Unsupported MEP Curve Type: {source.GetType().Name}");
        }

        public void ConnectMepCurves(MEPCurve curve1, MEPCurve curve2)
        {
            Connector c1 = null, c2 = null;
            double minDist = double.MaxValue;

            foreach (Connector a in curve1.ConnectorManager.Connectors)
            {
                foreach (Connector b in curve2.ConnectorManager.Connectors)
                {
                    double d = a.Origin.DistanceTo(b.Origin);
                    if (d < minDist)
                    {
                        minDist = d;
                        c1 = a;
                        c2 = b;
                    }
                }
            }

            if (c1 != null && c2 != null)
            {
                if (c1.Origin.DistanceTo(c2.Origin) > 1e-4)
                {
                    _doc.Create.NewElbowFitting(c1, c2);
                }
                else
                {
                    c1.ConnectTo(c2);
                }
            }
        }

        public Connector GetNearestConnector(Element elem, XYZ point)
        {
            ConnectorSet set = null;
            if (elem is MEPCurve mep) set = mep.ConnectorManager.Connectors;
            else if (elem is FamilyInstance fi && fi.MEPModel != null) set = fi.MEPModel.ConnectorManager.Connectors;

            if (set == null) return null;
            Connector closest = null;
            double minDist = double.MaxValue;

            foreach (Connector c in set)
            {
                double d = c.Origin.DistanceTo(point);
                if (d < minDist)
                {
                    minDist = d;
                    closest = c;
                }
            }
            return closest;
        }
    }
}
