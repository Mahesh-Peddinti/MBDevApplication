using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace TheResolver.Routing
{
    /// <summary>
    /// Represents a complete routing solution.
    /// </summary>
    public class RouteCandidate
    {
        /// <summary>
        /// Route strategy.
        /// </summary>
        public RouteType RouteType { get; set; }

        /// <summary>
        /// Human readable name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional description shown in UI.
        /// </summary>
        public string Description { get; set; }

        //-------------------------------------------------------
        // ROUTING GEOMETRY
        //-------------------------------------------------------

        /// <summary>
        /// Start break point on original tray.
        /// </summary>
        public XYZ BreakStart { get; set; }

        /// <summary>
        /// End break point on original tray.
        /// </summary>
        public XYZ BreakEnd { get; set; }

        /// <summary>
        /// Route waypoints.
        /// Passed directly into CreateNewRoute().
        /// </summary>
        public List<XYZ> DetourPoints { get; set; }
            = new List<XYZ>();

        //-------------------------------------------------------
        // ENGINEERING DATA
        //-------------------------------------------------------

        /// <summary>
        /// Total route length.
        /// </summary>
        public double RouteLength { get; set; }

        /// <summary>
        /// Maximum rise above original tray.
        /// </summary>
        public double ElevationGain { get; set; }

        /// <summary>
        /// Maximum drop below original tray.
        /// </summary>
        public double ElevationDrop { get; set; }

        /// <summary>
        /// Maximum side offset.
        /// </summary>
        public double LateralOffset { get; set; }

        /// <summary>
        /// Number of bends.
        /// </summary>
        public int BendCount { get; set; }

        /// <summary>
        /// Smallest clearance to any obstacle.
        /// </summary>
        public double MinimumClearance { get; set; }

        //-------------------------------------------------------
        // VALIDATION
        //-------------------------------------------------------

        /// <summary>
        /// Route passed validation.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Collision free.
        /// </summary>
        public bool IsCollisionFree { get; set; }

        /// <summary>
        /// Validation warnings.
        /// </summary>
        public List<string> Warnings { get; set; }
            = new List<string>();

        /// <summary>
        /// Validation errors.
        /// </summary>
        public List<string> Errors { get; set; }
            = new List<string>();

        //-------------------------------------------------------
        // COLLISION RESULTS
        //-------------------------------------------------------

        /// <summary>
        /// Obstacles hit by the route.
        /// </summary>
        public List<ElementId> CollidingElementIds { get; set; }
            = new List<ElementId>();

        /// <summary>
        /// Number of collisions.
        /// </summary>
        public int CollisionCount
        {
            get
            {
                return CollidingElementIds.Count;
            }
        }

        //-------------------------------------------------------
        // SCORING
        //-------------------------------------------------------

        /// <summary>
        /// Final route score.
        /// Lower is better.
        /// </summary>
        public double Cost { get; set; }

        public double LengthCost { get; set; }

        public double BendCost { get; set; }

        public double ElevationCost { get; set; }

        public double ClearanceCost { get; set; }

        public double CollisionCost { get; set; }

        public RouteQuality Quality { get; set; }
            = RouteQuality.Unknown;

        //-------------------------------------------------------
        // PREVIEW SUPPORT
        //-------------------------------------------------------

        /// <summary>
        /// Polyline representation used by preview UI.
        /// </summary>
        public List<Line> PreviewSegments { get; set; }
            = new List<Line>();

        //-------------------------------------------------------
        // METADATA
        //-------------------------------------------------------

        public string GeneratedBy { get; set; }

        public string Notes { get; set; }

        //-------------------------------------------------------
        // HELPERS
        //-------------------------------------------------------

        public override string ToString()
        {
            return
                $"{RouteType} | " +
                $"Length={RouteLength:0.00} | " +
                $"Bends={BendCount} | " +
                $"Clearance={MinimumClearance:0.00} | " +
                $"Cost={Cost:0.00}";
        }
    }
}