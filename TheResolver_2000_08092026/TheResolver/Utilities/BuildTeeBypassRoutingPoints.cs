using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheResolver.DTOs;

namespace TheResolver.Utilities
{
    public class BuildTeeBypassRoutingPoints
    {
        public List<XYZ> TeeBypassRoutingPoints (   ClashGroupInfoDTOs clashGroupInfo,
                                                    ClashInfoDTOs clashInfo,
                                                    List<FamilyInstance> tees,
                                                    RouteSettingDTOs settings,
                                                    out List<XYZ> routingPoints )
        {
            routingPoints = new List<XYZ>();
            //get the Tee's location
            foreach ( var tee in tees )
            {
                LocationPoint teeLocation = tee.Location as LocationPoint;
                XYZ teePoint = teeLocation.Point;
                if (teePoint != null && clashGroupInfo != null)
                {
                    if(!(clashGroupInfo.CenetrPoint.DistanceTo(teePoint) < settings.MinimumClearance))
                    {
                        //if the Tee is too close to the clash point, skip it
                        continue;
                    }

                    //get the direction of the Tee
                    XYZ teeDirection = tee.HandOrientation;

                    //get the direction of the main run
                    XYZ mainRunDirection = clashGroupInfo.CenetrPoint - teePoint;
                    mainRunDirection = mainRunDirection.Normalize();

                    //get the direction of the bypass
                    XYZ bypassDirection = mainRunDirection.CrossProduct(teeDirection);
                    bypassDirection = bypassDirection.Normalize();

                    //get the length of the bypass
                    double bypassLength = settings.MinimumClearance + settings.BendRadius * 2;

                    //get the routing points
                    XYZ routingPoint1 = teePoint + (bypassDirection * bypassLength);
                    XYZ routingPoint2 = routingPoint1 + (mainRunDirection * bypassLength);
                    XYZ routingPoint3 = routingPoint2 - (bypassDirection * bypassLength);
                    routingPoints.Add(routingPoint1);
                    routingPoints.Add(routingPoint2);
                    routingPoints.Add(routingPoint3);

                }                
            }

            return routingPoints;

        } 

        public List<RoutingpointsDTOs> ClashPointUtilities()
        {
            List<RoutingpointsDTOs> routingPointsList = new List<RoutingpointsDTOs>();


            return routingPointsList;
        }

        public class RoutingpointsDTOs
        {
            public List<XYZ> RoutingPoints { get; set; }
            public List<XYZ> BypassPoints { get; set; }
        }


    }
}
