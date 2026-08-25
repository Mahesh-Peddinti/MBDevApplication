namespace RevitMepAutomation.Models
{
    public class RerouteConfig
    {
        /// <summary>
        /// 500mm reference distance from Clash Point to P1 / P4 (in internal feet).
        /// 500 mm = 1.64042 ft
        /// </summary>
        public double ClashOffsetRangeFeet { get; set; } = 500.0 / 304.8;

        /// <summary>
        /// 50mm clearance above the obstacle top (in internal feet).
        /// 50 mm = 0.16404 ft
        /// </summary>
        public double Clearance50mmFeet { get; set; } = 50.0 / 304.8;

        /// <summary>
        /// Minimum straight gap between consecutive fittings (in internal feet).
        /// 10 mm = 0.0328084 ft
        /// </summary>
        public double MinFittingGapFeet { get; set; } = 10.0 / 304.8;

        /// <summary>
        /// Default Cable Tray Bend Radius (default: 300mm = 0.98425 ft).
        /// </summary>
        public double DefaultBendRadiusFeet { get; set; } = 300.0 / 304.8;

        /// <summary>
        /// Transition horizontal run length (default: 200mm = 0.656168 ft).
        /// </summary>
        public double TransitionRunFeet { get; set; } = 200.0 / 304.8;
    }
}
