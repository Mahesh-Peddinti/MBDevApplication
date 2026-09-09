using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.DTOs
{
    public class ObstacleProjection
    {
        public Element Element { get; set; }

        public double MinStation { get; set; }

        public double MaxStation { get; set; }

        public double MinElevation { get; set; }

        public double MaxElevation { get; set; }

        public BoundingBoxXYZ BoundingBox { get; set; }
    }
}
