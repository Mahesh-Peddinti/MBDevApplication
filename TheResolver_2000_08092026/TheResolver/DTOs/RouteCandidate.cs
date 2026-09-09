using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.DTOs
{
    public class RouteCandidate
    {
        public RouteType Type;

        public XYZ BreakStart;

        public XYZ BreakEnd;

        public List<XYZ> DetourPoints;

        public double RouteLength;

        public int BendCount;

        public double ElevationChange;

        public double Clearance;

        public double Cost;

        public bool IsValid;
    }

}
