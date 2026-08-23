using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.DTOs
{
    public class ClashInfoDTOs
    {
        public CableTray Tray { get; set; }
        public Conduit Conduit { get; set; }
        public Solid Intersection { get; set; }
    }
}
