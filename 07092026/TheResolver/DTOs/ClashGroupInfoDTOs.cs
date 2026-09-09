using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.DTOs
{
    public class ClashGroupInfoDTOs
    {
        public CableTray Tray { get; set; }        
        public Conduit Conduit { get; set; }
        public double AvaragePoint { get; set; } 
        public XYZ CenetrPoint { get; set; }
        public double ZMax { get; set; }
        public double XOffsetMin { get; set; }
        public double XOffsetMax { get; set; }
        public BoundingBoxXYZ ClashBoundingBox { get; set; }

    }
}
