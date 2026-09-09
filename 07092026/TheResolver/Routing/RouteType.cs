using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.Routing
{
    /// <summary>
    /// High-level routing strategy.
    /// </summary>
    public enum RouteType
    {
        Unknown = 0,

        /// <summary>
        /// Route over the obstacle region.
        /// </summary>
        Over,

        /// <summary>
        /// Route below the obstacle region.
        /// </summary>
        Under,

        /// <summary>
        /// Route to the left side of tray direction.
        /// </summary>
        Left,

        /// <summary>
        /// Route to the right side of tray direction.
        /// </summary>
        Right,

        /// <summary>
        /// Combination of horizontal and vertical movements.
        /// </summary>
        Mixed,

        /// <summary>
        /// Future use for A* search.
        /// </summary>
        SolverGenerated
    }
}
