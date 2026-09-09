namespace TheResolver.DTOs
{
    /// <summary>
    /// Vertical side the bypass detour should run on.
    /// </summary>
    public enum RouteDirection
    {
        /// <summary>
        /// Let the geometry pick the side from where the obstruction sits
        /// relative to the tray. This is the original behaviour.
        /// </summary>
        Auto,

        /// <summary>
        /// Force the detour above the tray.
        /// </summary>
        Up,

        /// <summary>
        /// Force the detour below the tray.
        /// </summary>
        Down
    }

    public class RouteSettingDTOs
    {
        /// <summary>
        /// Entry/exit angle of the bend, in degrees.
        /// Must be greater than 0 and less than 90.
        /// Typical values: 30, 45, 60.
        /// </summary>
        public double BendAngle { get; set; }

        /// <summary>
        /// Bend radius, in Revit internal units (feet).
        /// </summary>
        public double BendRadius { get; set; }

        /// <summary>
        /// Vertical clearance kept between the tray and the obstruction,
        /// in Revit internal units (feet).
        /// </summary>
        public double MinimumClearance { get; set; }

        /// <summary>
        /// Horizontal margin kept either side of the obstruction before the
        /// detour starts, in Revit internal units (feet).
        /// </summary>
        public double MinimumSideOffset { get; set; }

        /// <summary>
        /// Unitless multiplier applied to the calculated bend rise.
        /// Do NOT scale this by the mm-to-feet factor.
        /// </summary>
        public double BendSafetyfactor { get; set; } = 1.05;

        /// <summary>
        /// Optional user override for the detour side.
        /// <see cref="RouteDirection.Auto"/> preserves the geometric decision.
        /// </summary>
        public RouteDirection PreferredDirection { get; set; } = RouteDirection.Auto;
    }
}
