using System;
using System.Collections.Generic;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Core
{
    public class ClashClusterEngine
    {
        private readonly RerouteConfig _config;

        public ClashClusterEngine(RerouteConfig config)
        {
            _config = config;
        }

        public List<ClashCluster> ClusterClashes(List<ClashInfo> rawClashes)
        {
            List<ClashCluster> clusters = new List<ClashCluster>();
            if (rawClashes == null || rawClashes.Count == 0) return clusters;

            double thetaRad = _config.PreferredAngleDeg * (Math.PI / 180.0);

            ClashCluster current = null;

            foreach (var clash in rawClashes)
            {
                double transLen = clash.RequiredDeltaZ / Math.Tan(thetaRad);
                double breakMin = Math.Max(0, clash.ParamStart - transLen - _config.MinStraightSpoolFeet);
                double breakMax = clash.ParamEnd + transLen + _config.MinStraightSpoolFeet;

                if (current == null)
                {
                    current = new ClashCluster
                    {
                        ParamStart = breakMin,
                        ParamEnd = breakMax,
                        DeltaZ = clash.RequiredDeltaZ
                    };
                    current.Clashes.Add(clash);
                }
                else
                {
                    // If this clash falls within the transition range of the current cluster -> Merge
                    if (breakMin <= current.ParamEnd)
                    {
                        current.ParamEnd = Math.Max(current.ParamEnd, breakMax);
                        current.DeltaZ = Math.Max(current.DeltaZ, clash.RequiredDeltaZ);
                        current.Clashes.Add(clash);
                    }
                    else
                    {
                        clusters.Add(current);
                        current = new ClashCluster
                        {
                            ParamStart = breakMin,
                            ParamEnd = breakMax,
                            DeltaZ = clash.RequiredDeltaZ
                        };
                        current.Clashes.Add(clash);
                    }
                }
            }

            if (current != null) clusters.Add(current);
            return clusters;
        }
    }
}
