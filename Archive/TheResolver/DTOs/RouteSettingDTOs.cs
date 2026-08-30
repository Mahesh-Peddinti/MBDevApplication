namespace TheResolver.DTOs
{
    public class RouteSettingDTOs
    {
        /// <summary>
        /// Entry/Exit angle.
        /// Typical values:
        /// 30,45,60
        /// </summary>
        public double BendAngle { get; set; } 

        /// <summary>
        /// Horizontal clearance from host.
        /// </summary>
        public double BendRadius { get; set; }

        /// <summary>
        /// Vertical rise.
        /// </summary>
        public double MinimumClearance { get; set; }

        /// <summary>
        /// Extra clearance above host.
        /// </summary>
        public double MinimumSideOffset { get; set; }

        /// <summary>
        /// Additional safety margin
        /// </summary>
        public double BendSafetyfactor { get; set; } = 1.05;
    }
}
