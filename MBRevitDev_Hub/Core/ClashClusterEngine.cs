using System;
using System.Collections.Generic;
using System.Linq;
using MBRevitDev_Hub.Models;

namespace MBRevitDev_Hub.Core
{
    public class ClashClusterEngine
    {
        private readonly RerouteConfig _config;

        public ClashClusterEngine(RerouteConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Groups clashes based on the 500mm reference range.
        /// If two clashes are within (2 * 500mm = 1000mm) of each other, merges them into a single cluster.
        /// </summary>
        public List<CableTrayClashCluster> ClusterClashes(List<CableTrayClashInfo> rawClashes, double totalLength)
        {
            List<CableTrayClashCluster> clusters = new List<CableTrayClashCluster>();
            if (rawClashes == null || rawClashes.Count == 0) return clusters;

            List<CableTrayClashInfo> sortedClashes = rawClashes.OrderBy(c => c.ParamCenter).ToList();
            HashSet<int> processed = new HashSet<int>();

            double offset500 = _config.ClashOffsetRangeFeet; // 500 mm in internal feet

            for (int i = 0; i < sortedClashes.Count; i++)
            {
                if (processed.Contains(i)) continue;

                CableTrayClashInfo seed = sortedClashes[i];
                processed.Add(i);

                List<CableTrayClashInfo> currentGroup = new List<CableTrayClashInfo> { seed };

                // The range around the clash point is [S - 500mm, S + 500mm]
                double rangeMin = seed.ParamCenter - offset500;
                double rangeMax = seed.ParamCenter + offset500;

                bool expanded;
                do
                {
                    expanded = false;
                    for (int j = 0; j < sortedClashes.Count; j++)
                    {
                        if (processed.Contains(j)) continue;

                        CableTrayClashInfo candidate = sortedClashes[j];

                        // If candidate clash falls inside the active [rangeMin, rangeMax] -> Group into cluster
                        if (candidate.ParamCenter >= rangeMin && candidate.ParamCenter <= rangeMax)
                        {
                            processed.Add(j);
                            currentGroup.Add(candidate);
                            expanded = true;

                            // Expand range to encompass 500mm before first clash and 500mm after last clash
                            rangeMin = currentGroup.Min(c => c.ParamCenter) - offset500;
                            rangeMax = currentGroup.Max(c => c.ParamCenter) + offset500;
                        }
                    }
                } while (expanded);

                double firstParam = currentGroup.Min(c => c.ParamCenter);
                double lastParam = currentGroup.Max(c => c.ParamCenter);
                double maxLift = currentGroup.Max(c => c.RequiredDeltaZ);

                clusters.Add(new CableTrayClashCluster
                {
                    FirstClashParam = firstParam,
                    LastClashParam = lastParam,
                    MaxDeltaZ = maxLift,
                    Clashes = currentGroup
                });
            }

            return clusters;
        }
    }
}
