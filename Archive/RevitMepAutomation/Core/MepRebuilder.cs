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

        public void ExecuteRebuild(CableTray originalTray, List<TrayPointSet> pointSets)
        {
            if (pointSets == null || pointSets.Count == 0) return;

            LocationCurve locCurve = originalTray.Location as LocationCurve;
            Line line = locCurve?.Curve as Line;
            if (line == null) return;

            XYZ p0 = line.GetEndPoint(0);
            XYZ pEnd = line.GetEndPoint(1);

            ElementId typeId = originalTray.GetTypeId();
            ElementId levelId = originalTray.ReferenceLevel.Id;
            double width = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble();
            double height = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble();

            // 1. Capture attached fittings at the ends before creating new segments
            AttachedFittingInfo fit0 = GetConnectedFitting(originalTray, p0, 0);
            AttachedFittingInfo fitEnd = GetConnectedFitting(originalTray, pEnd, 1);

            // 2. Build ordered path line segments across all clusters
            List<Line> pathLines = new List<Line>();
            XYZ currentPt = p0;

            for (int i = 0; i < pointSets.Count; i++)
            {
                TrayPointSet pts = pointSets[i];

                // Base approach: currentPt -> P1 (Horizontal)
                if (currentPt.DistanceTo(pts.P1) > 0.05)
                {
                    pathLines.Add(Line.CreateBound(currentPt, pts.P1));
                }

                // Incline slant: P1 -> P2 (Length = 2T + 10mm)
                if (pts.P1.DistanceTo(pts.P2) > 0.05)
                {
                    pathLines.Add(Line.CreateBound(pts.P1, pts.P2));
                }

                // Elevated horizontal bridge: P2 -> P3 (Length = 2T + 10mm)
                if (pts.P2.DistanceTo(pts.P3) > 0.05)
                {
                    pathLines.Add(Line.CreateBound(pts.P2, pts.P3));
                }

                // Decline slant: P3 -> P4 (Length = 2T + 10mm)
                if (pts.P3.DistanceTo(pts.P4) > 0.05)
                {
                    pathLines.Add(Line.CreateBound(pts.P3, pts.P4));
                }

                currentPt = pts.P4;
            }

            // Tail segment: currentPt -> pEnd (Horizontal)
            if (currentPt.DistanceTo(pEnd) > 0.05)
            {
                pathLines.Add(Line.CreateBound(currentPt, pEnd));
            }

            if (pathLines.Count == 0) return;

            // 3. Create new Cable Tray instances for each segment
            List<CableTray> newSegments = new List<CableTray>();
            foreach (Line pathLine in pathLines)
            {
                CableTray segment = CableTray.Create(_doc, typeId, pathLine.GetEndPoint(0), pathLine.GetEndPoint(1), levelId);
                segment.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).Set(width);
                segment.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).Set(height);
                newSegments.Add(segment);
            }

            // Regenerate document to update connectors for new segments
            _doc.Regenerate();

            // 4. Create Vertical Elbow fittings strictly between adjacent consecutive segment connectors
            for (int i = 0; i < newSegments.Count - 1; i++)
            {
                CableTray segA = newSegments[i];
                CableTray segB = newSegments[i + 1];

                LocationCurve lcA = segA.Location as LocationCurve;
                XYZ jointPt = lcA.Curve.GetEndPoint(1);

                Connector connA = GetEndpointConnector(segA, jointPt);
                Connector connB = GetEndpointConnector(segB, jointPt);

                if (connA != null && connB != null)
                {
                    try
                    {
                        // Creates native Revit Vertical Inside Bend / Vertical Outside Bend
                        _doc.Create.NewElbowFitting(connA, connB);
                    }
                    catch
                    {
                        try { connA.ConnectTo(connB); } catch { }
                    }
                }
            }

            // 5. Reconnect to original Start / End fittings
            if (fit0 != null && newSegments.Count > 0)
            {
                Connector firstStartConn = GetEndpointConnector(newSegments.First(), p0);
                if (firstStartConn != null && fit0.FittingConnector != null && !firstStartConn.IsConnected)
                {
                    try { firstStartConn.ConnectTo(fit0.FittingConnector); } catch { }
                }
            }

            if (fitEnd != null && newSegments.Count > 0)
            {
                Connector lastEndConn = GetEndpointConnector(newSegments.Last(), pEnd);
                if (lastEndConn != null && fitEnd.FittingConnector != null && !lastEndConn.IsConnected)
                {
                    try { lastEndConn.ConnectTo(fitEnd.FittingConnector); } catch { }
                }
            }

            // 6. Delete original clashing straight tray
            _doc.Delete(originalTray.Id);
        }

        private Connector GetEndpointConnector(CableTray tray, XYZ targetPt)
        {
            Connector closest = null;
            double minDist = double.MaxValue;

            foreach (Connector c in tray.ConnectorManager.Connectors)
            {
                double d = c.Origin.DistanceTo(targetPt);
                if (d < minDist)
                {
                    minDist = d;
                    closest = c;
                }
            }
            return (minDist < 0.2) ? closest : null;
        }

        private AttachedFittingInfo GetConnectedFitting(CableTray tray, XYZ endPt, int endIndex)
        {
            foreach (Connector c in tray.ConnectorManager.Connectors)
            {
                if (c.Origin.DistanceTo(endPt) < 0.1 && c.IsConnected)
                {
                    foreach (Connector linked in c.AllRefs)
                    {
                        if (linked.Owner is FamilyInstance fi)
                        {
                            return new AttachedFittingInfo
                            {
                                Fitting = fi,
                                FittingConnector = linked,
                                TrayEndIndex = endIndex
                            };
                        }
                    }
                }
            }
            return null;
        }
    }
}
