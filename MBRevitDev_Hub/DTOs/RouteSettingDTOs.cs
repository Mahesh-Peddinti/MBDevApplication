using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.DTOs
{
    public class RouteSettingDTOs
    {
        // Required free space above the conduit.
        public double ClearanceMm{ get;set; } = 50.0;

        // Minimum vertical rise.
        public double MinimumRiseMm{ get;set; } = 100.0;

        // Minimum clash/geometry tolerance.
        public double PointToleranceMm{ get;set; } = 1.0;

        // First-stage routing supports horizontal trays in any XY direction.
        public  double HorizontalTrayZTolerance{ get;set; } = 0.01;

        // Horizontal minimum clearance for grouping clashes.
        // Clashes within this distance along the tray direction are grouped.
        public  double HorizontalMinimumClearanceMm { get;set; } = 300.0;

        // Minimum accepted clash volume.
        public double MinimumIntersectionVolume { get;set; } = 1e-9;
    }
}
