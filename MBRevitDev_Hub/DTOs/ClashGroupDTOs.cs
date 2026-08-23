using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace MBRevitDev_Hub.DTOs
{
    public class ClashGroupDTOs
    {
        public ElementId TrayId { get; set; }
        public List<ClashInfoDTOs> Clashes { get; set; }
        public double MinStation { get; set; }
        public double MaxStation { get; set; }
        public XYZ ConsolidatedCentroid { get; set; }
    }
}
