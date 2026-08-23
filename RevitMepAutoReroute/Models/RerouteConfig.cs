namespace RevitMepAutomation.Models
{
    public class RerouteConfig
    {
        /// <summary>
        /// Safety buffer clearance above or below obstacle (in internal feet).
        /// Default: 50mm (~0.164 ft)
        /// </summary>
        public double ClearanceFeet { get; set; } = 50.0 / 304.8;

        /// <summary>
        /// Standard bend angle for vertical transitions in degrees (e.g., 30.0, 45.0).
        /// Default: 30 degrees.
        /// </summary>
        public double PreferredAngleDeg { get; set; } = 30.0;

        /// <summary>
        /// Minimum straight spool piece length between fittings and transition bends (in internal feet).
        /// Default: 100mm (~0.328 ft)
        /// </summary>
        public double MinStraightSpoolFeet { get; set; } = 100.0 / 304.8;

        /// <summary>
        /// When true, attempts to route over (upward); when false, routes under (downward).
        /// </summary>
        public bool PreferOver { get; set; } = true;
    }
}
