using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.DTOs
{
    public class RouteDefinitionDTOs
    {
        public List<XYZ> Points { get; set; }  // P0, P1, P2, P3
        public double Rise { get; set; }
        public double Transition { get; set; }
        public double EntryStation { get; set; }
        public double ExitStation { get; set; }
    }
}
