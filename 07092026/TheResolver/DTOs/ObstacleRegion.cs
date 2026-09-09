using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.DTOs
{
    public class ObstacleRegion
    {
        public XYZ TrayDirection { get; set; }

        public XYZ TrayOrigin { get; set; }

        public XYZ BreakStart { get; set; }

        public XYZ BreakEnd { get; set; }

        public double MinStation { get; set; }

        public double MaxStation { get; set; }

        public double RegionLength { get; set; }

        public double RegionHeight { get; set; }

        public double RegionWidth { get; set; }

        public XYZ RegionCenter { get; set; }

        public List<Element> Obstacles { get; set; }
            = new List<Element>();

        public List<ObstacleProjection> Projections { get; set; }
            = new List<ObstacleProjection>();
    }
}
